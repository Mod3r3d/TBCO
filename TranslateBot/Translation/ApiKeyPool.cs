using System;
using System.Collections.Generic;
using System.Linq;
using TranslateBot.Infrastructure;
using TranslateBot.Infrastructure.Security;

namespace TranslateBot.Translation
{
    public class ApiKeyPool : IApiKeyPool
    {
        private readonly List<ApiCredential> _credentials = new();
        private readonly object _lock = new();
        private int _roundRobinIndex = 0;

        public event Action? OnPoolStateChanged;

        public ApiKeyPool() { }

        public ApiKeyPool(IEnumerable<ApiCredential> initialCredentials)
        {
            foreach (var cred in initialCredentials)
            {
                AddOrUpdateCredential(cred);
            }
        }

        public ApiCredential? AcquireKey(string provider = "Gemini")
        {
            lock (_lock)
            {
                // Lọc các key thuộc provider được yêu cầu
                var providerKeys = _credentials
                    .Where(c => string.Equals(c.Provider, provider, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (providerKeys.Count == 0) return null;

                // Tự động kiểm tra hồi phục từ Cooldown
                foreach (var k in providerKeys)
                {
                    k.CheckAndResetCooldown();
                }

                var availableKeys = providerKeys.Where(k => k.IsAvailable).ToList();
                if (availableKeys.Count == 0)
                {
                    AppLogger.Warn($"[API_KEY_POOL] Toàn bộ {providerKeys.Count} key của [{provider}] hiện không khả dụng (đang Cooldown hoặc Invalid).");
                    return null;
                }

                // Luân chuyển đều các key bằng Round-Robin để tránh dội quota vào 1 key
                int idx = _roundRobinIndex % availableKeys.Count;
                _roundRobinIndex = (_roundRobinIndex + 1) % availableKeys.Count;
                var selectedKey = availableKeys[idx];
                selectedKey.LastUsedAt = DateTime.UtcNow;

                AppLogger.Info($"[API_KEY_ACQUIRED] Chọn key [{selectedKey.Id}] ({SecretVault.MaskSecret(selectedKey.Secret)})");
                return selectedKey;
            }
        }

        public void ReportSuccess(ApiCredential key, TimeSpan latency)
        {
            lock (_lock)
            {
                key.RecordSuccess(latency);
                AppLogger.Info($"[API_KEY_SUCCESS] [{key.Id}] hoàn thành sau {latency.TotalMilliseconds:F0}ms (Thành công: {key.SuccessCount})");
            }
            OnPoolStateChanged?.Invoke();
        }

        public void ReportRateLimit(ApiCredential key, TimeSpan cooldownDuration, string reason = "429 Rate Limit")
        {
            lock (_lock)
            {
                key.RecordCooldown(cooldownDuration, reason);
                AppLogger.Warn($"[API_KEY_COOLDOWN] [{key.Id}] vào chế độ làm nguội {cooldownDuration.TotalSeconds:F0}s. Lý do: {reason}");
            }
            OnPoolStateChanged?.Invoke();
        }

        public void ReportInvalid(ApiCredential key, string reason = "401 Invalid Token")
        {
            lock (_lock)
            {
                key.RecordInvalid(reason);
                AppLogger.Error($"[API_KEY_INVALID] [{key.Id}] bị đánh dấu KHÔNG HỢP LỆ. Lý do: {reason}");
            }
            OnPoolStateChanged?.Invoke();
        }

        public void ReportTransientError(ApiCredential key, int statusCode, string message)
        {
            lock (_lock)
            {
                key.RecordError($"HTTP {statusCode}: {message}");
                AppLogger.Warn($"[API_KEY_ERROR] [{key.Id}] gặp lỗi tạm thời {statusCode}: {message}");
            }
            OnPoolStateChanged?.Invoke();
        }

        public void ReleaseKey(ApiCredential key)
        {
            // Dự phòng cho mô hình lease nếu có
        }

        public void AddOrUpdateCredential(ApiCredential credential)
        {
            lock (_lock)
            {
                var existing = _credentials.FirstOrDefault(c => c.Id == credential.Id);
                if (existing != null)
                {
                    existing.Secret = credential.Secret;
                    existing.Provider = credential.Provider;
                    existing.Status = credential.Status;
                    existing.CooldownUntil = credential.CooldownUntil;
                }
                else
                {
                    _credentials.Add(credential);
                }
            }
            OnPoolStateChanged?.Invoke();
        }

        public bool RemoveCredential(string id)
        {
            bool removed;
            lock (_lock)
            {
                removed = _credentials.RemoveAll(c => c.Id == id) > 0;
            }
            if (removed) OnPoolStateChanged?.Invoke();
            return removed;
        }

        public IReadOnlyList<ApiCredential> GetAllCredentials()
        {
            lock (_lock)
            {
                return _credentials.ToList().AsReadOnly();
            }
        }
    }
}
