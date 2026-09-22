using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace TranslateBot.Capture
{
    public record FrameChangeResult(
        bool HasChanged,
        double ChangedRatio,
        int ChangedPixels,
        int TotalSampledPixels,
        double ElapsedMs);

    /// <summary>
    /// Phát hiện thay đổi khung hình tối ưu hóa theo Section 11 của TBCO Plan:
    /// - Downsampling ROI (SampleScale mặc định 4x -> giảm 16x số phép tính)
    /// - Chuyển đổi Grayscale bằng phép toán số nguyên (Integer Luminance)
    /// - Theo dõi độc lập theo từng OcrArea (Multi-area support)
    /// - Tái sử dụng buffer so sánh (Zero GC allocation ở hot-path)
    /// </summary>
    public class FrameChangeDetector
    {
        private class AreaSampleBuffer
        {
            public int Width { get; private set; }
            public int Height { get; private set; }
            public int SampleScale { get; private set; }
            public int SampledW { get; private set; }
            public int SampledH { get; private set; }
            public int TotalSampled { get; private set; }

            public byte[]? LastSampled;
            public byte[]? CurrentSampled;
            public bool HasPrevious;

            public void EnsureSize(int width, int height, int sampleScale)
            {
                int sampledW = (width + sampleScale - 1) / sampleScale;
                int sampledH = (height + sampleScale - 1) / sampleScale;
                int total = sampledW * sampledH;

                if (Width == width && Height == height && SampleScale == sampleScale &&
                    LastSampled != null && LastSampled.Length == total &&
                    CurrentSampled != null && CurrentSampled.Length == total)
                {
                    return;
                }

                Width = width;
                Height = height;
                SampleScale = sampleScale;
                SampledW = sampledW;
                SampledH = sampledH;
                TotalSampled = total;

                LastSampled = new byte[total];
                CurrentSampled = new byte[total];
                HasPrevious = false;
            }

            public void Reset()
            {
                HasPrevious = false;
            }
        }

        private readonly ConcurrentDictionary<string, AreaSampleBuffer> _areaBuffers = new();

        // ── Configurable Parameters (Section 11) ───────────────────────────
        public int PixelDiffThreshold { get; set; } = 18;
        public double ChangedRatioThreshold { get; set; } = 0.005; // 0.5% số pixel mẫu (đủ nhạy cho text thoại FGO, tránh lỗi đứng hình ở 0.8% diff)
        public int MinimumChangedArea { get; set; } = 6;            // Ít nhất 6 điểm mẫu đổi để tránh bụi/glitter
        public int SampleScale { get; set; } = 4;                  // Bước nhảy 4x4 (Downsample 16x)

        // ═══════════════════════════════════════════════════════════════════
        // Public API
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Kiểm tra xem vùng diện tích theo areaId có thay đổi hay không.
        /// </summary>
        public bool HasAreaChanged(string areaId, byte[] currentFrame, int width, int height)
        {
            return CheckAreaChange(areaId, currentFrame, width, height).HasChanged;
        }

        /// <summary>
        /// Kiểm tra chi tiết thay đổi và trả về metric (tỷ lệ, số pixel, thời gian tính toán).
        /// </summary>
        public FrameChangeResult CheckAreaChange(string areaId, byte[] currentFrame, int width, int height)
        {
            if (currentFrame == null || currentFrame.Length == 0 || width <= 0 || height <= 0)
            {
                return new FrameChangeResult(false, 0, 0, 0, 0);
            }

            var sw = Stopwatch.StartNew();

            int scale = Math.Max(1, SampleScale);
            var buffer = _areaBuffers.GetOrAdd(areaId, _ => new AreaSampleBuffer());

            lock (buffer)
            {
                buffer.EnsureSize(width, height, scale);

                var currentSampled = buffer.CurrentSampled!;
                var lastSampled = buffer.LastSampled!;
                int sampledW = buffer.SampledW;
                int sampledH = buffer.SampledH;
                int totalSampled = buffer.TotalSampled;

                // 1. Trích xuất Downsampled Grayscale
                int sampleIdx = 0;
                for (int sy = 0; sy < sampledH; sy++)
                {
                    int y = sy * scale;
                    if (y >= height) y = height - 1;
                    int rowOffset = y * width * 4;

                    for (int sx = 0; sx < sampledW; sx++)
                    {
                        int x = sx * scale;
                        if (x >= width) x = width - 1;
                        int pixelOffset = rowOffset + (x * 4);

                        if (pixelOffset + 2 < currentFrame.Length)
                        {
                            // BGRA: B=0, G=1, R=2. Chuyển sang Grayscale luminance bằng số nguyên
                            byte b = currentFrame[pixelOffset];
                            byte g = currentFrame[pixelOffset + 1];
                            byte r = currentFrame[pixelOffset + 2];
                            currentSampled[sampleIdx] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
                        }
                        else
                        {
                            currentSampled[sampleIdx] = 0;
                        }

                        sampleIdx++;
                    }
                }

                // Nếu chưa có frame trước đó -> Đánh dấu đổi và lưu frame hiện tại
                if (!buffer.HasPrevious)
                {
                    Array.Copy(currentSampled, lastSampled, totalSampled);
                    buffer.HasPrevious = true;
                    sw.Stop();
                    return new FrameChangeResult(true, 1.0, totalSampled, totalSampled, sw.Elapsed.TotalMilliseconds);
                }

                // 2. So sánh từng điểm mẫu
                int changedPixels = 0;
                int diffThreshold = PixelDiffThreshold;

                for (int i = 0; i < totalSampled; i++)
                {
                    if (Math.Abs(currentSampled[i] - lastSampled[i]) > diffThreshold)
                    {
                        changedPixels++;
                    }
                }

                double changedRatio = totalSampled > 0 ? (double)changedPixels / totalSampled : 0;
                // Linh hoạt nhận diện: hoặc vượt tỷ lệ changedRatioThreshold (0.5%),
                // hoặc đủ số lượng điểm ảnh thay đổi (>= 10) với tỷ lệ >= 0.25% (bắt chữ thoại mỏng)
                bool hasChanged = (changedRatio >= ChangedRatioThreshold && changedPixels >= MinimumChangedArea)
                               || (changedPixels >= MinimumChangedArea * 2 && changedRatio >= 0.0025);

                if (hasChanged)
                {
                    // Hoán đổi con trỏ buffer (Zero allocation swap)
                    buffer.LastSampled = currentSampled;
                    buffer.CurrentSampled = lastSampled;
                }

                sw.Stop();
                return new FrameChangeResult(hasChanged, changedRatio, changedPixels, totalSampled, sw.Elapsed.TotalMilliseconds);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Backward-compatible Overloads (Single-region fallback)
        // ═══════════════════════════════════════════════════════════════════

        public bool HasChanged(byte[] currentFrame, int width, int height)
        {
            return HasAreaChanged("default", currentFrame, width, height);
        }

        public bool HasChanged(byte[] currentFrame)
        {
            if (currentFrame == null || currentFrame.Length < 4) return false;
            // Dự đoán kích thước nếu không truyền width/height
            // Sử dụng "default" buffer
            if (_areaBuffers.TryGetValue("default", out var buf) && buf.Width > 0 && buf.Height > 0 && (buf.Width * buf.Height * 4 == currentFrame.Length))
            {
                return HasAreaChanged("default", currentFrame, buf.Width, buf.Height);
            }

            // Fallback kích thước ước lượng giả định ARGB
            int pixelCount = currentFrame.Length / 4;
            int width = Math.Max(1, (int)Math.Sqrt(pixelCount * 16.0 / 9.0));
            int height = Math.Max(1, pixelCount / width);
            return HasAreaChanged("default", currentFrame, width, height);
        }

        /// <summary>
        /// Xóa sạch lịch sử so sánh để frame tiếp theo luôn được nhận diện là có thay đổi.
        /// Thường gọi khi người dùng chuyển cửa sổ, kéo lại vùng quét hoặc bấm Reset.
        /// </summary>
        public void Invalidate(string? areaId = null)
        {
            if (areaId == null)
            {
                foreach (var kvp in _areaBuffers)
                {
                    lock (kvp.Value)
                    {
                        kvp.Value.Reset();
                    }
                }
            }
            else if (_areaBuffers.TryGetValue(areaId, out var buf))
            {
                lock (buf)
                {
                    buf.Reset();
                }
            }
        }
    }
}
