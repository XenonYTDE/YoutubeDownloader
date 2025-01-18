namespace YoutubeDownloader
{
    public class DownloadHistoryItem
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public bool IsMP4 { get; set; }
        public DateTime DownloadDate { get; set; }
    }
}

