using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TwitchChatOverlay.Services
{
    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; init; }
        public string LatestVersion { get; init; }
        public string DownloadUrl { get; init; }
        public string ChecksumUrl { get; init; }
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
            LogService.Info("アップデートチェック開始");
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

            // アセットから .exe または .zip、および対応する .sha256 を探す
            string downloadUrl = null;
            string checksumUrl = null;
            var assets = json["assets"] as JArray;
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    string name = asset["name"]?.Value<string>() ?? "";
                    string assetUrl = asset["browser_download_url"]?.Value<string>();

                    if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        checksumUrl = assetUrl;
                    }
                    else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                             name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = assetUrl;
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            break; // .exe があれば優先
                    }
                }
            }

            LogService.Info($"アップデート利用可能: {tagName}");
            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                LatestVersion = tagName,
                DownloadUrl = downloadUrl,
                ChecksumUrl = checksumUrl,
                ReleasePageUrl = htmlUrl,
            };
        }

        // GitHub がリリースアセットに使用するホスト
        private static readonly string[] TrustedHosts =
        {
            "github.com",
            "objects.githubusercontent.com",
        };

        private static void ValidateDownloadUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("ダウンロード URL が不正です。");
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("HTTPS 以外のダウンロード URL は許可されていません。");
            bool trusted = false;
            foreach (var host in TrustedHosts)
            {
                if (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))
                {
                    trusted = true;
                    break;
                }
            }
            if (!trusted)
                throw new InvalidOperationException($"信頼されていないホストからのダウンロードは許可されていません: {uri.Host}");
        }

        public async Task<string> DownloadUpdateAsync(string url, string checksumUrl, IProgress<int> progress)
        {
            ValidateDownloadUrl(url);

            string tempDir = Path.Combine(Path.GetTempPath(), "TwitchChatOverlay");
            Directory.CreateDirectory(tempDir);

            string fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            // URLデコード（%20 など）
            fileName = Uri.UnescapeDataString(fileName);
            // パストラバーサル防止: ファイル名のみを使用
            fileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new InvalidOperationException("ダウンロード先ファイル名を決定できません。");
            string destPath = Path.Combine(tempDir, fileName);

            LogService.Info($"アップデートファイルダウンロード開始: {fileName}");

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

            // チェックサムファイルがある場合は SHA256 を検証する
            if (!string.IsNullOrEmpty(checksumUrl))
                await VerifySha256Async(destPath, checksumUrl);

            LogService.Info($"ダウンロード完了およびSHA256検証成功: {destPath}");
            return destPath;
        }

        /// <summary>
        /// 公開されている .sha256 ファイルをダウンロードし、ローカルファイルのハッシュと比較する。
        /// フォーマット: "&lt;hexhash&gt;  &lt;filename&gt;" (sha256sum 標準形式)
        /// </summary>
        private async Task VerifySha256Async(string localFilePath, string checksumUrl)
        {
            ValidateDownloadUrl(checksumUrl);

            string checksumText = await _http.GetStringAsync(checksumUrl);
            // 最初のトークン（空白区切り）がハッシュ値
            string expectedHash = checksumText.Split(new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();

            string actualHash = await ComputeSha256Async(localFilePath);

            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(localFilePath);
                throw new InvalidOperationException(
                    $"SHA256 検証に失敗しました。ファイルが破損しているか改ざんされている可能性があります。\n" +
                    $"期待値: {expectedHash}\n実際値: {actualHash}");
            }
        }

        private static async Task<string> ComputeSha256Async(string filePath)
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 81920, useAsync: true);
            byte[] hashBytes = await SHA256.HashDataAsync(fs);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        public void LaunchInstaller(string filePath)
        {
            LogService.Info($"インストーラー起動: {filePath}");
            string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
            string currentDir = Path.GetDirectoryName(currentExe)!;

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

                int pid = Process.GetCurrentProcess().Id;
                string tempDir = Path.GetDirectoryName(filePath)!;

                // cmd.exe のバッチファイルで自己更新（PowerShell Bypass を回避）
                string scriptContent =
                    $"@echo off\r\n" +
                    $":wait\r\n" +
                    $"tasklist /FI \"PID eq {pid}\" 2>NUL | find \" {pid} \" >NUL\r\n" +
                    $"if not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto wait)\r\n" +
                    $"xcopy /Y /E /Q \"{extractDir}\\*\" \"{currentDir}\\\"\r\n" +
                    $"start \"\" \"{currentExe}\"\r\n" +
                    $"ping -n 4 127.0.0.1 >NUL\r\n" +
                    $"rd /S /Q \"{tempDir}\"\r\n";

                string scriptPath = Path.Combine(
                    Path.GetTempPath(), "TwitchChatOverlay_update.bat");
                File.WriteAllText(scriptPath, scriptContent, System.Text.Encoding.Default);

                Process.Start(new ProcessStartInfo("cmd.exe", $"/C \"{scriptPath}\"")
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized,
                });

                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => System.Windows.Application.Current.Shutdown());
            }
        }

        public void OpenReleasePage(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
