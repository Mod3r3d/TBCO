using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TranslateBot.Capture
{
    /// <summary>
    /// Quản lý tái sử dụng GDI Bitmap và mảng byte thô theo slot key.
    /// Giúp loại bỏ hoàn toàn việc cấp phát Bitmap và mảng byte hàng trăm KB vào Large Object Heap (LOH) mỗi frame.
    /// </summary>
    public class CaptureBufferPool : IDisposable
    {
        private class BufferSlot : IDisposable
        {
            public int Width { get; private set; }
            public int Height { get; private set; }
            public Bitmap? Bitmap { get; private set; }
            public byte[]? RawBuffer { get; private set; }

            public void EnsureSize(int width, int height)
            {
                if (Bitmap != null && Width == width && Height == height && RawBuffer != null)
                {
                    return;
                }

                DisposeGdi();

                Width = width;
                Height = height;
                Bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                int bufferSize = width * height * 4;
                if (RawBuffer == null || RawBuffer.Length != bufferSize)
                {
                    RawBuffer = new byte[bufferSize];
                }
            }

            public void Dispose()
            {
                DisposeGdi();
                RawBuffer = null;
            }

            private void DisposeGdi()
            {
                Bitmap?.Dispose();
                Bitmap = null;
            }
        }

        private readonly ConcurrentDictionary<string, BufferSlot> _slots = new();
        private bool _disposed;

        /// <summary>
        /// Chụp vùng màn hình vào buffer tái sử dụng của slot tương ứng.
        /// Trả về mảng byte BGRA32 tái sử dụng.
        /// </summary>
        public byte[] Capture(string slotKey, int x, int y, int width, int height)
        {
            if (_disposed || width <= 0 || height <= 0)
            {
                return Array.Empty<byte>();
            }

            var slot = _slots.GetOrAdd(slotKey, _ => new BufferSlot());
            lock (slot)
            {
                slot.EnsureSize(width, height);

                if (slot.Bitmap == null || slot.RawBuffer == null)
                {
                    return Array.Empty<byte>();
                }

                // Chụp màn hình vào Bitmap tái sử dụng.
                // Lưu ý: Graphics.FromImage phải được Dispose trước khi gọi LockBits
                // để tránh xung đột GDI+ surface handle.
                bool copySuccess = false;
                try
                {
                    using (var g = Graphics.FromImage(slot.Bitmap))
                    {
                        g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                    }
                    copySuccess = true;
                }
                catch (Exception)
                {
                    // Trường hợp Desktop DC không khả dụng (ví dụ: máy tính bị khóa màn hình Win+L,
                    // UAC prompt bật lên, hoặc chạy trong môi trường test/CI không có interactive desktop session).
                    // Không throw exception làm sập bot mà giữ an toàn cho app.
                }

                if (copySuccess)
                {
                    // Khóa bộ nhớ bitmap và copy trực tiếp vào buffer tái sử dụng
                    var rect = new Rectangle(0, 0, width, height);
                    var bmpData = slot.Bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        int bytesToCopy = Math.Min(Math.Abs(bmpData.Stride) * height, slot.RawBuffer.Length);
                        Marshal.Copy(bmpData.Scan0, slot.RawBuffer, 0, bytesToCopy);
                    }
                    finally
                    {
                        slot.Bitmap.UnlockBits(bmpData);
                    }
                }

                return slot.RawBuffer;
            }
        }

        /// <summary>
        /// Xóa bỏ slot cụ thể khi vùng capture bị xóa hoặc thay đổi.
        /// </summary>
        public void RemoveSlot(string slotKey)
        {
            if (_slots.TryRemove(slotKey, out var slot))
            {
                lock (slot)
                {
                    slot.Dispose();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kvp in _slots)
            {
                lock (kvp.Value)
                {
                    kvp.Value.Dispose();
                }
            }
            _slots.Clear();
        }
    }
}
