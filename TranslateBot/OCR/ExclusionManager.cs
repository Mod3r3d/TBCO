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
        // offsetX, offsetY: tọa độ gốc của vùng capture (để quy đổi hệ tọa độ exclusion sang tọa độ cục bộ của frame, P0-5).
        public void ApplyExclusions(byte[] frame, int captureWidth, int captureHeight, int offsetX = 0, int offsetY = 0)
        {
            if (frame.Length == 0 || captureWidth <= 0 || captureHeight <= 0) return;

            int stride = captureWidth * 4; // BGRA = 4 bytes/pixel

            foreach (var ex in _exclusions.Where(e => e.Enabled))
            {
                // Quy đổi tọa độ exclusion sang tọa độ cục bộ của frame đã capture
                int localX = ex.RegionX - offsetX;
                int localY = ex.RegionY - offsetY;

                // Tính phần giao (intersection) giữa vùng exclusion và frame capture
                int startX = Math.Max(0, localX);
                int startY = Math.Max(0, localY);
                int endX = Math.Min(captureWidth, localX + ex.RegionWidth);
                int endY = Math.Min(captureHeight, localY + ex.RegionHeight);

                // Nếu không giao nhau thì bỏ qua
                if (startX >= endX || startY >= endY) continue;

                int rowBytes = (endX - startX) * 4;

                // Tô đen từng dòng pixel trong phần giao
                for (int y = startY; y < endY; y++)
                {
                    int rowStart = y * stride + startX * 4;

                    if (rowStart + rowBytes <= frame.Length)
                    {
                        // Zero-out toàn bộ BGRA → pixel đen hoàn toàn
                        Array.Clear(frame, rowStart, rowBytes);
                    }
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
