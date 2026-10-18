using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinAirPlay.Gui
{
    /// <summary>
    /// Hosts the ffplay window (launched by the engine) inside the WPF visual tree.
    /// ffplay is a separate process; we reparent its top-level window into this HwndHost
    /// and, crucially, resize it to fill this host on every arrange pass. Without that
    /// resize the embedded window collapses to 0x0 and the mirror shows nothing.
    /// All P/Invoke is guarded so a failure simply leaves ffplay as a standalone window.
    /// </summary>
    public class FfplayHost : HwndHost
    {
        private readonly int _pid;
        private IntPtr _ffHwnd = IntPtr.Zero;

        public FfplayHost(int pid) => _pid = pid;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _ffHwnd = FindFfplayWindow(_pid, 4000);
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

            // Reparent into the host. Sizing happens in ArrangeOverride (ActualWidth
            // is still 0 here, so we must not MoveWindow to a zero rect yet).
            SetParent(_ffHwnd, hwndParent.Handle);
            return new HandleRef(this, _ffHwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            try { SetParent(hwnd.Handle, IntPtr.Zero); } catch { }
        }

        protected override Size MeasureOverride(Size constraint)
        {
            // Claim all the space the parent grid cell gives us.
            return constraint;
        }

        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            if (arrangeBounds.Width >= 1 && arrangeBounds.Height >= 1 && _ffHwnd != IntPtr.Zero)
                ResizeToHost(arrangeBounds);
            return arrangeBounds;
        }

        private void ResizeToHost(Size bounds)
        {
            if (_ffHwnd == IntPtr.Zero) return;
            if (bounds.Width < 1 || bounds.Height < 1) return;
            try
            {
                // arrangeBounds are device-independent pixels; MoveWindow wants physical pixels.
                var dpi = VisualTreeHelper.GetDpi(this);
                int pw = (int)(bounds.Width * dpi.X);
                int ph = (int)(bounds.Height * dpi.Y);
                MoveWindow(_ffHwnd, 0, 0, pw, ph, true);
            }
            catch { }
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

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    }
}
