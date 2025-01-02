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
                var releases = await GetAllReleases();
                
                if (!releases.Any())
                {
                    Logger.Log("No releases found");
                    return null;
                }

                var currentVersionParsed = Version.Parse(_currentVersion);
                var missedReleases = new List<GitHubRelease>();

                foreach (var release in releases)
                {
                    if (string.IsNullOrEmpty(release.TagName)) continue;
                    
                    var releaseVersion = Version.Parse(release.TagName.TrimStart('v'));
                    if (releaseVersion > currentVersionParsed)
                    {
                        missedReleases.Add(release);
                    }
                    else
                    {
                        break; // Stop once we reach current version
                    }
                }

                if (missedReleases.Any())
                {
                    var latestRelease = missedReleases.First(); // Most recent release
                    var asset = latestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe"));
                    
                    if (asset != null)
                    {
                        // Build cumulative patch notes
                        var patchNotes = new System.Text.StringBuilder();
                        foreach (var release in missedReleases)
                        {
                            patchNotes.AppendLine($"Version {release.TagName}:");
                            patchNotes.AppendLine(release.Body ?? "No patch notes available.");
                            patchNotes.AppendLine(); // Add blank line between versions
                        }

                        Logger.Log($"Updates available. Found {missedReleases.Count} missed version(s)");
                        return (true, latestRelease.TagName.TrimStart('v'), asset.BrowserDownloadUrl, patchNotes.ToString());
                    }
                }

                Logger.Log("No updates needed");
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

                var scriptPath = Path.Combine(updatePath, "update.vbs");
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                
                if (currentExePath == null)
                {
                    Logger.Log("ERROR: Could not determine current executable path");
                    throw new Exception("Could not determine current executable path");
                }

                Logger.Log($"Current exe path: {currentExePath}");
                
                var scriptContent = $@"
Set objShell = CreateObject(""WScript.Shell"")
WScript.Sleep 2000 ' Wait 2 seconds
objShell.Run ""cmd /c copy /y ""{installerPath}"" ""{currentExePath}"""", 0, True
objShell.Run ""cmd /c del ""{installerPath}"""", 0, True
objShell.Run ""cmd /c start """" ""{currentExePath}"""", 0, False
WScript.Sleep 1000 ' Wait 1 second
Set objFSO = CreateObject(""Scripting.FileSystemObject"")
objFSO.DeleteFile WScript.ScriptFullName
";

                await File.WriteAllTextAsync(scriptPath, scriptContent);
                Logger.Log($"Update script created at: {scriptPath}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = "wscript.exe",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
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

        private async Task<List<GitHubRelease>> GetAllReleases()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "YoutubeDownloader");
                
                // Use releases API instead of latest
                var response = await client.GetStringAsync(_updateUrl.Replace("/latest", ""));
                return JsonSerializer.Deserialize<List<GitHubRelease>>(response) ?? new List<GitHubRelease>();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "GetAllReleases");
                return new List<GitHubRelease>();
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