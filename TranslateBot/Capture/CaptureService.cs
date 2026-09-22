using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TranslateBot.Infrastructure;

namespace TranslateBot.Capture
{
    public class CaptureService : IDisposable
    {
        private readonly CaptureBufferPool _bufferPool = new();
        private bool _disposed;

        public CaptureBufferPool BufferPool => _bufferPool;

        // ═══════════════════════════════════════════════════════════════════
        // Chế độ 1: Screen-absolute capture (Tối ưu Buffer Reuse - Stage 4)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Chụp vùng màn hình tái sử dụng buffer theo slotKey.
        /// Mặc định trả về mảng byte đệm tái sử dụng (zero LOH allocations).
        /// </summary>
        public byte[] CaptureRegion(int x, int y, int width, int height, string slotKey = "default")
        {
            if (_disposed || width <= 0 || height <= 0) 
                return Array.Empty<byte>();

            return _bufferPool.Capture(slotKey, x, y, width, height);
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
        // Chế độ 2: Window-relative capture (Stage 3A & Stage 4)
        // ═══════════════════════════════════════════════════════════════════

        // Trả về null nếu cửa sổ không hợp lệ hoặc bị minimize.
        public byte[]? CaptureWindowRegion(IntPtr windowHandle, int relX, int relY, int width, int height, string slotKey = "default")
        {
            if (_disposed || width <= 0 || height <= 0)
                return null;

            // Kiểm tra handle còn hợp lệ không (game có thể đã đóng/restart)
            if (!NativeMethods.IsWindow(windowHandle))
            {
                AppLogger.Info("[CAPTURE_WARN] Window handle không còn hợp lệ");
                return null;
            }

            // Cửa sổ bị minimize → CopyFromScreen sẽ trả về frame đen, vô nghĩa
            if (NativeMethods.IsIconic(windowHandle))
            {
                return null;
            }

            // Tính tọa độ tuyệt đối từ client-area origin
            var clientOrigin = new NativeMethods.POINT(0, 0);
            if (!NativeMethods.ClientToScreen(windowHandle, ref clientOrigin))
            {
                AppLogger.Error("[CAPTURE_WARN] Không thể lấy vị trí client area");
                return null;
            }

            int absX = clientOrigin.X + relX;
            int absY = clientOrigin.Y + relY;

            // Dùng lại logic capture screen-absolute tối ưu buffer reuse
            return CaptureRegion(absX, absY, width, height, slotKey);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bufferPool.Dispose();
        }
    }
}