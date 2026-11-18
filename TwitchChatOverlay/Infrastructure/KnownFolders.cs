using System;
using System.Runtime.InteropServices;

namespace TwitchChatOverlay.Infrastructure
{
    /// <summary>
    /// OS の既知フォルダーを取得します。
    /// MSIX では %APPDATA% / %LOCALAPPDATA% 配下の書き込みが仮想化され、
    /// パッケージ外のプロセスからは見えなくなるため、仮想化対象外の場所を明示的に扱うために使用します。
    /// </summary>
    public static class KnownFolders
    {
        private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        /// <summary>ダウンロード フォルダーの実パスを返します。取得できない場合は null。</summary>
        public static string GetDownloadsPath()
        {
            var folderId = DownloadsFolderId;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                var result = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out buffer);
                if (result != 0 || buffer == IntPtr.Zero)
                {
                    return null;
                }

                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(buffer);
                }
            }
        }
    }
}
