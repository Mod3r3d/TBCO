using System;

namespace TranslateBot.Translation
{
    public enum ApiKeyStatus
    {
        Active,
        Cooldown,
        Exhausted,
        Invalid,
        Disabled
    }

    /// <summary>
    /// Đại diện cho một credential trong API Key Pool (TBCO Master Plan Section 12).
    /// </summary>
    public class ApiCredential
    {
        public string Id { get; set; } = string.Empty;
        public string Provider { get; set; } = "Gemini";
        public string Secret { get; set; } = string.Empty;
        public ApiKeyStatus Status { get; set; } = ApiKeyStatus.Active;
        public DateTime? CooldownUntil { get; set; }
        public int ConsecutiveErrors { get; set; }
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
        public double AverageLatencyMs { get; set; }
        public string? LastErrorMessage { get; set; }
        public DateTime? LastUsedAt { get; set; }

        public bool IsAvailable
        {
            get
            {
                if (Status == ApiKeyStatus.Disabled || Status == ApiKeyStatus.Invalid || Status == ApiKeyStatus.Exhausted)
                    return false;

                if (Status == ApiKeyStatus.Cooldown)
                {
                    if (CooldownUntil.HasValue && DateTime.UtcNow >= CooldownUntil.Value)
                    {
                        // Đã hết thời gian cooldown -> tự động hồi phục
                        Status = ApiKeyStatus.Active;
                        CooldownUntil = null;
                        return true;
                    }
                    return false;
                }

                return Status == ApiKeyStatus.Active;
            }
        }

        public void CheckAndResetCooldown()
        {
            if (Status == ApiKeyStatus.Cooldown && CooldownUntil.HasValue && DateTime.UtcNow >= CooldownUntil.Value)
            {
                Status = ApiKeyStatus.Active;
                CooldownUntil = null;
            }
        }

        public void RecordSuccess(TimeSpan latency)
        {
            ConsecutiveErrors = 0;
            SuccessCount++;
            LastUsedAt = DateTime.UtcNow;
            double ms = latency.TotalMilliseconds;
            AverageLatencyMs = AverageLatencyMs == 0 ? ms : (AverageLatencyMs * 0.8 + ms * 0.2);
            Status = ApiKeyStatus.Active;
            CooldownUntil = null;
        }

        public void RecordCooldown(TimeSpan duration, string reason)
        {
            Status = ApiKeyStatus.Cooldown;
            CooldownUntil = DateTime.UtcNow.Add(duration);
            ConsecutiveErrors++;
            FailureCount++;
            LastErrorMessage = reason;
            LastUsedAt = DateTime.UtcNow;
        }

        public void RecordInvalid(string reason)
        {
            Status = ApiKeyStatus.Invalid;
            ConsecutiveErrors++;
            FailureCount++;
            LastErrorMessage = reason;
            LastUsedAt = DateTime.UtcNow;
        }

        public void RecordError(string message)
        {
            ConsecutiveErrors++;
            FailureCount++;
            LastErrorMessage = message;
            LastUsedAt = DateTime.UtcNow;
        }
    }
}
