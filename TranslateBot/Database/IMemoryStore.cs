using System;
using TranslateBot.Translation;

namespace TranslateBot.Database
{
    /// <summary>
    /// Giao diện chuẩn cho kho lưu trữ bộ nhớ dịch thuật (TBCO Master Plan Section 6 & Giai đoạn 4).
    /// Hỗ trợ cả lưu trữ Persistent SQLite và in-memory với cô lập theo Game Profile.
    /// </summary>
    public interface IMemoryStore : IDisposable
    {
        // 1. Truy vấn cơ bản
        string? TryGet(string normalizedText);

        // 2. Truy vấn có ngữ cảnh (Profile và Cặp ngôn ngữ)
        string? TryGet(string normalizedText, string profileId, string sourceLang = "auto", string targetLang = "vi")
            => TryGet(normalizedText);

        // 3. Truy vấn đầy đủ Entry mang theo metadata/provenance
        TranslationMemoryEntry? TryGetEntry(string normalizedText, string profileId = "global", string sourceLang = "auto", string targetLang = "vi", bool allowFallbackDegraded = false)
        {
            string? trans = TryGet(normalizedText, profileId, sourceLang, targetLang);
            if (string.IsNullOrEmpty(trans)) return null;
            return new TranslationMemoryEntry
            {
                NormalizedText = normalizedText,
                Translation = trans,
                ProfileId = profileId,
                SourceLanguage = sourceLang,
                TargetLanguage = targetLang
            };
        }

        // 4. Lưu bản dịch cơ bản
        void Store(
            string normalizedText,
            string originalText,
            string translatedText,
            string? speaker = null,
            string provider = "Gemini",
            string? model = null);

        // 5. Lưu bản dịch kèm toàn bộ metadata và provenance
        void Store(TranslationMemoryEntry entry)
        {
            Store(
                entry.NormalizedText,
                string.IsNullOrEmpty(entry.OriginalText) ? entry.NormalizedText : entry.OriginalText,
                entry.Translation,
                entry.Speaker,
                entry.Provider,
                entry.Model);
        }

        // 6. Fuzzy Match cơ bản
        bool TryGetFuzzy(string normalizedText, out string? canonicalText, out string? translation);

        // 7. Fuzzy Match cô lập theo Profile và Ngôn ngữ
        bool TryGetFuzzy(
            string normalizedText, 
            string profileId, 
            string sourceLang, 
            string targetLang, 
            out string? canonicalText, 
            out string? translation)
        {
            return TryGetFuzzy(normalizedText, out canonicalText, out translation);
        }

        // 8. Lưu biến thể Fuzzy
        void StoreFuzzyMapping(string variantText, string canonicalText);

        void StoreFuzzyMapping(
            string variantText, 
            string canonicalText, 
            string profileId = "global", 
            string sourceLang = "auto", 
            string targetLang = "vi")
        {
            StoreFuzzyMapping(variantText, canonicalText);
        }

        int GetCount();
        int GetCountByProfile(string profileId) => GetCount();

        void Clear();
        void ClearByProfile(string profileId);
    }
}
