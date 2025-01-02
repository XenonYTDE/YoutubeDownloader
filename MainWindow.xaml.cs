using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;
using System.Net.Http;
using System.Linq;
using System.IO.Compression;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using YoutubeDownloader;

namespace YoutubeDownloader
{
    public sealed partial class MainWindow : Window
    {
        private bool _isDownloading;
        private readonly YoutubeDL _youtubeDl;
        private readonly string _dependenciesPath;
        private ObservableCollection<DownloadHistoryItem> _downloadHistory;
        private readonly string _historyFilePath;
        private string _lastUrl = string.Empty;
        private readonly UpdateManager _updateManager;
        private readonly string _currentVersion = "1.0.1"; // Changed from 1.0.0

        public MainWindow()
        {
            InitializeComponent();
            Title = "YouTube Downloader";
            
            // Set window size
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            
            // Set size and position
            appWindow.Resize(new SizeInt32 { Width = 800, Height = 600 });
            
            // Setup dependencies path in AppData/Local
            _dependenciesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YoutubeDownloader"
            );
            Directory.CreateDirectory(_dependenciesPath);
            
            // Initialize history
            _historyFilePath = Path.Combine(_dependenciesPath, "download_history.json");
            _downloadHistory = LoadDownloadHistory();
            HistoryListView.ItemsSource = _downloadHistory;
            
            // Initialize YoutubeDL with full paths
            _youtubeDl = new YoutubeDL();
            _youtubeDl.YoutubeDLPath = Path.Combine(_dependenciesPath, "yt-dlp.exe");
            _youtubeDl.FFmpegPath = Path.Combine(_dependenciesPath, "ffmpeg.exe");
            
            InitializeQualityOptions();
            
            // Download dependencies if needed
            _ = EnsureDependenciesExist();

            _updateManager = new UpdateManager(_currentVersion, _dependenciesPath);
            _ = CheckForUpdatesAsync();
        }

        private void InitializeQualityOptions()
        {
            QualityComboBox.Items.Add("Best");
            QualityComboBox.Items.Add("1080p");
            QualityComboBox.Items.Add("720p");
            QualityComboBox.Items.Add("480p");
            QualityComboBox.Items.Add("360p");
            QualityComboBox.SelectedIndex = 0;
        }

        private async Task DownloadVideo(bool isMP4)
        {
            try
            {
                ClearPreview();
                _isDownloading = true;
                UpdateStatus("Starting download...");
                DownloadProgress.Value = 0;

                var url = UrlTextBox.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    UpdateStatus("Please enter a valid YouTube URL");
                    return;
                }

                // Get video info first to get the title
                var videoInfo = await _youtubeDl.RunVideoDataFetch(url);
                if (!videoInfo.Success)
                {
                    UpdateStatus("Failed to get video information");
                    return;
                }

                // Create a safe filename from the title
                var safeTitle = string.Join("_", videoInfo.Data.Title.Split(Path.GetInvalidFileNameChars()));

                string outputPath = !string.IsNullOrEmpty(LocationTextBox.Text)
                    ? LocationTextBox.Text
                    : (isMP4 
                        ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

                // Configure download options with specific output template
                _youtubeDl.OutputFolder = outputPath;
                _youtubeDl.OutputFileTemplate = $"{safeTitle}.%(ext)s";

                var progress = new Progress<DownloadProgress>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        DownloadProgress.Value = p.Progress * 100;
                        UpdateStatus($"Downloading: {(p.Progress * 100):F1}%");
                    });
                });

                if (!File.Exists(_youtubeDl.FFmpegPath))
                {
                    UpdateStatus("FFmpeg not found. Trying to download...");
                    await EnsureDependenciesExist();
                    
                    if (!File.Exists(_youtubeDl.FFmpegPath))
                    {
                        UpdateStatus("Failed to setup FFmpeg. Please try restarting the application.");
                        return;
                    }
                }

                if (isMP4)
                {
                    string format = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best";
                    if (QualityComboBox.SelectedItem?.ToString() != "Best")
                    {
                        string quality = QualityComboBox.SelectedItem?.ToString()?.Replace("p", "") ?? "1080";
                        format = $"bestvideo[height<={quality}][ext=mp4]+bestaudio[ext=m4a]/best[height<={quality}][ext=mp4]";
                    }

                    var options = new OptionSet
                    {
                        Format = format,
                        Output = Path.Combine(outputPath, $"{safeTitle}.%(ext)s"),
                        RestrictFilenames = true,
                        NoPlaylist = true,
                        PreferFreeFormats = true
                    };

                    var result = await _youtubeDl.RunVideoDownload(
                        url,
                        format,
                        DownloadMergeFormat.Mp4,
                        progress: progress
                    );

                    if (result.Success && File.Exists(result.Data))
                    {
                        try
                        {
                            File.SetCreationTime(result.Data, DateTime.Now);
                            File.SetLastWriteTime(result.Data, DateTime.Now);
                            File.SetLastAccessTime(result.Data, DateTime.Now);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to update timestamps: {ex.Message}");
                        }
                    }

                    HandleResult(result, videoInfo.Data.Title, true);
                }
                else
                {
                    var options = new OptionSet
                    {
                        Format = "bestaudio/best",
                        ExtractAudio = true,
                        AudioFormat = AudioConversionFormat.Mp3,
                        Output = Path.Combine(outputPath, $"{safeTitle}.%(ext)s"),
                        RestrictFilenames = true,
                        NoPlaylist = true,
                        PreferFreeFormats = true
                    };

                    // First download as best audio
                    var result = await _youtubeDl.RunVideoDownload(
                        url,
                        "bestaudio/best",
                        DownloadMergeFormat.Mkv,
                        progress: progress
                    );

                    if (result.Success && File.Exists(result.Data))
                    {
                        try
                        {
                            var mp3Path = Path.Combine(
                                Path.GetDirectoryName(result.Data)!,
                                Path.GetFileNameWithoutExtension(result.Data) + ".mp3"
                            );

                            // Add debug information
                            System.Diagnostics.Debug.WriteLine($"Original file: {result.Data}");
                            System.Diagnostics.Debug.WriteLine($"Target MP3 path: {mp3Path}");

                            if (!result.Data.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                            {
                                if (File.Exists(mp3Path))
                                {
                                    File.Delete(mp3Path);
                                }

                                // Use FFmpeg directly to convert to MP3
                                var process = new System.Diagnostics.Process
                                {
                                    StartInfo = new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = _youtubeDl.FFmpegPath,
                                        Arguments = $"-i \"{result.Data}\" -vn -ar 44100 -ac 2 -b:a 192k \"{mp3Path}\"",
                                        UseShellExecute = false,
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true,
                                        CreateNoWindow = true
                                    }
                                };

                                process.Start();
                                await process.WaitForExitAsync();

                                // Delete the original file after conversion
                                if (File.Exists(mp3Path))
                                {
                                    File.Delete(result.Data);
                                    result = new YoutubeDLSharp.RunResult<string>(true, Array.Empty<string>(), mp3Path);
                                }
                                else
                                {
                                    throw new Exception("FFmpeg conversion failed");
                                }
                            }

                            File.SetCreationTime(result.Data, DateTime.Now);
                            File.SetLastWriteTime(result.Data, DateTime.Now);
                            File.SetLastAccessTime(result.Data, DateTime.Now);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to update file: {ex.Message}");
                            UpdateStatus($"Error converting file: {ex.Message}");
                        }
                    }

                    HandleResult(result, videoInfo.Data.Title, false);
                }

                if (videoInfo.Success)
                {
                    // Show video title
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        VideoTitleText.Text = videoInfo.Data.Title;
                        VideoTitleText.Visibility = Visibility.Visible;
                    });

                    // Load thumbnail if available
                    if (!string.IsNullOrEmpty(videoInfo.Data.Thumbnails?.LastOrDefault()?.Url))
                    {
                        await LoadThumbnail(videoInfo.Data.Thumbnails.Last().Url);
                    }
                }
            }
            catch (Exception ex)
            {
                ClearPreview();
                UpdateStatus($"Error: {ex.Message}");
            }
            finally
            {
                _isDownloading = false;
            }
        }

        private void HandleResult(YoutubeDLSharp.RunResult<string> result, string title = null, bool isMP4 = false)
        {
            if (result.Success && result.Data != null)
            {
                if (File.Exists(result.Data))
                {
                    UpdateStatus($"Download completed! File saved to: {result.Data}");
                    if (title != null)  // Only add to history if we have a title
                    {
                        AddToHistory(title, result.Data, isMP4);
                    }
                }
                else
                {
                    UpdateStatus("Download completed but file not found");
                }
            }
            else
            {
                var errorMessage = result.ErrorOutput?.FirstOrDefault() ?? "Unknown error";
                if (result.Data != null)
                {
                    errorMessage += $"\nData: {result.Data}";
                }
                
                // Add debug information
                System.Diagnostics.Debug.WriteLine($"FFmpeg path: {_youtubeDl.FFmpegPath}");
                System.Diagnostics.Debug.WriteLine($"FFmpeg exists: {File.Exists(_youtubeDl.FFmpegPath)}");
                System.Diagnostics.Debug.WriteLine($"yt-dlp path: {_youtubeDl.YoutubeDLPath}");
                System.Diagnostics.Debug.WriteLine($"yt-dlp exists: {File.Exists(_youtubeDl.YoutubeDLPath)}");
                
                UpdateStatus($"Download failed: {errorMessage}");
                System.Diagnostics.Debug.WriteLine($"Full error: {errorMessage}");
            }
        }

        private async void DownloadMP4Button_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading)
            {
                UpdateStatus("A download is already in progress");
                return;
            }

            await DownloadVideo(true);
        }

        private async void DownloadMP3Button_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading)
            {
                UpdateStatus("A download is already in progress");
                return;
            }

            await DownloadVideo(false);
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            folderPicker.FileTypeFilter.Add("*");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                LocationTextBox.Text = folder.Path;
            }
        }

        private void UpdateStatus(string message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StatusText.Text = message;
            });
        }

        private async Task EnsureDependenciesExist()
        {
            try
            {
                if (!File.Exists(_youtubeDl.YoutubeDLPath))
                {
                    UpdateStatus("Downloading yt-dlp...");
                    await DownloadFile(
                        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
                        _youtubeDl.YoutubeDLPath
                    );
                }

                if (!File.Exists(_youtubeDl.FFmpegPath))
                {
                    UpdateStatus("Downloading FFmpeg...");
                    var ffmpegZipPath = Path.Combine(_dependenciesPath, "ffmpeg.zip");
                    
                    // Download FFmpeg zip
                    await DownloadFile(
                        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip",
                        ffmpegZipPath
                    );

                    UpdateStatus("Extracting FFmpeg...");
                    using (var archive = ZipFile.OpenRead(ffmpegZipPath))
                    {
                        var ffmpegEntry = archive.Entries
                            .FirstOrDefault(e => e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) && 
                                                !e.FullName.Contains("_"));

                        if (ffmpegEntry != null)
                        {
                            try
                            {
                                ffmpegEntry.ExtractToFile(_youtubeDl.FFmpegPath, true);
                                UpdateStatus("FFmpeg extracted successfully");
                            }
                            catch (Exception ex)
                            {
                                UpdateStatus($"Error extracting FFmpeg: {ex.Message}");
                            }
                        }
                        else
                        {
                            UpdateStatus("Could not find ffmpeg.exe in the downloaded archive");
                        }
                    }

                    // Clean up zip file
                    try
                    {
                        File.Delete(ffmpegZipPath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }

                UpdateStatus("Ready to download videos!");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error setting up dependencies: {ex.Message}");
            }
        }

        private async Task DownloadFile(string url, string destinationPath)
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var fs = new FileStream(destinationPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var filePath = (string)button.CommandParameter;
            if (File.Exists(filePath))
            {
                var argument = $"/select,\"{filePath}\"";
                Process.Start("explorer.exe", argument);
            }
        }

        private ObservableCollection<DownloadHistoryItem> LoadDownloadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    var items = JsonSerializer.Deserialize<List<DownloadHistoryItem>>(json);
                    return new ObservableCollection<DownloadHistoryItem>(items ?? new List<DownloadHistoryItem>());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading history: {ex.Message}");
            }
            
            return new ObservableCollection<DownloadHistoryItem>();
        }

        private void SaveDownloadHistory()
        {
            try
            {
                var json = JsonSerializer.Serialize(_downloadHistory);
                File.WriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving history: {ex.Message}");
            }
        }

        private void AddToHistory(string title, string filePath, bool isMP4)
        {
            var item = new DownloadHistoryItem
            {
                Title = title,
                FilePath = filePath,
                Type = isMP4 ? "MP4" : "MP3",
                DateTime = DateTime.Now
            };
            
            _downloadHistory.Insert(0, item);
            SaveDownloadHistory();
        }

        private async Task LoadThumbnail(string url)
        {
            try
            {
                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync(url);
                
                var image = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    var randomAccessStream = await ConvertToRandomAccessStream(ms);
                    await image.SetSourceAsync(randomAccessStream);
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    ThumbnailImage.Source = image;
                    ThumbnailImage.Visibility = Visibility.Visible;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading thumbnail: {ex.Message}");
            }
        }

        private async Task<IRandomAccessStream> ConvertToRandomAccessStream(MemoryStream memoryStream)
        {
            var randomAccessStream = new InMemoryRandomAccessStream();
            var outputStream = randomAccessStream.GetOutputStreamAt(0);
            var dw = new DataWriter(outputStream);
            var task = Task.Run(() => dw.WriteBytes(memoryStream.ToArray()));
            await task;
            await dw.StoreAsync();
            await outputStream.FlushAsync();
            return randomAccessStream;
        }

        private void ClearPreview()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ThumbnailImage.Source = null;
                ThumbnailImage.Visibility = Visibility.Collapsed;
                VideoTitleText.Text = string.Empty;
                VideoTitleText.Visibility = Visibility.Collapsed;
            });
        }

        private async void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url) || url == _lastUrl)
                return;

            _lastUrl = url;
            try
            {
                var videoInfo = await _youtubeDl.RunVideoDataFetch(url);
                if (videoInfo.Success)
                {
                    // Show video title
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        VideoTitleText.Text = videoInfo.Data.Title;
                        VideoTitleText.Visibility = Visibility.Visible;
                    });

                    // Load thumbnail if available
                    if (!string.IsNullOrEmpty(videoInfo.Data.Thumbnails?.LastOrDefault()?.Url))
                    {
                        await LoadThumbnail(videoInfo.Data.Thumbnails.Last().Url);
                    }
                }
                else
                {
                    ClearPreview();
                }
            }
            catch
            {
                ClearPreview();
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var updateInfo = await _updateManager.CheckForUpdates();
                if (updateInfo?.Available == true)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Update Available",
                        Content = $"Version {updateInfo.Value.NewVersion} is available. Would you like to update now?",
                        PrimaryButtonText = "Update",
                        SecondaryButtonText = "Later",
                        XamlRoot = Content.XamlRoot
                    };

                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        UpdateStatus("Downloading update...");
                        if (await _updateManager.DownloadAndInstallUpdate(updateInfo.Value.DownloadUrl))
                        {
                            var restartDialog = new ContentDialog
                            {
                                Title = "Update Ready",
                                Content = "The update has been downloaded. The application will now restart.",
                                PrimaryButtonText = "OK",
                                XamlRoot = Content.XamlRoot
                            };
                            await restartDialog.ShowAsync();
                            Application.Current.Exit();
                        }
                        else
                        {
                            UpdateStatus("Update failed. Please try again later.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
            }
        }
    }

    public class DownloadHistoryItem
    {
        public string Title { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTime DateTime { get; set; }
    }
} 