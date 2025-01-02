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
        private readonly string _updateUrl = "https://api.github.com/repos/XenonYTDE/YoutubeDownloader/releases";
        private readonly string _dependenciesPath;
        private readonly HttpClient _httpClient;
        private const string UpdateScriptName = "update.ps1";  // Change to PowerShell script

        public UpdateManager(string currentVersion, string dependenciesPath)
        {
            _currentVersion = currentVersion;
            _dependenciesPath = dependenciesPath;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "YoutubeDownloader");
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
                var updatePath = Path.Combine(_dependenciesPath, "update");
                Directory.CreateDirectory(updatePath);

                // Download the new version
                var zipPath = Path.Combine(updatePath, "update.zip");
                var response = await _httpClient.GetAsync(downloadUrl);
                using (var fs = new FileStream(zipPath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fs);
                }

                // Create PowerShell update script
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (currentExePath == null) return false;

                var currentDir = Path.GetDirectoryName(currentExePath);
                var errorLogPath = Path.Combine(_dependenciesPath, "update_error.log");

                // Use @ for verbatim string and escape { with {{
                var scriptContent = @"
$ErrorActionPreference = 'Stop'
try {
    # Hide the window completely
    $WindowCode = '[DllImport(""user32.dll"")] public static extern bool ShowWindow(int handle, int state);'
    add-type -name win -member $WindowCode -namespace native
    [native.win]::ShowWindow(([System.Diagnostics.Process]::GetCurrentProcess() | Get-Process).MainWindowHandle, 0)
    
    Start-Sleep -Seconds 1
    Expand-Archive -Path '" + zipPath + @"' -DestinationPath '" + updatePath + @"' -Force
    
    # Copy all files from update folder to application folder
    Get-ChildItem -Path '" + updatePath + @"' -Recurse | ForEach-Object {
        $destPath = $_.FullName.Replace('" + updatePath + "', '" + currentDir + @"')
        if ($_.PSIsContainer) {
            New-Item -ItemType Directory -Path $destPath -Force -ErrorAction SilentlyContinue
        } else {
            Copy-Item -Path $_.FullName -Destination $destPath -Force
        }
    }

    # Cleanup
    Remove-Item -Path '" + updatePath + @"' -Recurse -Force
    
    # Start the updated application
    Start-Process -FilePath '" + currentExePath + @"'
} catch {
    [System.IO.File]::AppendAllText('" + errorLogPath + @"', $_.Exception.Message)
    exit 1
}";

                var scriptPath = Path.Combine(_dependenciesPath, UpdateScriptName);
                await File.WriteAllTextAsync(scriptPath, scriptContent);

                // Execute the update script
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update failed: {ex}");
                return false;
            }
        }

        private async Task<List<GitHubRelease>> GetAllReleases()
        {
            try
            {
                // Use releases API
                var response = await _httpClient.GetStringAsync(_updateUrl);
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