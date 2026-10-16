using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;

namespace WinAirPlay.Gui
{
    /// <summary>
    /// Hosts the ffplay window (launched by the engine) inside the WPF visual tree.
    /// ffplay is a separate process; we reparent its top-level window into this HwndHost.
    /// All P/Invoke is guarded so a failure simply leaves ffplay as a standalone window.
    /// </summary>
    public class FfplayHost : HwndHost
    {
        private readonly int _pid;
        private IntPtr _ffHwnd = IntPtr.Zero;

        public FfplayHost(int pid) => _pid = pid;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _ffHwnd = FindFfplayWindow(_pid, 3000);
            if (_ffHwnd == IntPtr.Zero)
                return new HandleRef(null, IntPtr.Zero);

            try
            {
                // Strip the title bar / borders for a seamless embed.
                int style = GetWindowLong(_ffHwnd, GWL_STYLE);
                style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
                SetWindowLong(_ffHwnd, GWL_STYLE, style);
            }
            catch { }

            SetParent(_ffHwnd, hwndParent.Handle);
            return new HandleRef(this, _ffHwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            try { SetParent(hwnd.Handle, IntPtr.Zero); } catch { }
        }

        /// <summary>Wait (up to timeoutMs) for ffplay's main window to appear for the given PID.</summary>
        public static IntPtr FindFfplayWindow(int pid, int timeoutMs)
        {
            int deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                foreach (var p in Process.GetProcessesByName("ffplay"))
                {
                    try
                    {
                        if (p.Id == pid && p.MainWindowHandle != IntPtr.Zero)
                            return p.MainWindowHandle;
                    }
                    catch { }
                }
                Thread.Sleep(100);
            }
            return IntPtr.Zero;
        }

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;

        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
