using System;

namespace TranslateBot.Database
{
    /// <summary>
    /// Giao diện chuẩn cho kho lưu trữ bộ nhớ dịch thuật (TBCO Master Plan Section 6).
    /// Hỗ trợ cả lưu trữ Persistent SQLite và in-memory.
    /// </summary>
    public interface IMemoryStore : IDisposable
    {
        string? TryGet(string normalizedText);

        void Store(
            string normalizedText,
            string originalText,
            string translatedText,
            string? speaker = null,
            string provider = "Gemini",
            string? model = null);

        bool TryGetFuzzy(string normalizedText, out string? canonicalText, out string? translation);

        void StoreFuzzyMapping(string variantText, string canonicalText);

        int GetCount();

        void Clear();
    }
}
