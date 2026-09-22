using System;
using System.Collections.Generic;
using System.Linq;
using TranslateBot.Infrastructure;

namespace TranslateBot.OCR
{
    // Quản lý danh sách vùng OCR (Section 16). Khi chưa có area nào được tạo,
    // GetEnabledAreas() trả về list rỗng — TranslationEngine sẽ fallback về
    // single-region capture cũ để backward compat với config chưa nâng cấp.
    public class OcrAreaManager
    {
        private readonly List<OcrArea> _areas = new();

        public IReadOnlyList<OcrArea> Areas => _areas.AsReadOnly();

        public void Add(OcrArea area)
        {
            _areas.Add(area);
            AppLogger.Info($"[OCR_AREA_ADDED] \"{area.Name}\" ({area.Type}) {area.RegionWidth}×{area.RegionHeight}");
        }

        public bool Remove(string id)
        {
            var area = _areas.FirstOrDefault(a => a.Id == id);
            if (area != null)
            {
                _areas.Remove(area);
                AppLogger.Info($"[OCR_AREA_REMOVED] \"{area.Name}\"");
                return true;
            }
            return false;
        }

        public void SetEnabled(string id, bool enabled)
        {
            var area = _areas.FirstOrDefault(a => a.Id == id);
            if (area != null)
            {
                area.Enabled = enabled;
            }
        }

        public List<OcrArea> GetEnabledAreas()
            => _areas.Where(a => a.Enabled && a.RegionWidth > 0 && a.RegionHeight > 0).ToList();

        // Chỉ lấy area loại Dialogue (đi vào DialogueTracker)
        public List<OcrArea> GetEnabledDialogueAreas()
            => GetEnabledAreas().Where(a => a.Type == OcrAreaType.Dialogue || a.Type == OcrAreaType.Narration).ToList();

        public void Clear() => _areas.Clear();

        // Load từ config — thay thế toàn bộ danh sách hiện tại
        public void LoadFrom(List<OcrArea>? areas)
        {
            _areas.Clear();
            if (areas != null)
            {
                _areas.AddRange(areas);
            }
        }

        // Export cho config serialization
        public List<OcrArea> ToList() => new(_areas);
    }
}
