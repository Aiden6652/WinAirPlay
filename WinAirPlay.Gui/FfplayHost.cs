using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

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

            // SDL may have created the window hidden while initialising; force it visible
            // and move it off the "hidden" state once it belongs to our host.
            try { ShowWindow(_ffHwnd, SW_SHOW); } catch { }

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
                double scaleX = 1.0, scaleY = 1.0;
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    var m = source.CompositionTarget.TransformToDevice;
                    scaleX = m.M11;
                    scaleY = m.M22;
                }
                int pw = (int)(bounds.Width * scaleX);
                int ph = (int)(bounds.Height * scaleY);
                MoveWindow(_ffHwnd, 0, 0, pw, ph, true);
            }
            catch { }
        }

        /// <summary>
        /// Wait (up to timeoutMs) for ffplay's video window.
        /// Primary strategy: FindWindow by the fixed SDL window class ("SDL_app"). Unlike
        /// EnumWindows this does NOT require the window to be visible yet, which is exactly
        /// the case while ffplay is still initialising its renderer.
        /// Fallback: enumerate top-level windows owned by the ffplay PID.
        /// </summary>
        public static IntPtr FindFfplayWindow(int pid, int timeoutMs)
        {
            int deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                // 1) SDL video window by class name (most reliable).
                IntPtr sdl = FindWindow("SDL_app", null);
                if (sdl != IntPtr.Zero && (pid <= 0 || OwnedByPid(sdl, pid)))
                    return sdl;

                // 2) Fallback: any top-level window belonging to the ffplay process.
                IntPtr found = IntPtr.Zero;
                EnumWindows((hWnd, lParam) =>
                {
                    if (!OwnedByPid(hWnd, pid)) return true;
                    if (!IsWindowVisible(hWnd)) return true;
                    if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;
                    if (GetWindowTextLength(hWnd) == 0 && GetWindowLong(hWnd, GWL_STYLE) == 0) return true;
                    found = hWnd;
                    return false;
                }, IntPtr.Zero);

                if (found != IntPtr.Zero) return found;
                Thread.Sleep(80);
            }
            return IntPtr.Zero;
        }

        private static bool OwnedByPid(IntPtr hWnd, int pid)
        {
            GetWindowThreadProcessId(hWnd, out uint wpid);
            return wpid == (uint)pid;
        }

        private const int GWL_STYLE = -16;
        private const uint GW_OWNER = 4;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_SHOW = 5;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

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
