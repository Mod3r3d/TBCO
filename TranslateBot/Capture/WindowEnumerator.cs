using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace TranslateBot.Capture
{
    // Thông tin một cửa sổ trên desktop. Được dùng bởi WindowSelector UI để
    // hiển thị danh sách cho người dùng chọn, và bởi CaptureService để capture
    // vùng tương đối cửa sổ.
    public class WindowInfo
    {
        public IntPtr Handle { get; init; }
        public string Title { get; init; } = string.Empty;
        public string ProcessName { get; init; } = string.Empty;
        public uint ProcessId { get; init; }
        public string ClassName { get; init; } = string.Empty;
        public System.Windows.Rect Bounds { get; init; }       // Full window rect (screen coords)
        public System.Windows.Rect ClientBounds { get; init; }  // Client area (screen coords)

        public override string ToString() => $"{Title} [{ProcessName}]";
    }

    // Liệt kê các cửa sổ đang mở trên Windows để người dùng chọn game window.
    // Mỗi lần gọi GetVisibleWindows() đều quét lại từ đầu (không cache),
    // vì danh sách cửa sổ thay đổi liên tục.
    public static class WindowEnumerator
    {
        // Lấy danh sách tất cả cửa sổ visible, có title, kích thước hợp lý.
        public static List<WindowInfo> GetVisibleWindows()
        {
            var windows = new List<WindowInfo>();

            NativeMethods.EnumWindows((hWnd, _) =>
            {
                // Bỏ qua cửa sổ invisible
                if (!NativeMethods.IsWindowVisible(hWnd))
                    return true;

                // Bỏ qua cửa sổ không có title
                int titleLength = NativeMethods.GetWindowTextLength(hWnd);
                if (titleLength == 0)
                    return true;

                // Lấy title
                var titleBuilder = new StringBuilder(titleLength + 1);
                NativeMethods.GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
                string title = titleBuilder.ToString();

                // Bỏ qua cửa sổ kích thước quá nhỏ (< 100x50 pixel, thường là tooltip/popup hệ thống)
                NativeMethods.GetWindowRect(hWnd, out var windowRect);
                if (windowRect.Width < 100 || windowRect.Height < 50)
                    return true;

                // Lấy class name
                var classBuilder = new StringBuilder(256);
                NativeMethods.GetClassName(hWnd, classBuilder, classBuilder.Capacity);

                // Lấy process info
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
                string processName = string.Empty;
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
                catch
                {
                    // Process có thể đã kết thúc giữa chừng — bỏ qua
                }

                // Tính client area bounds (screen coordinates)
                var clientBounds = GetClientScreenBounds(hWnd);

                windows.Add(new WindowInfo
                {
                    Handle = hWnd,
                    Title = title,
                    ProcessName = processName,
                    ProcessId = processId,
                    ClassName = classBuilder.ToString(),
                    Bounds = windowRect.ToWindowsRect(),
                    ClientBounds = clientBounds
                });

                return true; // Tiếp tục duyệt
            }, IntPtr.Zero);

            return windows;
        }

        // Tìm lại cửa sổ bằng title + process name (dùng khi game restart, handle cũ invalid).
        // Trả về null nếu không tìm thấy.
        public static WindowInfo? FindByTitleAndProcess(string targetTitle, string targetProcess)
        {
            if (string.IsNullOrEmpty(targetTitle) && string.IsNullOrEmpty(targetProcess))
                return null;

            var windows = GetVisibleWindows();

            foreach (var w in windows)
            {
                bool titleMatch = string.IsNullOrEmpty(targetTitle)
                    || w.Title.Contains(targetTitle, StringComparison.OrdinalIgnoreCase);
                bool processMatch = string.IsNullOrEmpty(targetProcess)
                    || w.ProcessName.Equals(targetProcess, StringComparison.OrdinalIgnoreCase);

                if (titleMatch && processMatch)
                    return w;
            }

            return null;
        }

        // Chuyển client rect (0,0 origin) sang screen coordinates.
        private static System.Windows.Rect GetClientScreenBounds(IntPtr hWnd)
        {
            NativeMethods.GetClientRect(hWnd, out var clientRect);

            var topLeft = new NativeMethods.POINT(0, 0);
            NativeMethods.ClientToScreen(hWnd, ref topLeft);

            return new System.Windows.Rect(
                topLeft.X, topLeft.Y,
                clientRect.Width, clientRect.Height);
        }
    }
}
