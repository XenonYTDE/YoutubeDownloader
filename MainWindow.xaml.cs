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
        private readonly string _currentVersion = "1.1.0"; // Major update from 1.0.10
        private Settings _settings;
        private readonly string _settingsPath;
        private bool _isInitialized;
        private new AppWindow AppWindow { get; set; } = null!;

        public MainWindow()
        {
            try
            {
                Logger.Log("Starting application initialization");
                
                InitializeComponent();
                Logger.Log("InitializeComponent completed");
                
                Title = "YouTube Downloader";
                Logger.Log($"Current version: {_currentVersion}");
                
                // Setup dependencies path in AppData/Local
                _dependenciesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? "",
                    "YoutubeDownloader"
                );
                Directory.CreateDirectory(_dependenciesPath);
                Logger.Log($"Dependencies path created: {_dependenciesPath}");
                
                // Add settings initialization
                _settingsPath = Path.Combine(_dependenciesPath, "settings.json");
                _settings = LoadSettings();
                Logger.Log("Settings loaded");
                
                try
                {
                    // Set window size and title bar
                    Logger.Log("Initializing window handle");
                    IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    Logger.Log($"Window handle obtained: {hWnd}");
                    
                    WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                    Logger.Log($"Window ID obtained: {windowId}");
                    
                    AppWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                    Logger.Log("AppWindow obtained");
                    
                    if (AppWindow != null)
                    {
                        Logger.Log("Configuring window size and title bar");
                        // Set size and position
                        AppWindow.Resize(new SizeInt32 { Width = 800, Height = 600 });
                        
                        // Setup title bar
                        if (AppWindowTitleBar.IsCustomizationSupported())
                        {
                            var titleBar = AppWindow.TitleBar;
                            titleBar.ExtendsContentIntoTitleBar = false;
                            titleBar.IconShowOptions = IconShowOptions.ShowIconAndSystemMenu;
                            
                            var theme = _settings.Theme;
                            UpdateTitleBarColors(theme);
                            Logger.Log($"Title bar configured with theme: {theme}");
                        }
                        else
                        {
                            Logger.Log("Title bar customization not supported");
                        }
                    }
                    else
                    {
                        Logger.Log("WARNING: AppWindow is null");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Window initialization");
                }

                try
                {
                    // Initialize UI with settings
                    InitializeSettings();
                    Logger.Log("UI settings initialized");
                    
                    VersionText.Text = _currentVersion;
                    Logger.Log("Version text set");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "UI initialization");
                }

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

                // Create Start Menu shortcut
                CreateStartMenuShortcut();

                _isInitialized = true;
                Logger.Log("Application initialization completed");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "MainWindow constructor");
                throw; // Re-throw to see the error in Visual Studio
            }
        }

        private void UpdateTitleBarColors(string theme)
        {
            if (AppWindow?.TitleBar == null) return;
            
            var titleBar = AppWindow.TitleBar;
            if (theme == "Light")
            {
                titleBar.ButtonForegroundColor = Colors.Black;
                titleBar.ButtonHoverForegroundColor = Colors.Black;
                titleBar.ButtonPressedForegroundColor = Colors.Black;
                titleBar.ForegroundColor = Colors.Black;
            }
            else
            {
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonPressedForegroundColor = Colors.White;
                titleBar.ForegroundColor = Colors.White;
            }
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
                    : (!string.IsNullOrEmpty(_settings.DefaultDownloadPath)
                        ? _settings.DefaultDownloadPath
                        : (isMP4 
                            ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                            : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)));

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

                var options = new OptionSet();

                if (_settings.DownloadThumbnails)
                {
                    var thumbnailUrl = videoInfo.Data.Thumbnails?.LastOrDefault()?.Url;
                    if (!string.IsNullOrEmpty(thumbnailUrl))
                    {
                        await DownloadThumbnail(thumbnailUrl, safeTitle, outputPath);
                    }
                }

                if (_settings.DownloadSubtitles)
                {
                    options.WriteAutoSubs = true;
                    options.SubLangs = "en";
                    options.EmbedSubs = true;
                    options.ConvertSubs = "srt";
                }

                if (isMP4)
                {
                    string format = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best";
                    if (QualityComboBox.SelectedItem?.ToString() != "Best")
                    {
                        string quality = QualityComboBox.SelectedItem?.ToString()?.Replace("p", "") ?? "1080";
                        format = $"bestvideo[height<={quality}][ext=mp4]+bestaudio[ext=m4a]/best[height<={quality}][ext=mp4]";
                    }

                    options.Format = format;
                    options.Output = Path.Combine(outputPath, $"{safeTitle}.%(ext)s");
                    options.RestrictFilenames = true;
                    options.NoPlaylist = true;
                    options.PreferFreeFormats = true;

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
                    options.Format = "bestaudio/best";
                    options.ExtractAudio = true;
                    options.AudioFormat = AudioConversionFormat.Mp3;
                    options.Output = Path.Combine(outputPath, $"{safeTitle}.%(ext)s");
                    options.RestrictFilenames = true;
                    options.NoPlaylist = true;
                    options.PreferFreeFormats = true;

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
                UpdateStatus("Checking for updates...");
                var updateInfo = await _updateManager.CheckForUpdates();
                
                if (updateInfo == null)
                {
                    UpdateStatus("Failed to check for updates");
                    return;
                }

                if (updateInfo.Value.Available)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Update Available",
                        Content = new StackPanel
                        {
                            Spacing = 10,
                            Children =
                            {
                                new TextBlock 
                                { 
                                    Text = $"Version {updateInfo.Value.NewVersion} is available. Would you like to update now?",
                                    TextWrapping = TextWrapping.Wrap
                                },
                                new TextBlock
                                {
                                    Text = "What's New:",
                                    Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
                                    Margin = new Thickness(0, 8, 0, 0)
                                },
                                new ScrollViewer
                                {
                                    Content = new TextBlock
                                    {
                                        Text = updateInfo.Value.PatchNotes ?? "No patch notes available.",
                                        TextWrapping = TextWrapping.Wrap,
                                        Style = Application.Current.Resources["BodyTextBlockStyle"] as Style,
                                        Opacity = 0.8
                                    },
                                    MaxHeight = 200,
                                    HorizontalScrollMode = ScrollMode.Disabled,
                                    VerticalScrollMode = ScrollMode.Auto,
                                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                                    Margin = new Thickness(0, 0, 0, 8)
                                }
                            }
                        },
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
                            var errorDialog = new ContentDialog
                            {
                                Title = "Update Failed",
                                Content = "Failed to download or install the update. Check the Output window for details.\n\n" +
                                         $"Download URL: {updateInfo.Value.DownloadUrl}",
                                PrimaryButtonText = "OK",
                                XamlRoot = Content.XamlRoot
                            };
                            await errorDialog.ShowAsync();
                            UpdateStatus("Update failed. Please try again later.");
                        }
                    }
                }
                else
                {
                    UpdateStatus("No updates available");
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"Update check failed: {ex.Message}\nStack trace: {ex.StackTrace}";
                Debug.WriteLine(errorMessage);
                
                // Show error to user
                var errorDialog = new ContentDialog
                {
                    Title = "Update Check Failed",
                    Content = errorMessage,
                    PrimaryButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await errorDialog.ShowAsync();
                UpdateStatus($"Update check failed: {ex.Message}");
            }
        }

        private async void DeleteHistoryItem_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var item = (DownloadHistoryItem)button.CommandParameter;

            // Show confirmation dialog
            var dialog = new ContentDialog
            {
                Title = "Delete from History",
                Content = "Are you sure you want to delete this item from history?",
                PrimaryButtonText = "Delete",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _downloadHistory.Remove(item);
                SaveDownloadHistory();
            }
        }

        private async void ClearAllHistory_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadHistory.Count == 0)
            {
                var emptyDialog = new ContentDialog
                {
                    Title = "History Empty",
                    Content = "There are no items in the history to clear.",
                    PrimaryButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await emptyDialog.ShowAsync();
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Clear All History",
                Content = "Are you sure you want to clear all download history? This cannot be undone.",
                PrimaryButtonText = "Clear All",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _downloadHistory.Clear();
                SaveDownloadHistory();
            }
        }

        private void CreateStartMenuShortcut()
        {
            try
            {
                var startMenuPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs",
                    "YouTube Downloader.lnk"
                );

                if (!File.Exists(startMenuPath))
                {
                    var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (currentExePath == null) return;

                    var powershellScript = $@"
$WshShell = New-Object -comObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut('{startMenuPath}')
$Shortcut.TargetPath = '{currentExePath}'
$Shortcut.WorkingDirectory = '{Path.GetDirectoryName(currentExePath) ?? string.Empty}'
$Shortcut.Description = 'YouTube Video Downloader'
$Shortcut.IconLocation = '{currentExePath}'
$Shortcut.Save()";

                    var scriptPath = Path.Combine(_dependenciesPath, "createshortcut.ps1");
                    File.WriteAllText(scriptPath, powershellScript);

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };

                    using var process = Process.Start(startInfo);
                    process?.WaitForExit();

                    try { File.Delete(scriptPath); } catch { }
                    
                    Logger.Log($"Created Start Menu shortcut at: {startMenuPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "CreateStartMenuShortcut");
            }
        }

        private void InitializeSettings()
        {
            // Initialize quality options
            DefaultQualityComboBox.Items.Add("Best");
            DefaultQualityComboBox.Items.Add("1080p");
            DefaultQualityComboBox.Items.Add("720p");
            DefaultQualityComboBox.Items.Add("480p");
            DefaultQualityComboBox.Items.Add("360p");
            
            // Set UI elements from settings
            DefaultLocationBox.Text = _settings.DefaultDownloadPath;
            DefaultQualityComboBox.SelectedItem = _settings.DefaultQuality;
            MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTray;
            RememberPositionCheckBox.IsChecked = _settings.RememberWindowPosition;
            AutoUpdateDepsCheckBox.IsChecked = _settings.AutoUpdateDependencies;
            DownloadThumbnailsCheckBox.IsChecked = _settings.DownloadThumbnails;
            DownloadSubtitlesCheckBox.IsChecked = _settings.DownloadSubtitles;
            ThemeComboBox.SelectedItem = _settings.Theme;

            // Apply theme
            ApplyTheme(_settings.Theme);

            UpdateThemeIcon(_settings.Theme);
        }

        private Settings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "LoadSettings");
            }
            return new Settings();
        }

        private void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "SaveSettings");
            }
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            _settings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked ?? false;
            _settings.RememberWindowPosition = RememberPositionCheckBox.IsChecked ?? false;
            _settings.AutoUpdateDependencies = AutoUpdateDepsCheckBox.IsChecked ?? false;
            _settings.DownloadThumbnails = DownloadThumbnailsCheckBox.IsChecked ?? false;
            _settings.DownloadSubtitles = DownloadSubtitlesCheckBox.IsChecked ?? false;
            _settings.DefaultQuality = DefaultQualityComboBox.SelectedItem?.ToString() ?? "Best";
            
            SaveSettings();
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            var theme = ThemeComboBox.SelectedItem?.ToString() ?? "System";
            _settings.Theme = theme;
            ApplyTheme(theme);
            SaveSettings();
        }

        private void ApplyTheme(string theme)
        {
            if (Content is FrameworkElement rootElement)
            {
                // Set the app's theme
                rootElement.RequestedTheme = theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };

                // Update title bar colors
                if (AppWindowTitleBar.IsCustomizationSupported())
                {
                    var titleBar = AppWindow.TitleBar;
                    if (theme == "Light")
                    {
                        titleBar.ButtonForegroundColor = Colors.Black;
                        titleBar.ButtonHoverForegroundColor = Colors.Black;
                        titleBar.ButtonPressedForegroundColor = Colors.Black;
                        titleBar.ForegroundColor = Colors.Black;
                    }
                    else // Dark or System (when system is in dark mode)
                    {
                        titleBar.ButtonForegroundColor = Colors.White;
                        titleBar.ButtonHoverForegroundColor = Colors.White;
                        titleBar.ButtonPressedForegroundColor = Colors.White;
                        titleBar.ForegroundColor = Colors.White;
                    }
                }

                // Save the theme preference
                _settings.Theme = theme;
                SaveSettings();
            }
        }

        private async void DefaultLocationBrowse_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            folderPicker.FileTypeFilter.Add("*");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                DefaultLocationBox.Text = folder.Path;
                _settings.DefaultDownloadPath = folder.Path;
                SaveSettings();
            }
        }

        private async Task DownloadThumbnail(string url, string videoTitle, string outputPath)
        {
            try
            {
                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync(url);
                
                var thumbnailFileName = $"{videoTitle}_thumbnail.jpg";
                var safeThumbnailName = string.Join("_", thumbnailFileName.Split(Path.GetInvalidFileNameChars()));
                var thumbnailPath = Path.Combine(outputPath, safeThumbnailName);

                await File.WriteAllBytesAsync(thumbnailPath, bytes);
                Logger.Log($"Thumbnail saved to: {thumbnailPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "DownloadThumbnail");
            }
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            var currentTheme = _settings.Theme;
            var newTheme = currentTheme switch
            {
                "Light" => "Dark",
                "Dark" => "Light",
                _ => "Light" // If system, start with light
            };

            ApplyTheme(newTheme);
            UpdateThemeIcon(newTheme);
        }

        private void UpdateThemeIcon(string theme)
        {
            ThemeIcon.Glyph = theme == "Light" ? "\uE793" : "\uE708";  // Sun/Moon icon
        }
    }

    public class DownloadHistoryItem
    {
        public string Title { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTime DateTime { get; set; }
    }

    public static class Extensions
    {
        public static T Apply<T>(this T obj, Action<T> action)
        {
            action(obj);
            return obj;
        }
    }
} 