using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchChatOverlay.Services
{
    /// <summary>ログの重要度レベル</summary>
    public enum LogLevel
    {
        Debug   = 0,
        Info    = 1,
        Warning = 2,
        Error   = 3,
    }

    /// <summary>
    /// スレッドセーフな非同期ファイルログサービス（静的シングルトン）。
    /// アプリ起動時に <see cref="Initialize"/> を呼び出してから使用してください。
    /// </summary>
    public static class LogService
    {
        // --- 設定 ---
        private static string _logDirectory;
        private static LogLevel _minLevel = LogLevel.Debug;

        // --- 内部状態 ---
        private static readonly ConcurrentQueue<string> _queue = new();
        private static readonly SemaphoreSlim _signal = new(0);
        private static CancellationTokenSource _cts;
        private static Task _writerTask;
        private static bool _initialized;
        private static readonly object _initLock = new();

        // ---------------------------------------------------------------
        // 公開プロパティ
        // ---------------------------------------------------------------

        /// <summary>出力する最低ログレベル（デフォルト: Debug）</summary>
        public static LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        // ---------------------------------------------------------------
        // 初期化 / シャットダウン
        // ---------------------------------------------------------------

        /// <summary>
        /// ログシステムを初期化します。アプリ起動時に一度だけ呼び出してください。
        /// </summary>
        /// <param name="logDirectory">
        /// ログ保存先ディレクトリ。null の場合は %APPDATA%\TwitchChatOverlay\logs を使用します。
        /// </param>
        public static void Initialize(string logDirectory = null)
        {
            lock (_initLock)
            {
                if (_initialized) return;

                if (logDirectory == null)
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    logDirectory = Path.Combine(appData, "TwitchChatOverlay", "logs");
                }

                _logDirectory = logDirectory;
                Directory.CreateDirectory(_logDirectory);

                _cts = new CancellationTokenSource();
                _writerTask = Task.Run(() => WriteLoopAsync(_cts.Token));

                _initialized = true;

                CleanupOldLogs(30);
                Info($"TwitchChatOverlay 起動。ログ出力先: {_logDirectory}");
            }
        }

        /// <summary>
        /// ログシステムをシャットダウンします。アプリ終了時に呼び出してください。
        /// キュー内の残ログをすべて書き出してから終了します。
        /// </summary>
        public static void Shutdown()
        {
            if (!_initialized) return;
            Info("TwitchChatOverlay 終了。");
            _cts?.Cancel();
            Flush();
            _writerTask?.Wait(TimeSpan.FromSeconds(5));
        }

        /// <summary>キュー内のログが書き出されるまで待機します（同期）。</summary>
        public static void Flush()
        {
            var timeout = DateTime.Now.AddSeconds(5);
            while (!_queue.IsEmpty && DateTime.Now < timeout)
                Thread.Sleep(50);
        }

        // ---------------------------------------------------------------
        // ログ書き込みメソッド
        // ---------------------------------------------------------------

        /// <summary>DEBUG レベルでログを記録します。</summary>
        public static void Debug(
            string message,
            Exception ex = null,
            [CallerFilePath]   string filePath  = "",
            [CallerLineNumber] int    lineNumber = 0,
            [CallerMemberName] string memberName = "")
            => Enqueue(LogLevel.Debug, message, ex, filePath, lineNumber, memberName);

        /// <summary>INFO レベルでログを記録します。</summary>
        public static void Info(
            string message,
            Exception ex = null,
            [CallerFilePath]   string filePath  = "",
            [CallerLineNumber] int    lineNumber = 0,
            [CallerMemberName] string memberName = "")
            => Enqueue(LogLevel.Info, message, ex, filePath, lineNumber, memberName);

        /// <summary>WARNING レベルでログを記録します。</summary>
        public static void Warning(
            string message,
            Exception ex = null,
            [CallerFilePath]   string filePath  = "",
            [CallerLineNumber] int    lineNumber = 0,
            [CallerMemberName] string memberName = "")
            => Enqueue(LogLevel.Warning, message, ex, filePath, lineNumber, memberName);

        /// <summary>ERROR レベルでログを記録します。</summary>
        public static void Error(
            string message,
            Exception ex = null,
            [CallerFilePath]   string filePath  = "",
            [CallerLineNumber] int    lineNumber = 0,
            [CallerMemberName] string memberName = "")
            => Enqueue(LogLevel.Error, message, ex, filePath, lineNumber, memberName);

        // ---------------------------------------------------------------
        // 内部実装
        // ---------------------------------------------------------------

        private static void Enqueue(
            LogLevel level, string message, Exception ex,
            string filePath, int lineNumber, string memberName)
        {
            if (level < _minLevel || !_initialized) return;

            string fileName = Path.GetFileName(filePath);
            string levelTag = level switch
            {
                LogLevel.Debug   => "DEBUG",
                LogLevel.Info    => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error   => "ERROR",
                _                => "     ",
            };

            var sb = new StringBuilder();
            sb.Append(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{levelTag}] [{fileName}:{lineNumber}] [{memberName}] {message}");

            if (ex != null)
            {
                sb.AppendLine();
                sb.Append($"  Exception({ex.GetType().Name}): {ex.Message}");

                if (ex.InnerException != null)
                {
                    sb.AppendLine();
                    sb.Append(
                        $"  InnerException({ex.InnerException.GetType().Name}): {ex.InnerException.Message}");
                }

                if (ex.StackTrace != null)
                {
                    sb.AppendLine();
                    sb.Append("  StackTrace:");
                    foreach (string line in ex.StackTrace.Split('\n'))
                    {
                        string trimmed = line.TrimEnd();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            sb.AppendLine();
                            sb.Append("    " + trimmed);
                        }
                    }
                }
            }

            _queue.Enqueue(sb.ToString());

            try { _signal.Release(); }
            catch { /* 無視 */ }
        }

        /// <summary>バックグラウンドでキューをファイルに書き出すループ。</summary>
        private static async Task WriteLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested || !_queue.IsEmpty)
            {
                // キューが空なら書き込み通知を待機（最大 500ms）
                if (_queue.IsEmpty)
                {
                    try
                    {
                        await _signal.WaitAsync(TimeSpan.FromMilliseconds(500), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        // シャットダウン要求 — 残りのキューを書き切るために続行
                    }
                }

                if (_queue.IsEmpty) continue;

                try
                {
                    string logFilePath = GetCurrentLogFilePath();
                    var batchBuilder = new StringBuilder();

                    while (_queue.TryDequeue(out string entry))
                        batchBuilder.AppendLine(entry);

                    if (batchBuilder.Length > 0)
                        await File.AppendAllTextAsync(logFilePath, batchBuilder.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // ログ書き込み失敗は無視（ログのために例外を発生させない）
                }
            }
        }

        /// <summary>本日分のログファイルパスを返します。日付が変わると新ファイルになります。</summary>
        private static string GetCurrentLogFilePath()
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            return Path.Combine(_logDirectory, $"TwitchChatOverlay_{date}.log");
        }

        /// <summary>指定日数より古いログファイルを削除します。</summary>
        private static void CleanupOldLogs(int keepDays)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-keepDays);
                foreach (string file in Directory.GetFiles(_logDirectory, "TwitchChatOverlay_*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch
            {
                // クリーンアップ失敗は無視
            }
        }
    }
}
