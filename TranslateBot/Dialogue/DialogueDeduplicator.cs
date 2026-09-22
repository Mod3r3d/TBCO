using System;
using System.Collections.Generic;
using System.Linq;

namespace TranslateBot.Dialogue
{
    // Chặn việc CÙNG một câu thoại bị đẩy đi dịch 2 lần liên tiếp. Trường hợp này có
    // thể xảy ra khi:
    //   - Một false split lọt qua bộ lọc OCR-correction (Section 9 của plan)
    //   - OCR đọc lại đúng câu vừa Confirmed ngay sau khi flush (frame nhiễu thoáng qua)
    //
    // LƯU Ý: đây KHÔNG phải Translation Memory (đó là Stage 5, cache dài hạn để tái sử
    // dụng bản dịch). Đây chỉ là lưới an toàn cục bộ trong vài giây gần nhất.
    public class DialogueDeduplicator
    {
        private readonly LinkedList<(string NormalizedText, DateTime ConfirmedAt)> _recent = new();
        private const int MaxRecentEntries = 5;
        private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(4);

        public bool IsDuplicate(string normalizedText)
        {
            Prune();
            return _recent.Any(entry => TextSimilarity.IsLikelyOcrCorrection(entry.NormalizedText, normalizedText));
        }

        public void Record(string normalizedText)
        {
            _recent.AddLast((normalizedText, DateTime.Now));
            while (_recent.Count > MaxRecentEntries)
            {
                _recent.RemoveFirst();
            }
        }

        public void Reset() => _recent.Clear();

        private void Prune()
        {
            var now = DateTime.Now;
            while (_recent.Count > 0 && now - _recent.First!.Value.ConfirmedAt > DedupWindow)
            {
                _recent.RemoveFirst();
            }
        }
    }
}
