using System;
using System.IO;
using System.Threading;

namespace YoutubeDownloader.Updater
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Invalid arguments");
                return;
            }

            var appPath = args[0];
            var updatePath = args[1];
            var targetPath = args[2];

            try
            {
                // Wait for main app to exit
                Thread.Sleep(2000);

                // Copy update files
                foreach (var file in Directory.GetFiles(updatePath, "*.*", SearchOption.AllDirectories))
                {
                    var relativePath = file.Substring(updatePath.Length + 1);
                    var targetFile = Path.Combine(targetPath, relativePath);
                    
                    var targetDir = Path.GetDirectoryName(targetFile);
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir!);
                    }

                    File.Copy(file, targetFile, true);
                }

                // Cleanup
                Directory.Delete(updatePath, true);

                // Start updated app
                System.Diagnostics.Process.Start(appPath);
            }
            catch (Exception ex)
            {
                File.WriteAllText(
                    Path.Combine(Path.GetDirectoryName(appPath)!, "update_error.log"),
                    $"Update failed at {DateTime.Now}:\n{ex}"
                );
            }
        }
    }
} 