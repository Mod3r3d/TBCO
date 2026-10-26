using System;

namespace TranslateBot.Translation
{
    public enum CacheEntryType
    {
        ModelOutput = 0,        // Kết quả từ LLM chuẩn (Gemini, DeepL, Ollama)
        UserApproved = 1,       // Bản dịch do người dùng chỉnh sửa/xác nhận (ưu tiên cao nhất)
        FallbackDegraded = 2,   // Kết quả fallback chất lượng thấp (GoogleWeb thô) - không dùng làm canonical
        ImportedGlossary = 3    // Dữ liệu nhập từ file từ điển hoặc harness
    }

    /// <summary>
    /// Cache key phân tách ngữ cảnh theo Version, Game/Profile và Cặp ngôn ngữ (Giai đoạn 4 của plan.md).
    /// Đảm bảo cùng một câu thoại trong hai Game Profile khác nhau (VD: FGO vs Visual Novel)
    /// không bị trả nhầm cache lẫn nhau.
    /// </summary>
    public class TranslationCacheKey
    {
        public string NormalizedText { get; }
        public string ProfileId { get; }
        public string SourceLanguage { get; }
        public string TargetLanguage { get; }
        public int Version { get; }

        public TranslationCacheKey(
            string normalizedText, 
            string profileId = "global", 
            string sourceLanguage = "auto", 
            string targetLanguage = "vi",
            int version = 1)
        {
            NormalizedText = normalizedText?.Trim() ?? string.Empty;
            ProfileId = string.IsNullOrWhiteSpace(profileId) ? "global" : profileId.Trim().ToLowerInvariant();
            SourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage.Trim().ToLowerInvariant();
            TargetLanguage = string.IsNullOrWhiteSpace(targetLanguage) ? "vi" : targetLanguage.Trim().ToLowerInvariant();
            Version = version;
        }

        public string ToKeyString()
        {
            return $"v{Version}::{ProfileId}::{SourceLanguage}->{TargetLanguage}::{NormalizedText}";
        }

        public static string BuildKey(
            string normalizedText, 
            string? profileId = null, 
            string? sourceLang = null, 
            string? targetLang = null,
            int version = 1)
        {
            return new TranslationCacheKey(
                normalizedText, 
                profileId ?? "global", 
                sourceLang ?? "auto", 
                targetLang ?? "vi", 
                version).ToKeyString();
        }

        public override string ToString() => ToKeyString();

        public override bool Equals(object? obj)
        {
            if (obj is TranslationCacheKey other)
            {
                return string.Equals(ToKeyString(), other.ToKeyString(), StringComparison.Ordinal);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return ToKeyString().GetHashCode();
        }
    }

    /// <summary>
    /// Bản ghi đầy đủ của một mục Translation Memory mang theo toàn bộ metadata và provenance.
    /// </summary>
    public class TranslationMemoryEntry
    {
        public string NormalizedText { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
        public string OriginalText { get; set; } = string.Empty;
        public string ProfileId { get; set; } = "global";
        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "vi";
        public string? Speaker { get; set; }
        public string Provider { get; set; } = "Gemini";
        public string? Model { get; set; }
        public CacheEntryType EntryType { get; set; } = CacheEntryType.ModelOutput;
        public bool IsUserApproved { get; set; }
        public bool IsFallback { get; set; }
        public string? ReviewerSource { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
        public int UseCount { get; set; } = 1;
        public double Confidence { get; set; } = 1.0;

        public string GetCacheKey(int version = 1)
        {
            return TranslationCacheKey.BuildKey(NormalizedText, ProfileId, SourceLanguage, TargetLanguage, version);
        }
    }
}
