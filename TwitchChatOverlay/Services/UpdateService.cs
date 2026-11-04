using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
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

        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
        private const int MaxRedirects = 5;
        private const long MaxDownloadBytes = 300L * 1024 * 1024;

        // API 呼び出し用（既定タイムアウト100秒で十分）
        private static readonly HttpClient _http = new();

        // ダウンロード用。リダイレクトは自前で検証するため自動追従を無効化し、タイムアウトも有限にする。
        private static readonly HttpClient _downloadHttp = new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
        })
        {
            Timeout = DownloadTimeout,
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
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
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

            // チェックサムが無いリリースでは自動ダウンロードしない（検証不能なバイナリを実行しない）
            if (!string.IsNullOrEmpty(downloadUrl) && string.IsNullOrEmpty(checksumUrl))
            {
                LogService.Warning(
                    $"リリース {tagName} に .sha256 が含まれていないため、自動更新を無効化しリリースページを案内します。");
                downloadUrl = null;
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
        // (2025 以降、実体の配信は release-assets.githubusercontent.com へ 302 される)
        private static readonly string[] TrustedHosts =
        {
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
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
            // 検証できないバイナリは実行しない（fail-closed）。
            // チェックサムが無いリリースは CheckForUpdateAsync 側でも自動更新の対象から外している。
            if (string.IsNullOrWhiteSpace(checksumUrl))
            {
                throw new InvalidOperationException(
                    "更新ファイルのチェックサム(.sha256)が公開されていないため、安全のため更新を中止しました。" +
                    "リリースページから手動で更新してください。");
            }

            ValidateDownloadUrl(url);
            ValidateDownloadUrl(checksumUrl);

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

            using var timeoutCts = new CancellationTokenSource(DownloadTimeout);
            try
            {
                using var response = await SendWithValidatedRedirectsAsync(url, timeoutCts.Token);
                _ = response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                if (totalBytes.HasValue && totalBytes.Value > MaxDownloadBytes)
                {
                    throw new InvalidOperationException(
                        $"更新ファイルのサイズが上限を超えています: {totalBytes.Value} bytes (上限 {MaxDownloadBytes} bytes)");
                }

                await using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token))
                {
                    var buffer = new byte[81920];
                    long downloaded = 0;
                    int read;

                    while ((read = await stream.ReadAsync(buffer, timeoutCts.Token)) > 0)
                    {
                        downloaded += read;
                        if (downloaded > MaxDownloadBytes)
                        {
                            throw new InvalidOperationException(
                                $"更新ファイルのサイズが上限（{MaxDownloadBytes} bytes）を超えたため中断しました。");
                        }

                        await fs.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token);

                        if (totalBytes.HasValue && totalBytes > 0)
                        {
                            progress?.Report((int)(downloaded * 100 / totalBytes.Value));
                        }
                    }

                    await fs.FlushAsync(timeoutCts.Token);
                }

                progress?.Report(100);

                // ダウンロード後に必ず SHA256 を検証する
                await this.VerifySha256Async(destPath, checksumUrl);

                LogService.Info($"ダウンロード完了およびSHA256検証成功: {destPath}");
                return destPath;
            }
            catch
            {
                // 検証失敗・中断時は不完全なファイルを残さない
                TryDeleteFile(destPath);
                throw;
            }
        }

        /// <summary>
        /// リダイレクトを自動追従せず、各ホップの URL を信頼ホスト判定してから取得する。
        /// </summary>
        private static async Task<HttpResponseMessage> SendWithValidatedRedirectsAsync(string url, CancellationToken cancellationToken)
        {
            var currentUrl = url;
            for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
            {
                ValidateDownloadUrl(currentUrl);

                var response = await _downloadHttp.GetAsync(
                    currentUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!IsRedirectStatusCode((int)response.StatusCode))
                {
                    return response;
                }

                var location = response.Headers.Location;
                response.Dispose();

                if (location == null)
                {
                    throw new InvalidOperationException("リダイレクト先の URL が取得できませんでした。");
                }

                currentUrl = location.IsAbsoluteUri
                    ? location.ToString()
                    : new Uri(new Uri(currentUrl), location).ToString();
            }

            throw new InvalidOperationException("リダイレクト回数が上限を超えたため、ダウンロードを中止しました。");
        }

        private static bool IsRedirectStatusCode(int statusCode)
        {
            return statusCode is 301 or 302 or 303 or 307 or 308;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 後始末の失敗は無視する
            }
        }

        /// <summary>
        /// 公開されている .sha256 ファイルをダウンロードし、ローカルファイルのハッシュと比較する。
        /// フォーマット: "&lt;hexhash&gt;  &lt;filename&gt;" (sha256sum 標準形式)
        /// </summary>
        private async Task VerifySha256Async(string localFilePath, string checksumUrl)
        {
            ValidateDownloadUrl(checksumUrl);

            // チェックサム取得もリダイレクト先ホストを検証する
            using var response = await SendWithValidatedRedirectsAsync(checksumUrl, CancellationToken.None);
            _ = response.EnsureSuccessStatusCode();
            var checksumText = await response.Content.ReadAsStringAsync();
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
            if (!IsSha256Hex(expectedHash))
            {
                TryDeleteFile(localFilePath);
                throw new InvalidOperationException(
                    "SHA256 チェックサムファイルの形式が不正です（64桁の16進数が必要です）。");
            }

            var actualHash = await ComputeSha256Async(localFilePath);

            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(localFilePath);
                throw new InvalidOperationException(
                    $"SHA256 検証に失敗しました。ファイルが破損しているか改ざんされている可能性があります。\n" +
                    $"期待値: {expectedHash}\n実際値: {actualHash}");
            }
        }

        private static bool IsSha256Hex(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            foreach (var c in value)
            {
                var isHex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
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
            // 実行対象はこのアプリがダウンロードしたディレクトリ配下のみに限定する
            var expectedDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TwitchChatOverlay"));
            var fullPath = Path.GetFullPath(filePath);
            if (!fullPath.StartsWith(expectedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("更新ファイルの配置場所が想定外のため、実行を中止しました。");
            }

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("更新ファイルが見つかりません。", fullPath);
            }

            LogService.Info($"インストーラー起動: {fullPath}");
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
            // GitHub API 由来の URL をそのまま起動しない（スキーム・ホストを検証する）
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                 !uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)))
            {
                LogService.Warning($"リリースページの URL が不正なため開きませんでした: {url}");
                return;
            }

            _ = Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
    }
}
