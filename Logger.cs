using System;
using System.IO;
using System.Diagnostics;

namespace YoutubeDownloader
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YoutubeDownloader",
            "debug.log"
        );

        private static readonly int MaxLogAgeDays = 7; // Keep logs for 7 days

        static Logger()
        {
            try
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Always clean the log file on startup
                if (File.Exists(LogPath))
                {
                    File.Delete(LogPath);
                }

                // Start new session
                File.AppendAllText(LogPath, $"=== New Session Started at {DateTime.Now} ===\n");
                File.AppendAllText(LogPath, $"YouTube Downloader Version: {GetAppVersion()}\n");
                File.AppendAllText(LogPath, $"OS Version: {Environment.OSVersion}\n");
                File.AppendAllText(LogPath, $"64-bit OS: {Environment.Is64BitOperatingSystem}\n");
                File.AppendAllText(LogPath, $".NET Runtime: {Environment.Version}\n");
                File.AppendAllText(LogPath, "=====================================\n\n");
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

        public static void Log(string message, bool isProgress = false)
        {
            try
            {
                // Skip progress messages if they're too frequent
                if (isProgress && message.StartsWith("Progress update received:"))
                {
                    return;
                }

                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INFO: {message}";
                File.AppendAllText(LogPath, logMessage + "\n");
                Debug.WriteLine(logMessage);
            }
            catch { }
        }

        public static void LogError(Exception ex, string context)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR in {context}:\n" +
                                $"Message: {ex.Message}\n" +
                                $"Stack Trace: {ex.StackTrace}\n" +
                                $"Source: {ex.Source}\n";
                
                if (ex.InnerException != null)
                {
                    logMessage += $"Inner Exception: {ex.InnerException.Message}\n" +
                                 $"Inner Stack Trace: {ex.InnerException.StackTrace}\n";
                }

                File.AppendAllText(LogPath, logMessage + "\n");
                Debug.WriteLine(logMessage);
            }
            catch { }
        }
    }
} 