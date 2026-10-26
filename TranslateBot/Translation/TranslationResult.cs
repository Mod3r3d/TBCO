using System;

namespace TranslateBot.Translation
{
    public enum TranslationStatus
    {
        Success,
        Fallback,
        RateLimited429,
        Unauthorized401,
        SafetyBlocked,
        EmptyOutput,
        Timeout,
        ProviderError,
        Cancelled
    }

    /// <summary>
    /// Kết quả dịch thuật mang đầy đủ metadata snapshot độc lập theo từng request (Stage 3 của plan.md).
    /// Loại bỏ rủi ro sai lệch provider attribution và race condition khi chạy song song.
    /// </summary>
    public class TranslationResult
    {
        public string Text { get; set; } = "";
        public string ProviderName { get; set; } = "";
        public string? ModelName { get; set; }
        public string? KeyIdentifier { get; set; }
        public bool IsFallback { get; set; }
        public string? FallbackReason { get; set; }
        public double LatencyMs { get; set; }
        public bool IsCacheHit { get; set; }
        public TranslationStatus Status { get; set; } = TranslationStatus.Success;
        public string? ErrorMessage { get; set; }

        public bool IsSuccess => 
            (Status == TranslationStatus.Success || Status == TranslationStatus.Fallback) && 
            !string.IsNullOrWhiteSpace(Text);

        public static TranslationResult CreateSuccess(
            string text, 
            string provider, 
            string? model = null, 
            double latency = 0, 
            bool isCacheHit = false, 
            string? keyId = null)
        {
            return new TranslationResult
            {
                Text = text,
                ProviderName = provider,
                ModelName = model,
                LatencyMs = latency,
                IsCacheHit = isCacheHit,
                KeyIdentifier = keyId,
                Status = TranslationStatus.Success
            };
        }

        public static TranslationResult CreateFallback(
            string text, 
            string primaryProvider, 
            string fallbackProvider, 
            string reason, 
            double latency = 0)
        {
            return new TranslationResult
            {
                Text = text,
                ProviderName = fallbackProvider,
                IsFallback = true,
                FallbackReason = $"{primaryProvider} -> {fallbackProvider}: {reason}",
                LatencyMs = latency,
                Status = TranslationStatus.Fallback
            };
        }

        public static TranslationResult CreateFailure(
            string provider, 
            TranslationStatus status, 
            string errorMessage, 
            double latency = 0)
        {
            return new TranslationResult
            {
                Text = "",
                ProviderName = provider,
                Status = status,
                ErrorMessage = errorMessage,
                LatencyMs = latency
            };
        }
    }
}
