public class Settings
{
    public string DefaultDownloadPath { get; set; } = string.Empty;
    public string DefaultQuality { get; set; } = "Best";
    public bool RememberWindowPosition { get; set; } = true;
    public bool AutoUpdateDependencies { get; set; } = true;
    public int MaxConcurrentDownloads { get; set; } = 1;
    public bool DownloadThumbnails { get; set; } = false;
    public bool DownloadSubtitles { get; set; } = false;
    public string FileNameTemplate { get; set; } = "%(title)s.%(ext)s";
} 