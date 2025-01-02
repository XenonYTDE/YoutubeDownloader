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

        static Logger()
        {
            try
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Start new session
                File.AppendAllText(LogPath, $"\n\n=== New Session Started at {DateTime.Now} ===\n");
            }
            catch { }
        }

        public static void Log(string message)
        {
            try
            {
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