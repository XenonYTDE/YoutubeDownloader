using System;
using System.IO;

namespace YoutubeDownloader
{
    public static class Logger
    {
        private static readonly string LogPath;

        static Logger()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YoutubeDownloader"
            );
            LogPath = Path.Combine(appDataPath, "debug.log");
        }

        public static void Log(string message)
        {
            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, logEntry);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public static void LogError(Exception ex, string context = "")
        {
            var message = $"ERROR in {context}\n{ex.Message}\nStack Trace:\n{ex.StackTrace}";
            Log(message);
        }
    }
} 