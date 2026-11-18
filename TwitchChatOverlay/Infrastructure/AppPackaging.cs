using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TwitchChatOverlay.Infrastructure
{
    /// <summary>
    /// アプリが MSIX パッケージとして実行されているかどうかを判定します。
    /// パッケージ版ではインストール先が保護されるため、従来の自己更新（ファイル差し替え）は行えません。
    /// </summary>
    public static class AppPackaging
    {
        private const int ErrorSuccess = 0;
        private const int ErrorInsufficientBuffer = 122;
        private const int ErrorNoPackage = 15700; // APPMODEL_ERROR_NO_PACKAGE

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder packageFullName);

        private static readonly Lazy<bool> _isPackaged = new(DetectPackaged);

        /// <summary>MSIX パッケージとして動作している場合は true。</summary>
        public static bool IsPackaged => _isPackaged.Value;

        private static bool DetectPackaged()
        {
            try
            {
                var length = 0;
                // 長さ 0 で呼び出すと必要なバッファ長が返る（未パッケージ時は APPMODEL_ERROR_NO_PACKAGE）。
                var result = GetCurrentPackageFullName(ref length, null);
                return result switch
                {
                    ErrorSuccess => true,
                    ErrorInsufficientBuffer => true,
                    ErrorNoPackage => false,
                    _ => false,
                };
            }
            catch (DllNotFoundException)
            {
                // Windows API が存在しない環境では未パッケージとして扱う
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }
}
