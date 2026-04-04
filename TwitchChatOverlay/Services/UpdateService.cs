using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;

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

        // API 呼び出し用（既定タイムアウト100秒で十分）
        private static readonly HttpClient _http = new();

        // ダウンロード用（大きなファイルのタイムアウトを防ぐため無制限）
        private static readonly HttpClient _downloadHttp = new()
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        static UpdateService()
        {
            var userAgent = new ProductInfoHeaderValue("TwitchChatOverlay", GetCurrentVersion());
            _http.DefaultRequestHeaders.UserAgent.Add(userAgent);
            _downloadHttp.DefaultRequestHeaders.UserAgent.Add(userAgent);
        }

        private static string GetCurrentVersion()
        {
#if DEBUG
            var v = new Version(0, 0, 1);
#else
            var v = Assembly.GetExecutingAssembly().GetName().Version;
#endif
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }

        public async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            LogService.Info("アップデートチェック開始");
            var response = await _http.GetStringAsync(ApiUrl);
            var json = JObject.Parse(response);

            var tagName = json["tag_name"]?.Value<string>() ?? "";
            var htmlUrl = json["html_url"]?.Value<string>() ?? "";

            // tag_name は "v0.2.0" や "0.2.0" の形式を想定
            var normalizedTag = tagName.TrimStart('v');

            if (!Version.TryParse(normalizedTag, out var latestVersion))
            {
                return new UpdateCheckResult { IsUpdateAvailable = false };
            }

            var currentRaw = GetCurrentVersion();
            if (!Version.TryParse(currentRaw, out var currentVersion))
            {
                return new UpdateCheckResult { IsUpdateAvailable = false };
            }

            if (latestVersion <= currentVersion)
            {
                return new UpdateCheckResult { IsUpdateAvailable = false };
            }

            // アセットから .exe または .zip を探し、そのファイル名に対応する .sha256 をペアリングして取得する
            string downloadUrl = null;
            string checksumUrl = null;
            if (json["assets"] is JArray assets)
            {
                string downloadName = null;

                // まずダウンロード対象のアセット (.exe 優先, なければ .zip) を決定する
                foreach (var asset in assets)
                {
                    var name = asset["name"]?.Value<string>() ?? "";
                    var assetUrl = asset["browser_download_url"]?.Value<string>();

                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadName = name;
                        downloadUrl = assetUrl;
                        break; // .exe があれば優先
                    }

                    if (downloadUrl == null &&
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadName = name;
                        downloadUrl = assetUrl;
                    }
                }

                // ダウンロード対象が決まっていれば、そのファイル名に対応する .sha256 を探す
                if (downloadName != null)
                {
                    var checksumTargetName = downloadName + ".sha256";

                    foreach (var asset in assets)
                    {
                        var name = asset["name"]?.Value<string>() ?? "";
                        if (name.Equals(checksumTargetName, StringComparison.OrdinalIgnoreCase))
                        {
                            checksumUrl = asset["browser_download_url"]?.Value<string>();
                            break;
                        }
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
            {
                throw new InvalidOperationException("ダウンロード URL が不正です。");
            }

            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("HTTPS 以外のダウンロード URL は許可されていません。");
            }

            var trusted = false;
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
            {
                throw new InvalidOperationException($"信頼されていないホストからのダウンロードは許可されていません: {uri.Host}");
            }
        }

        public async Task<string> DownloadUpdateAsync(string url, string checksumUrl, IProgress<int> progress)
        {
            ValidateDownloadUrl(url);

            var tempDir = Path.Combine(Path.GetTempPath(), "TwitchChatOverlay");
            _ = Directory.CreateDirectory(tempDir);

            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            // URLデコード（%20 など）
            fileName = Uri.UnescapeDataString(fileName);
            // パストラバーサル防止: ファイル名のみを使用
            fileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException("ダウンロード先ファイル名を決定できません。");
            }

            var destPath = Path.Combine(tempDir, fileName);

            using var response = await _downloadHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            _ = response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;

            await using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var stream = await response.Content.ReadAsStreamAsync())
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;

                    if (totalBytes.HasValue && totalBytes > 0)
                    {
                        progress?.Report((int)(downloaded * 100 / totalBytes.Value));
                    }
                }

                await fs.FlushAsync();
            }

            progress?.Report(100);

            // チェックサムファイルがある場合は SHA256 を検証する
            if (!string.IsNullOrEmpty(checksumUrl))
            {
                await this.VerifySha256Async(destPath, checksumUrl);
            }

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

            var checksumText = await _http.GetStringAsync(checksumUrl);
            // 最初のトークン（空白区切り）がハッシュ値
            var tokens = checksumText.Split(new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || string.IsNullOrWhiteSpace(tokens[0]))
            {
                File.Delete(localFilePath);
                throw new InvalidOperationException(
                    "SHA256 チェックサムファイル(.sha256)の形式が不正です。ファイルが空か、ハッシュ値が含まれていません。");
            }
            var expectedHash = tokens[0].ToLowerInvariant();

            var actualHash = await ComputeSha256Async(localFilePath);

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
            var hashBytes = await SHA256.HashDataAsync(fs);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        public void LaunchInstaller(string filePath)
        {
            LogService.Info($"インストーラー起動: {filePath}");
            var currentExe = Process.GetCurrentProcess().MainModule!.FileName;
            var currentDir = Path.GetDirectoryName(currentExe)!;

            if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                _ = Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                System.Windows.Application.Current.Dispatcher.Invoke(
                    System.Windows.Application.Current.Shutdown);
            }
            else if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var extractDir = Path.Combine(
                    Path.GetDirectoryName(filePath)!,
                    Path.GetFileNameWithoutExtension(filePath));
                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, true);
                }

                ZipFile.ExtractToDirectory(filePath, extractDir);

                var pid = Process.GetCurrentProcess().Id;
                var tempDir = Path.GetDirectoryName(filePath)!;

                // cmd.exe のバッチファイルで自己更新（PowerShell Bypass を回避）
                // chcp 65001 で UTF-8 コードページに切り替え、Unicode パスに対応
                var scriptContent =
                    $"@echo off\r\n" +
                    $"chcp 65001 >NUL\r\n" +
                    $":wait\r\n" +
                    $"tasklist /FI \"PID eq {pid}\" 2>NUL | find \" {pid} \" >NUL\r\n" +
                    $"if not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto wait)\r\n" +
                    $"xcopy /Y /E /Q \"{extractDir}\\*\" \"{currentDir}\\\"\r\n" +
                    $"start \"\" \"{currentExe}\"\r\n" +
                    $"ping -n 4 127.0.0.1 >NUL\r\n" +
                    $"rd /S /Q \"{tempDir}\"\r\n";

                var scriptPath = Path.Combine(
                    Path.GetTempPath(),
                    $"TwitchChatOverlay_update_{pid}_{Guid.NewGuid():N}.bat");
                using var fsBat = new FileStream(scriptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(fsBat, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(scriptContent);

                _ = Process.Start(new ProcessStartInfo("cmd.exe", $"/C \"{scriptPath}\"")
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized,
                });

                System.Windows.Application.Current.Dispatcher.Invoke(
                    System.Windows.Application.Current.Shutdown);
            }
        }

        public void OpenReleasePage(string url)
        {
            _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
