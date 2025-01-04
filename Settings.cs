public class Settings
{
    public string DefaultVideoDownloadPath { get; set; } = string.Empty;
    public string DefaultAudioDownloadPath { get; set; } = string.Empty;
    public string DefaultDownloadPath { get; set; } = string.Empty;
    public bool RememberWindowPosition { get; set; } = true;
    public bool AutoUpdateDependencies { get; set; } = true;
    public bool DownloadThumbnails { get; set; } = true;
    public bool DownloadSubtitles { get; set; } = false;
    public string DefaultVideoQuality { get; set; } = "1080p";
    public string DefaultVideoFormat { get; set; } = "MP4";
    public string DefaultAudioQuality { get; set; } = "192 kbps";
    public string DefaultAudioFormat { get; set; } = "MP3";
} 