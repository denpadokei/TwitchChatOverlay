using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TwitchChatOverlay.Services
{
    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; init; }
        public string LatestVersion { get; init; }
        public string DownloadUrl { get; init; }
        public string ReleasePageUrl { get; init; }
    }

    public class UpdateService
    {
        private const string ApiUrl =
            "https://api.github.com/repos/denpadokei/TwitchChatOverlay/releases/latest";

        private static readonly HttpClient _http = new();

        static UpdateService()
        {
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("TwitchChatOverlay", GetCurrentVersion()));
        }

        private static string GetCurrentVersion()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }

        public async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            var response = await _http.GetStringAsync(ApiUrl);
            var json = JObject.Parse(response);

            string tagName = json["tag_name"]?.Value<string>() ?? "";
            string htmlUrl = json["html_url"]?.Value<string>() ?? "";

            // tag_name は "v0.2.0" や "0.2.0" の形式を想定
            string normalizedTag = tagName.TrimStart('v');

            if (!Version.TryParse(normalizedTag, out var latestVersion))
                return new UpdateCheckResult { IsUpdateAvailable = false };

            string currentRaw = GetCurrentVersion();
            if (!Version.TryParse(currentRaw, out var currentVersion))
                return new UpdateCheckResult { IsUpdateAvailable = false };

            if (latestVersion <= currentVersion)
                return new UpdateCheckResult { IsUpdateAvailable = false };

            // アセットから .exe または .zip を探す
            string downloadUrl = null;
            var assets = json["assets"] as JArray;
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    string name = asset["name"]?.Value<string>() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset["browser_download_url"]?.Value<string>();
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            break; // .exe があれば優先
                    }
                }
            }

            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                LatestVersion = tagName,
                DownloadUrl = downloadUrl,
                ReleasePageUrl = htmlUrl,
            };
        }

        public async Task<string> DownloadUpdateAsync(string url, IProgress<int> progress)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TwitchChatOverlay");
            Directory.CreateDirectory(tempDir);

            string fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            // URLデコード（%20 など）
            fileName = Uri.UnescapeDataString(fileName);
            string destPath = Path.Combine(tempDir, fileName);

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var stream = await response.Content.ReadAsStreamAsync();

            byte[] buffer = new byte[81920];
            long downloaded = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;

                if (totalBytes.HasValue && totalBytes > 0)
                    progress?.Report((int)(downloaded * 100 / totalBytes.Value));
            }

            progress?.Report(100);
            return destPath;
        }

        public void LaunchInstaller(string filePath)
        {
            if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => System.Windows.Application.Current.Shutdown());
            }
            else if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string extractDir = Path.Combine(
                    Path.GetDirectoryName(filePath)!,
                    Path.GetFileNameWithoutExtension(filePath));
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(filePath, extractDir);
                Process.Start(new ProcessStartInfo("explorer.exe", extractDir)
                    { UseShellExecute = true });
            }
        }

        public void OpenReleasePage(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
