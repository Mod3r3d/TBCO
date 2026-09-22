using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TranslateBot.Capture
{
    // Tập trung toàn bộ P/Invoke declarations vào một file duy nhất.
    // Mọi class cần gọi Win32 API (WindowEnumerator, CaptureService...) đều import từ đây,
    // tránh khai báo rải rác khắp nơi và dễ kiểm soát khi cần sửa signature.
    internal static class NativeMethods
    {
        // ─── Window Enumeration ────────────────────────────────────────────

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        // ─── Window Geometry ───────────────────────────────────────────────

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        // ─── Window State ──────────────────────────────────────────────────

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);  // Minimized?

        // ─── Process Info ──────────────────────────────────────────────────

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // ─── Global Hotkeys (Section 39) ───────────────────────────────────
        // Dùng cho F8 Snapshot, F9 Lock Overlay, F10 Start/Pause... Hotkey hoạt động ngay cả khi
        // cửa sổ bot không có focus (người chơi đang focus vào game).

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Hotkey IDs (tự đặt, dùng làm tham số cho Register/Unregister)
        public const int HOTKEY_SNAPSHOT = 1;      // F8
        public const int HOTKEY_START_PAUSE = 2;    // F10 (dự phòng)
        public const int HOTKEY_LOCK_OVERLAY = 3;   // F9 (Lock/Unlock Click-Through Overlay)

        // Virtual key codes
        public const uint VK_F8 = 0x77;
        public const uint VK_F9 = 0x78;
        public const uint VK_F10 = 0x79;

        // WM_HOTKEY message
        public const int WM_HOTKEY = 0x0312;

        // ─── Window Styles & Click-Through (Section 20) ────────────────────
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        /// <summary>
        /// Bật hoặc tắt chế độ Click-Through (chuột xuyên qua cửa sổ mà không nhận click)
        /// </summary>
        public static void SetClickThrough(IntPtr hWnd, bool clickThrough)
        {
            if (hWnd == IntPtr.Zero) return;
            var currentExStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();

            if (clickThrough)
            {
                // Thêm WS_EX_TRANSPARENT và WS_EX_LAYERED để click xuyên thấu
                var newExStyle = currentExStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED;
                SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(newExStyle));
            }
            else
            {
                // Gỡ bỏ WS_EX_TRANSPARENT để nhận lại tương tác chuột (kéo thả, bấm nút)
                var newExStyle = currentExStyle & ~WS_EX_TRANSPARENT;
                SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(newExStyle));
            }
        }

        // ─── Structs ───────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;

            public System.Windows.Rect ToWindowsRect()
                => new(Left, Top, Width, Height);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y) { X = x; Y = y; }
        }
    }
}
