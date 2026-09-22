using System;
using System.Collections.Generic;
using System.Linq;
using TranslateBot.Infrastructure;

namespace TranslateBot.OCR
{
    // Quản lý vùng loại trừ (Section 17) và áp dụng chúng lên frame ảnh trước OCR.
    // Cách hoạt động: tô đen (zero-out) pixel trong vùng exclusion → OCR engine
    // không đọc được chữ ở vùng đó → AUTO/MENU/SKIP không lọt vào DialogueTracker.
    public class ExclusionManager
    {
        private readonly List<ExclusionArea> _exclusions = new();

        public IReadOnlyList<ExclusionArea> Exclusions => _exclusions.AsReadOnly();

        public void Add(ExclusionArea area)
        {
            _exclusions.Add(area);
            AppLogger.Info($"[EXCLUSION_ADDED] \"{area.Name}\" {area.RegionWidth}×{area.RegionHeight}");
        }

        public bool Remove(string id)
        {
            var ex = _exclusions.FirstOrDefault(a => a.Id == id);
            if (ex != null)
            {
                _exclusions.Remove(ex);
                AppLogger.Info($"[EXCLUSION_REMOVED] \"{ex.Name}\"");
                return true;
            }
            return false;
        }

        public void SetEnabled(string id, bool enabled)
        {
            var ex = _exclusions.FirstOrDefault(a => a.Id == id);
            if (ex != null) ex.Enabled = enabled;
        }

        public void Clear() => _exclusions.Clear();

        // Tô đen pixel trong các vùng exclusion đang bật.
        // frame: mảng byte BGRA, captureWidth/Height: kích thước frame đã capture.
        // Các tọa độ exclusion là TƯƠNG ĐỐI so với frame (0,0 = góc trên trái frame).
        public void ApplyExclusions(byte[] frame, int captureWidth, int captureHeight)
        {
            if (frame.Length == 0) return;

            int stride = captureWidth * 4; // BGRA = 4 bytes/pixel

            foreach (var ex in _exclusions.Where(e => e.Enabled))
            {
                // Clamp vùng exclusion vào trong bounds của frame
                int startX = Math.Max(0, ex.RegionX);
                int startY = Math.Max(0, ex.RegionY);
                int endX = Math.Min(captureWidth, ex.RegionX + ex.RegionWidth);
                int endY = Math.Min(captureHeight, ex.RegionY + ex.RegionHeight);

                // Tô đen từng dòng pixel trong vùng exclusion
                for (int y = startY; y < endY; y++)
                {
                    int rowStart = y * stride + startX * 4;
                    int rowEnd = y * stride + endX * 4;

                    if (rowEnd > frame.Length) break;

                    // Zero-out toàn bộ BGRA → pixel đen hoàn toàn
                    Array.Clear(frame, rowStart, rowEnd - rowStart);
                }
            }
        }

        // Load từ config
        public void LoadFrom(List<ExclusionArea>? exclusions)
        {
            _exclusions.Clear();
            if (exclusions != null)
                _exclusions.AddRange(exclusions);
        }

        // Export cho config serialization
        public List<ExclusionArea> ToList() => new(_exclusions);
    }
}
