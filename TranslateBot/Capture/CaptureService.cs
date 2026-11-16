using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TranslateBot.Infrastructure;

namespace TranslateBot.Capture
{
    public enum CaptureStatus
    {
        Success,
        Empty,
        Minimized,
        InvalidWindow,
        OutOfBounds,
        AccessDenied,
        DeviceUnavailable
    }

    public class CaptureResult
    {
        public byte[]? Buffer { get; }
        public CaptureStatus Status { get; }
        public string? ErrorMessage { get; }
        public int Width { get; }
        public int Height { get; }

        public bool IsSuccess => Status == CaptureStatus.Success && Buffer != null && Buffer.Length > 0;

        public CaptureResult(CaptureStatus status, byte[]? buffer = null, int width = 0, int height = 0, string? errorMessage = null)
        {
            Status = status;
            Buffer = buffer;
            Width = width;
            Height = height;
            ErrorMessage = errorMessage;
        }

        public static CaptureResult Success(byte[] buffer, int width, int height)
            => new(CaptureStatus.Success, buffer, width, height);

        public static CaptureResult Failure(CaptureStatus status, string? message = null)
            => new(status, null, 0, 0, message);
    }

    public class CaptureService : IDisposable
    {
        private readonly CaptureBufferPool _bufferPool = new();
        private bool _disposed;

        public CaptureBufferPool BufferPool => _bufferPool;

        // ═══════════════════════════════════════════════════════════════════
        // Chế độ 1: Screen-absolute capture (Tối ưu Buffer Reuse - Stage 4)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Chụp vùng màn hình có trả về trạng thái chi tiết (Phase 3 & §4.6 Plan).
        /// Hỗ trợ đầy đủ toạ độ âm của hệ thống đa màn hình (Multi-monitor virtual screen).
        /// </summary>
        public CaptureResult CaptureRegionWithResult(int x, int y, int width, int height, string slotKey = "default")
        {
            if (_disposed)
                return CaptureResult.Failure(CaptureStatus.DeviceUnavailable, "CaptureService đã bị giải phóng.");

            if (width <= 0 || height <= 0)
                return CaptureResult.Failure(CaptureStatus.Empty, "Kích thước chụp không hợp lệ (width/height <= 0).");

            var vScreen = NativeMethods.GetVirtualScreenBounds();

            // Kiểm tra xem vùng chụp có giao với Virtual Screen không
            var requestedRect = new Rectangle(x, y, width, height);
            if (!requestedRect.IntersectsWith(vScreen))
            {
                return CaptureResult.Failure(CaptureStatus.OutOfBounds, 
                    $"Vùng chụp ({x},{y},{width}x{height}) nằm ngoài toàn bộ màn hình ảo ({vScreen.X},{vScreen.Y},{vScreen.Width}x{vScreen.Height}).");
            }

            try
            {
                byte[] raw = _bufferPool.Capture(slotKey, x, y, width, height);
                if (raw == null || raw.Length == 0)
                {
                    return CaptureResult.Failure(CaptureStatus.DeviceUnavailable, "Không thể đọc dữ liệu điểm ảnh từ Desktop DC.");
                }

                return CaptureResult.Success(raw, width, height);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[CAPTURE_ERROR] Lỗi chụp vùng màn hình: {ex.Message}");
                return CaptureResult.Failure(CaptureStatus.AccessDenied, ex.Message);
            }
        }

        /// <summary>
        /// Chụp vùng màn hình tái sử dụng buffer theo slotKey (zero LOH allocations).
        /// </summary>
        public byte[] CaptureRegion(int x, int y, int width, int height, string slotKey = "default")
        {
            var res = CaptureRegionWithResult(x, y, width, height, slotKey);
            return res.IsSuccess && res.Buffer != null ? res.Buffer : Array.Empty<byte>();
        }

        /// <summary>
        /// Chụp một bản sao độc lập (dành cho snapshot hoặc lưu trữ không bị ghi đè frame sau).
        /// </summary>
        public byte[] CaptureRegionCloned(int x, int y, int width, int height)
        {
            var buffer = CaptureRegion(x, y, width, height, "cloned_temp");
            if (buffer.Length == 0) return Array.Empty<byte>();

            var clone = new byte[buffer.Length];
            Buffer.BlockCopy(buffer, 0, clone, 0, buffer.Length);
            return clone;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Chế độ 2: Window-relative capture (Stage 3A, Stage 4 & Phase 3)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Chụp vùng tương đối theo cửa sổ với mã trạng thái phân loại chi tiết.
        /// </summary>
        public CaptureResult CaptureWindowRegionWithResult(IntPtr windowHandle, int relX, int relY, int width, int height, string slotKey = "default")
        {
            if (_disposed)
                return CaptureResult.Failure(CaptureStatus.DeviceUnavailable, "CaptureService đã bị giải phóng.");

            if (width <= 0 || height <= 0)
                return CaptureResult.Failure(CaptureStatus.Empty, "Kích thước chụp không hợp lệ.");

            // 1. Kiểm tra handle còn hợp lệ không (game có thể đã đóng hoặc crash/restart)
            if (windowHandle == IntPtr.Zero || !NativeMethods.IsWindow(windowHandle))
            {
                AppLogger.Info("[CAPTURE_WARN] Window handle không còn tồn tại hoặc đã đóng.");
                return CaptureResult.Failure(CaptureStatus.InvalidWindow, "Window handle không còn tồn tại.");
            }

            // 2. Cửa sổ bị minimize
            if (NativeMethods.IsIconic(windowHandle))
            {
                return CaptureResult.Failure(CaptureStatus.Minimized, "Cửa sổ game đang bị thu nhỏ (Minimized).");
            }

            // 3. Lấy kích thước Client Area để kiểm tra bounds
            if (!NativeMethods.GetClientRect(windowHandle, out var clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
            {
                return CaptureResult.Failure(CaptureStatus.Empty, "Client Area của cửa sổ rỗng.");
            }

            // 4. Kiểm tra toạ độ tương đối
            var relRect = new Rectangle(relX, relY, width, height);
            var clientBounds = new Rectangle(0, 0, clientRect.Width, clientRect.Height);
            if (!relRect.IntersectsWith(clientBounds))
            {
                return CaptureResult.Failure(CaptureStatus.OutOfBounds, 
                    $"Vùng chụp tương đối ({relX},{relY},{width}x{height}) nằm ngoài Client Area ({clientBounds.Width}x{clientBounds.Height}).");
            }

            // 5. Tính toạ độ tuyệt đối từ client-area origin
            var clientOrigin = new NativeMethods.POINT(0, 0);
            if (!NativeMethods.ClientToScreen(windowHandle, ref clientOrigin))
            {
                AppLogger.Error("[CAPTURE_WARN] Không thể lấy vị trí ClientToScreen của cửa sổ.");
                return CaptureResult.Failure(CaptureStatus.AccessDenied, "Không thể tính toạ độ ClientToScreen.");
            }

            int absX = clientOrigin.X + relX;
            int absY = clientOrigin.Y + relY;

            return CaptureRegionWithResult(absX, absY, width, height, slotKey);
        }

        /// <summary>
        /// Trả về null nếu cửa sổ không hợp lệ hoặc bị minimize (tương thích ngược với interface cũ).
        /// </summary>
        public byte[]? CaptureWindowRegion(IntPtr windowHandle, int relX, int relY, int width, int height, string slotKey = "default")
        {
            var res = CaptureWindowRegionWithResult(windowHandle, relX, relY, width, height, slotKey);
            return res.IsSuccess ? res.Buffer : null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bufferPool.Dispose();
        }
    }
}
