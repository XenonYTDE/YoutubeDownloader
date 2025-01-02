using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace YoutubeDownloader
{
    public class UpdateManager
    {
        private readonly string _currentVersion;
        private readonly string _updateUrl = "https://api.github.com/repos/XenonYTDE/YoutubeDownloader/releases/latest";
        private readonly string _githubToken = "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";
        private readonly string _dependenciesPath;

        public UpdateManager(string currentVersion, string dependenciesPath)
        {
            _currentVersion = currentVersion;
            _dependenciesPath = dependenciesPath;
        }

        public async Task<(bool Available, string NewVersion, string DownloadUrl)?> CheckForUpdates()
        {
            try
            {
                using var client = new HttpClient();
                // Add User Agent and Authorization header
                client.DefaultRequestHeaders.Add("User-Agent", "YoutubeDownloader");
                client.DefaultRequestHeaders.Add("Authorization", $"token {_githubToken}");
                
                var response = await client.GetStringAsync(_updateUrl);
                var releaseInfo = JsonSerializer.Deserialize<GitHubRelease>(response);

                if (releaseInfo == null || string.IsNullOrEmpty(releaseInfo.TagName))
                    return null;

                // Remove 'v' prefix if present
                var latestVersion = releaseInfo.TagName.TrimStart('v');
                var currentVersionParsed = Version.Parse(_currentVersion);
                var latestVersionParsed = Version.Parse(latestVersion);

                if (latestVersionParsed > currentVersionParsed)
                {
                    var asset = releaseInfo.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe"));
                    if (asset != null)
                    {
                        return (true, latestVersion, asset.BrowserDownloadUrl);
                    }
                }

                return (false, null, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> DownloadAndInstallUpdate(string downloadUrl)
        {
            try
            {
                var updatePath = Path.Combine(_dependenciesPath, "update");
                Directory.CreateDirectory(updatePath);

                var installerPath = Path.Combine(updatePath, "YoutubeDownloader_new.exe");
                
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(downloadUrl);
                    response.EnsureSuccessStatusCode();
                    await using var fs = new FileStream(installerPath, FileMode.Create);
                    await response.Content.CopyToAsync(fs);
                }

                // Create update script
                var scriptPath = Path.Combine(updatePath, "update.bat");
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                
                var scriptContent = $@"
@echo off
timeout /t 2 /nobreak
copy /Y ""{installerPath}"" ""{currentExePath}""
start """" ""{currentExePath}""
del ""{installerPath}""
del ""%~f0""
";

                await File.WriteAllTextAsync(scriptPath, scriptContent);

                // Run update script
                Process.Start(new ProcessStartInfo
                {
                    FileName = scriptPath,
                    CreateNoWindow = true,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update installation failed: {ex.Message}");
                return false;
            }
        }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

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