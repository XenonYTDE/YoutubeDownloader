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
using System.Runtime.InteropServices;
using System.Threading;
using FFMpegCore;
using System.Text;
using VideoLibrary;
using FFMpegCore.Enums;

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
        private readonly string _currentVersion = "1.1.12";
        private Settings _settings;
        private readonly string _settingsPath;
        private bool _isInitialized;
        private new AppWindow AppWindow { get; set; } = null!;
        private bool _isMP3Mode = false;
        private DateTime _downloadStartTime;

        // Add these properties for binding
        public bool IsVideoMode => !_isMP3Mode;
        public bool IsAudioMode => _isMP3Mode;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private string FormatFileSize(double bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            while (bytes >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytes = bytes / 1024;
            }
            return $"{bytes:0.##} {sizes[order]}";
        }

        private string FormatTimeRemaining(TimeSpan? timeSpan)
        {
            if (!timeSpan.HasValue)
                return "";
            
            var ts = timeSpan.Value;
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        public MainWindow()
        {
            try
            {
                Logger.Log("Starting application initialization");
                
                InitializeComponent();
                
                // Initialize mode and buttons right away
                _isMP3Mode = false;
                UpdateModeBindings();
                
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
                            Logger.Log("Title bar configured");
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
                    Logger.Log($"Version text set to: {_currentVersion}");
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

            var ffmpegPath = Path.Combine(_dependenciesPath, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                Logger.Log($"FFmpeg not found at: {ffmpegPath}");
            }
            else
            {
                Logger.Log($"FFmpeg found at: {ffmpegPath}");
            }
        }

        private void InitializeQualityOptions()
        {
            try
            {
                Logger.Log("Initializing quality options...");
                
                // Video quality options
                QualityComboBox.Items.Clear();  // Clear first to avoid duplicates
                QualityComboBox.Items.Add("Best");
                QualityComboBox.Items.Add("1080p");
                QualityComboBox.Items.Add("720p");
                QualityComboBox.Items.Add("480p");
                QualityComboBox.Items.Add("360p");
                
                // Set default video quality from settings
                var defaultQualityItem = QualityComboBox.Items.Cast<string>()
                    .FirstOrDefault(x => x == _settings.DefaultVideoQuality) ?? "1080p";
                QualityComboBox.SelectedItem = defaultQualityItem;
                
                Logger.Log($"Video quality set to: {defaultQualityItem}");

                // Set initial state for video mode
                FormatPanel.Visibility = Visibility.Visible;
                QualityComboBox.Visibility = Visibility.Visible;
                AudioControlsPanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "InitializeQualityOptions");
            }
        }

        private void LogControlHierarchy()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Control Hierarchy:");
                LogControl(AudioControlsPanel, sb, 0);
                Logger.Log(sb.ToString());
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "LogControlHierarchy");
            }
        }

        private void LogControl(UIElement element, StringBuilder sb, int depth)
        {
            if (element == null) return;

            var indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}+ {element.GetType().Name} - Visibility: {element.Visibility}");

            if (element is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                {
                    LogControl(child, sb, depth + 1);
                }
            }
        }

        private async Task DownloadVideo(bool isMP4)
        {
            if (_isDownloading)
            {
                UpdateStatus("A download is already in progress");
                return;
            }

            try
            {
                _isDownloading = true;
                _downloadStartTime = DateTime.Now;
                UpdateStatus("Starting download...");
                DownloadProgress.Value = 0;
                SpeedText.Text = "";
                TimeRemainingText.Text = "";

                var url = UrlTextBox.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    UpdateStatus("Please enter a URL");
                    return;
                }

                string outputPath = !string.IsNullOrEmpty(LocationTextBox.Text)
                    ? LocationTextBox.Text
                    : (isMP4 ? _settings.DefaultVideoDownloadPath : _settings.DefaultAudioDownloadPath);

                if (string.IsNullOrEmpty(outputPath))
                {
                    outputPath = isMP4 
                        ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                }

                Logger.Log($"Starting {(isMP4 ? "video" : "audio")} download for URL: {url}");

                // Configure basic download options
                var options = new OptionSet
                {
                    Output = Path.Combine(outputPath, "%(title)s.%(ext)s"),
                    RestrictFilenames = true,
                    NoPlaylist = true,
                    ForceOverwrites = true,
                    ExtractAudio = !isMP4,
                    AudioFormat = !isMP4 ? AudioConversionFormat.Mp3 : AudioConversionFormat.Best,
                    AudioQuality = (byte)0,
                    Format = isMP4 ? "best" : "bestaudio",
                    NoCheckCertificates = true
                };

                // Add minimal required options
                options.AddCustomOption("--ffmpeg-location", _youtubeDl.FFmpegPath);
                options.AddCustomOption("--no-check-certificate", true);
                options.AddCustomOption("--no-cache-dir", true);
                options.AddCustomOption("--no-part", true);

                var progress = new Progress<DownloadProgress>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (p != null)
                        {
                            DownloadProgress.Value = p.Progress * 100;
                            UpdateStatus($"Downloading: {(p.Progress * 100):F1}%");
                            SpeedText.Text = p.DownloadSpeed?.ToString() ?? "";
                            
                            var elapsed = DateTime.Now - _downloadStartTime;
                            TimeRemainingText.Text = $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                        }
                    });
                });

                // Run the download
                var result = isMP4
                    ? await _youtubeDl.RunVideoDownload(url, progress: progress, overrideOptions: options)
                    : await _youtubeDl.RunAudioDownload(url, AudioConversionFormat.Mp3, progress: progress, overrideOptions: options);

                if (result.Success)
                {
                    UpdateStatus("Download completed!");
                    Logger.Log($"Download successful: {result.Data}");
                    
                    if (!string.IsNullOrEmpty(result.Data))
                    {
                        var historyItem = new DownloadHistoryItem
                        {
                            Title = Path.GetFileNameWithoutExtension(result.Data),
                            Url = url,
                            FilePath = result.Data,
                            IsMP4 = isMP4,
                            DownloadDate = DateTime.Now
                        };
                        _downloadHistory.Insert(0, historyItem);
                        SaveDownloadHistory();
                    }
                }
                else
                {
                    var error = result.ErrorOutput?.FirstOrDefault() ?? "Unknown error";
                    Logger.LogError(new Exception(error), $"{(isMP4 ? "Video" : "Audio")} download failed");
                    throw new Exception($"Download failed: {error}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "DownloadVideo");
                UpdateStatus($"Error: {ex.Message}");
            }
            finally
            {
                _isDownloading = false;
            }
        }

        private void HandleResult(YoutubeDLSharp.RunResult<string> result, string? title = null, bool isMP4 = false)
        {
            if (result.Success && result.Data != null)
            {
                if (File.Exists(result.Data))
                {
                    UpdateStatus($"Download completed! File saved to: {result.Data}");
                    if (title != null)  // Only add to history if we have a title
                    {
                        var historyItem = new DownloadHistoryItem
                        {
                            Title = title,
                            FilePath = result.Data,
                            IsMP4 = isMP4,
                            DownloadDate = DateTime.Now,
                            Url = UrlTextBox.Text.Trim()
                        };
                        _downloadHistory.Insert(0, historyItem);
                        SaveDownloadHistory();
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

        private async void DownloadMP3Button_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMP3Mode)
            {
                UpdateStatus("Please switch to audio mode first");
                return;
            }
            await DownloadVideo(false);
        }

        private async void DownloadMP4Button_Click(object sender, RoutedEventArgs e)
        {
            if (_isMP3Mode)
            {
                UpdateStatus("Please switch to video mode first");
                return;
            }
            await DownloadVideo(true);
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
            VideoTitleText.Visibility = Visibility.Collapsed;
            ThumbnailImage.Visibility = Visibility.Collapsed;
            ClearProgressInfo();
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
                // Check for updater in AppData
                var updaterPath = Path.Combine(_dependenciesPath, "Updater.exe");

                if (!File.Exists(updaterPath))
                {
                    Logger.Log("Updater not found, skipping update check");
                    return;
                }

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
                Logger.LogError(ex, "CheckForUpdates");
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
            try
            {
                // Initialize audio format options
                DefaultAudioFormatComboBox.Items.Clear();
                DefaultAudioFormatComboBox.Items.Add(new ComboBoxItem { Content = "MP3" });
                DefaultAudioFormatComboBox.Items.Add(new ComboBoxItem { Content = "WAV" });
                DefaultAudioFormatComboBox.Items.Add(new ComboBoxItem { Content = "AAC" });
                DefaultAudioFormatComboBox.Items.Add(new ComboBoxItem { Content = "M4A" });
                DefaultAudioFormatComboBox.Items.Add(new ComboBoxItem { Content = "FLAC" });

                // Initialize audio quality options
                DefaultAudioQualityComboBox.Items.Clear();
                DefaultAudioQualityComboBox.Items.Add(new ComboBoxItem { Content = "320 kbps" });
                DefaultAudioQualityComboBox.Items.Add(new ComboBoxItem { Content = "256 kbps" });
                DefaultAudioQualityComboBox.Items.Add(new ComboBoxItem { Content = "192 kbps" });
                DefaultAudioQualityComboBox.Items.Add(new ComboBoxItem { Content = "128 kbps" });
                
                // Set UI elements from settings
                DefaultVideoLocationBox.Text = _settings.DefaultVideoDownloadPath;
                DefaultAudioLocationBox.Text = _settings.DefaultAudioDownloadPath;
                DefaultLocationBox.Text = _settings.DefaultDownloadPath;
                RememberPositionCheckBox.IsChecked = _settings.RememberWindowPosition;
                AutoUpdateDepsCheckBox.IsChecked = _settings.AutoUpdateDependencies;
                DownloadThumbnailsCheckBox.IsChecked = _settings.DownloadThumbnails;
                DownloadSubtitlesCheckBox.IsChecked = _settings.DownloadSubtitles;

                // Set default audio format and quality
                var audioFormatItem = DefaultAudioFormatComboBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(x => x.Content.ToString() == _settings.DefaultAudioFormat);
                DefaultAudioFormatComboBox.SelectedItem = audioFormatItem ?? DefaultAudioFormatComboBox.Items[0];

                var audioQualityItem = DefaultAudioQualityComboBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(x => x.Content.ToString() == _settings.DefaultAudioQuality);
                DefaultAudioQualityComboBox.SelectedItem = audioQualityItem ?? DefaultAudioQualityComboBox.Items[2];
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "InitializeSettings");
            }
        }

        private Settings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<Settings>(json);
                    if (settings != null)
                    {
                        // Initialize DefaultQualityComboBox with the saved quality
                        if (DefaultQualityComboBox != null)
                        {
                            var qualityItem = DefaultQualityComboBox.Items.Cast<string>()
                                .FirstOrDefault(x => x == settings.DefaultVideoQuality) ?? "1080p";  // Changed from DefaultQuality
                            DefaultQualityComboBox.SelectedItem = qualityItem;
                        }
                        return settings;
                    }
                }
                return new Settings();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "LoadSettings");
                return new Settings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.DefaultVideoQuality = DefaultQualityComboBox?.SelectedItem?.ToString() ?? "1080p";  // Changed from DefaultQuality
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

            _settings.RememberWindowPosition = RememberPositionCheckBox.IsChecked ?? false;
            _settings.AutoUpdateDependencies = AutoUpdateDepsCheckBox.IsChecked ?? false;
            _settings.DownloadThumbnails = DownloadThumbnailsCheckBox.IsChecked ?? false;
            _settings.DownloadSubtitles = DownloadSubtitlesCheckBox.IsChecked ?? false;
            _settings.DefaultVideoQuality = DefaultQualityComboBox.SelectedItem?.ToString() ?? "Best";  // Changed from DefaultQuality
            
            SaveSettings();
        }

        private async void DefaultVideoLocationBrowse_Click(object sender, RoutedEventArgs e)
        {
            var folder = await PickFolder();
            if (folder != null)
            {
                DefaultVideoLocationBox.Text = folder.Path;
                _settings.DefaultVideoDownloadPath = folder.Path;
                SaveSettings();
            }
        }

        private async void DefaultAudioLocationBrowse_Click(object sender, RoutedEventArgs e)
        {
            var folder = await PickFolder();
            if (folder != null)
            {
                DefaultAudioLocationBox.Text = folder.Path;
                _settings.DefaultAudioDownloadPath = folder.Path;
                SaveSettings();
            }
        }

        private async Task<Windows.Storage.StorageFolder?> PickFolder()
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            folderPicker.FileTypeFilter.Add("*");
            return await folderPicker.PickSingleFolderAsync();
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

        private void UrlTextBox_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

        private async void UrlTextBox_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                var text = await e.DataView.GetTextAsync();
                if (text != null && (text.Contains("youtube.com") || text.Contains("youtu.be")))
                {
                    UrlTextBox.Text = text;
                }
            }
        }

        private void ClearProgressInfo()
        {
            DownloadProgress.Value = 0;
            SpeedText.Text = string.Empty;
            TimeRemainingText.Text = string.Empty;
            StatusText.Text = string.Empty;
        }

        private void OutputFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OutputFormatComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                _isMP3Mode = selectedItem.Content.ToString() == "MP3";
                
                // Update UI visibility
                VideoControlsPanel.Visibility = _isMP3Mode ? Visibility.Collapsed : Visibility.Visible;
                AudioControlsPanel.Visibility = _isMP3Mode ? Visibility.Visible : Visibility.Collapsed;
                
                // Update button visibility
                UpdateModeBindings();
                
                // Clear any error messages
                UpdateStatus("");
            }
        }

        private void Setting_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (sender is ComboBox comboBox)
            {
                if (comboBox == DefaultAudioFormatComboBox)
                {
                    var newFormat = (DefaultAudioFormatComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "MP3";
                    _settings.DefaultAudioFormat = newFormat;
                    AudioFormatComboBox.SelectedItem = AudioFormatComboBox.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(x => x.Content.ToString() == newFormat);
                }
                else if (comboBox == DefaultAudioQualityComboBox)
                {
                    var newQuality = (DefaultAudioQualityComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "192 kbps";
                    _settings.DefaultAudioQuality = newQuality;
                    AudioQualityComboBox.SelectedItem = AudioQualityComboBox.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(x => x.Content.ToString() == newQuality);
                }
                else if (comboBox == DefaultQualityComboBox)
                {
                    var newQuality = DefaultQualityComboBox.SelectedItem?.ToString() ?? "1080p";
                    _settings.DefaultVideoQuality = newQuality;
                    QualityComboBox.SelectedItem = newQuality;
                }

                SaveSettings();
            }
        }

        // Keep this for backward compatibility
        private async void DefaultLocationBrowse_Click(object sender, RoutedEventArgs e)
        {
            var folder = await PickFolder();
            if (folder != null)
            {
                DefaultLocationBox.Text = folder.Path;
                _settings.DefaultDownloadPath = folder.Path;
                SaveSettings();
            }
        }

        private void FormatToggle_Click(object sender, RoutedEventArgs e)
        {
            _isMP3Mode = FormatToggle.IsChecked ?? false;
            
            // Update UI
            FormatIcon.Glyph = _isMP3Mode ? "\uE8D6" : "\uE714";  // Switch between audio/video icons
            FormatText.Text = _isMP3Mode ? "Audio Mode" : "Video Mode";
            
            // Update visibility of controls
            VideoControlsPanel.Visibility = _isMP3Mode ? Visibility.Collapsed : Visibility.Visible;
            AudioControlsPanel.Visibility = _isMP3Mode ? Visibility.Visible : Visibility.Collapsed;
            
            // Update button states
            UpdateModeBindings();
            
            // Clear any error messages
            UpdateStatus("");
            
            Logger.Log($"Switched to {(_isMP3Mode ? "Audio" : "Video")} mode");
        }

        private void InitializeUI()
        {
            try
            {
                _isMP3Mode = false;  // Start in video mode
                FormatToggle.IsChecked = false;
                FormatIcon.Glyph = "\uE714";
                FormatText.Text = "Video Mode";
                VideoControlsPanel.Visibility = Visibility.Visible;
                AudioControlsPanel.Visibility = Visibility.Collapsed;
                
                // Set initial button states
                UpdateModeBindings();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "InitializeUI");
            }
        }

        // Add property changed notification
        private void UpdateModeBindings()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // Update download buttons
                if (DownloadMP4Button is Button videoButton)
                {
                    videoButton.Visibility = !_isMP3Mode ? Visibility.Visible : Visibility.Collapsed;
                }
                
                if (DownloadMP3Button is Button audioButton)
                {
                    audioButton.Visibility = _isMP3Mode ? Visibility.Visible : Visibility.Collapsed;
                }
            });
        }
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