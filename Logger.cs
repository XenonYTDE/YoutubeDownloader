using System;
using System.IO;
using System.Diagnostics;
using System.Text;

namespace YoutubeDownloader
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YoutubeDownloader",
            "debug.log"
        );

        static Logger()
        {
            try
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Start new session with separator
                var sessionHeader = new StringBuilder();
                sessionHeader.AppendLine("\n==================================================");
                sessionHeader.AppendLine($"=== New Session Started at {DateTime.Now} ===");
                sessionHeader.AppendLine($"YouTube Downloader Version: {GetAppVersion()}");
                sessionHeader.AppendLine($"OS Version: {Environment.OSVersion}");
                sessionHeader.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
                sessionHeader.AppendLine($".NET Runtime: {Environment.Version}");
                sessionHeader.AppendLine($"Machine Name: {Environment.MachineName}");
                sessionHeader.AppendLine($"Processor Count: {Environment.ProcessorCount}");
                sessionHeader.AppendLine($"System Memory: {GetSystemMemory()}");
                sessionHeader.AppendLine("==================================================\n");

                File.AppendAllText(LogPath, sessionHeader.ToString());
            }
            catch { }
        }

        private static string GetAppVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string GetSystemMemory()
        {
            try
            {
                var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                return $"{totalMemory / (1024 * 1024 * 1024.0):F1} GB";
            }
            catch
            {
                return "Unknown";
            }
        }

        public static void Log(string message, bool isDebug = false)
        {
            try
            {
                var threadId = Environment.CurrentManagedThreadId;
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}][Thread {threadId}] {message}";
                
                File.AppendAllText(LogPath, logMessage + Environment.NewLine);
                if (!isDebug)
                {
                    Debug.WriteLine(logMessage);
                }
            }
            catch { }
        }

        public static void LogUI(string component, string action, string details)
        {
            Log($"UI: {component} - {action} - {details}");
        }

        public static void LogError(Exception ex, string context)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ERROR in {context}:");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"Stack: {ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                sb.AppendLine("Inner Exception:");
                sb.AppendLine($"Message: {ex.InnerException.Message}");
                sb.AppendLine($"Stack: {ex.InnerException.StackTrace}");
            }

            sb.AppendLine($"Source: {ex.Source}");
            sb.AppendLine($"Target Site: {ex.TargetSite}");
            
            Log(sb.ToString());
        }

        public static void LogDownload(string url, string format, string quality, bool isAudio)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Download Started:");
            sb.AppendLine($"URL: {url}");
            sb.AppendLine($"Type: {(isAudio ? "Audio" : "Video")}");
            sb.AppendLine($"Format: {format}");
            sb.AppendLine($"Quality: {quality}");
            
            Log(sb.ToString());
        }

        public static void LogSettings(string setting, string oldValue, string newValue)
        {
            Log($"Setting Changed - {setting}: {oldValue} -> {newValue}");
        }

        public static void LogMemory()
        {
            var memory = GC.GetTotalMemory(false) / (1024 * 1024.0);
            Log($"Memory Usage: {memory:F2} MB", true);
        }
    }
} 