using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace GooseDeluxe
{
    /// <summary>Is a game, a video or a presentation filling the goose's (primary) screen right now?</summary>
    internal static class FullscreenDetector
    {
        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        private const int QUNS_PRESENTATION_MODE = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

        private static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;

        public static bool IsFullscreenOnPrimary()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || !IsWindowVisible(fg) || IsIconic(fg)) return false;

            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == ownPid) return false; // the goose's own windows and dialogs

            StringBuilder cls = new StringBuilder(64);
            GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;

            Screen screen = Screen.FromHandle(fg);
            if (!screen.Primary) return false; // the goose only lives on the primary screen

            int state;
            if (SHQueryUserNotificationState(out state) == 0 &&
                (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE))
                return true;

            // Borderless games, F11 browsers and video players: a caption-less, non-maximized window covering
            // the monitor. Maximized windows (even frameless ones like Discord) don't count, so an auto-hidden
            // taskbar doesn't make the goose hide all day; browsers un-maximize before going fullscreen.
            if (IsZoomed(fg)) return false;
            RECT r;
            if (!GetWindowRect(fg, out r)) return false;
            Rectangle b = screen.Bounds;
            const int slack = 2;
            bool covers = r.Left <= b.Left + slack && r.Top <= b.Top + slack && r.Right >= b.Right - slack && r.Bottom >= b.Bottom - slack;
            bool hasCaption = (GetWindowLong(fg, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
            return covers && !hasCaption;
        }
    }
}
