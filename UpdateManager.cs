using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.IO;

namespace YoutubeDownloader
{
    public class UpdateManager
    {
        private readonly string _currentVersion;
        private readonly string _updateUrl = "https://api.github.com/repos/XenonYTDE/YoutubeDownloader/releases/latest";
        private readonly string _dependenciesPath;

        public UpdateManager(string currentVersion, string dependenciesPath)
        {
            _currentVersion = currentVersion;
            _dependenciesPath = dependenciesPath;
        }

        public async Task<(bool Available, string NewVersion, string DownloadUrl, string? PatchNotes)?> CheckForUpdates()
        {
            try
            {
                Logger.Log("Starting update check...");
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "YoutubeDownloader");
                
                Logger.Log($"Checking URL: {_updateUrl}");
                var response = await client.GetStringAsync(_updateUrl);
                Logger.Log("Got response from GitHub");
                
                var releaseInfo = JsonSerializer.Deserialize<GitHubRelease>(response);
                Logger.Log($"Parsed release info: {response}");

                if (releaseInfo == null || string.IsNullOrEmpty(releaseInfo.TagName))
                {
                    Logger.Log("No release info found");
                    return null;
                }

                var latestVersion = releaseInfo.TagName.TrimStart('v');
                Logger.Log($"Latest version: {latestVersion}, Current version: {_currentVersion}");
                
                var currentVersionParsed = Version.Parse(_currentVersion);
                var latestVersionParsed = Version.Parse(latestVersion);

                if (latestVersionParsed > currentVersionParsed)
                {
                    var asset = releaseInfo.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe"));
                    if (asset != null)
                    {
                        Logger.Log($"Update available. Download URL: {asset.BrowserDownloadUrl}");
                        return (true, latestVersion, asset.BrowserDownloadUrl, releaseInfo.Body);
                    }
                    Logger.Log("No exe asset found in release");
                }
                else
                {
                    Logger.Log("No update needed");
                }

                return (false, string.Empty, string.Empty, null);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "CheckForUpdates");
                return null;
            }
        }

        public async Task<bool> DownloadAndInstallUpdate(string downloadUrl)
        {
            try
            {
                Logger.Log("Starting update download and installation");
                var updatePath = Path.Combine(_dependenciesPath, "update");
                Directory.CreateDirectory(updatePath);

                var installerPath = Path.Combine(updatePath, "YoutubeDownloader_new.exe");
                Logger.Log($"Download URL: {downloadUrl}");
                Logger.Log($"Update path: {updatePath}");
                Logger.Log($"Installer path: {installerPath}");
                
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "YoutubeDownloader");
                    
                    Logger.Log("Starting download");
                    var response = await client.GetAsync(downloadUrl);
                    Logger.Log($"Download response status: {response.StatusCode}");
                    response.EnsureSuccessStatusCode();
                    
                    await using var fs = new FileStream(installerPath, FileMode.Create);
                    await response.Content.CopyToAsync(fs);
                }

                if (!File.Exists(installerPath))
                {
                    Logger.Log("ERROR: Downloaded file not found");
                    throw new Exception("Downloaded file not found");
                }

                var fileInfo = new FileInfo(installerPath);
                Logger.Log($"File downloaded successfully: {fileInfo.Length} bytes");

                var scriptPath = Path.Combine(updatePath, "update.bat");
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                
                if (currentExePath == null)
                {
                    Logger.Log("ERROR: Could not determine current executable path");
                    throw new Exception("Could not determine current executable path");
                }

                Logger.Log($"Current exe path: {currentExePath}");
                
                var scriptContent = $@"
@echo off
timeout /t 2 /nobreak
copy /Y ""{installerPath}"" ""{currentExePath}""
start """" ""{currentExePath}""
del ""{installerPath}""
del ""%~f0""
";

                await File.WriteAllTextAsync(scriptPath, scriptContent);
                Logger.Log($"Update script created at: {scriptPath}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = scriptPath,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                });

                Logger.Log("Update script started");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "DownloadAndInstallUpdate");
                return false;
            }
        }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
} 