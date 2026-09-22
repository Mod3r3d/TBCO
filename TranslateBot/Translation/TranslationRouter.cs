using System;
using System.Diagnostics;
using System.Threading.Tasks;
using TranslateBot.Diagnostics;
using TranslateBot.Infrastructure;
using TranslateBot.Translation.Providers;

namespace TranslateBot.Translation
{
    /// <summary>
    /// Bộ điều hướng dịch thuật trung tâm Router 2.0 (TBCO Master Plan Section 13, 14).
    /// Quản lý chuỗi fallback: Gemini Key Pool -> DeepL -> Custom API -> Local AI (Ollama) -> Google Web Translate.
    /// Hỗ trợ Offline Mode và tính điểm sức khỏe ProviderHealthTracker.
    /// </summary>
    public class TranslationRouter : ITranslationProvider
    {
        private readonly IApiKeyPool _keyPool;
        private readonly GeminiProvider _geminiProvider;
        private readonly ITranslationProvider _fallbackProvider;
        private readonly ProviderHealthTracker _healthTracker;

        public IApiKeyPool KeyPool => _keyPool;
        public GeminiProvider GeminiProvider => _geminiProvider;
        public ITranslationProvider FallbackProvider => _fallbackProvider;
        public ProviderHealthTracker HealthTracker => _healthTracker;
        public PerformanceMetrics? Metrics { get; set; }

        public DeepLProvider? DeepLProvider { get; set; }
        public CustomApiTranslationProvider? CustomApiProvider { get; set; }
        public OllamaTranslationProvider? LocalAiProvider { get; set; }

        public bool OfflineMode { get; set; } = false;

        public string CurrentProviderName { get; private set; } = "Gemini";
        public string? CurrentKeyId { get; private set; }

        public TranslationRouter(
            IApiKeyPool keyPool,
            GeminiProvider geminiProvider,
            ITranslationProvider fallbackProvider,
            PerformanceMetrics? metrics = null,
            DeepLProvider? deepLProvider = null,
            CustomApiTranslationProvider? customApiProvider = null,
            OllamaTranslationProvider? localAiProvider = null,
            ProviderHealthTracker? healthTracker = null)
        {
            _keyPool = keyPool;
            _geminiProvider = geminiProvider;
            _geminiProvider.ThrowOnApiError = true; // Bật chế độ ném ngoại lệ để Router kiểm soát
            _fallbackProvider = fallbackProvider;
            Metrics = metrics;

            DeepLProvider = deepLProvider;
            CustomApiProvider = customApiProvider;
            LocalAiProvider = localAiProvider;
            _healthTracker = healthTracker ?? new ProviderHealthTracker();
        }

        public Task<string> TranslateAsync(string text)
            => TranslateAsync(text, null);

        public async Task<string> TranslateAsync(string text, TranslationContext? context)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // 0. Chế độ Offline Mode: Chỉ dịch qua Local AI (Ollama) mà không gọi Cloud
            if (OfflineMode)
            {
                if (LocalAiProvider != null)
                {
                    CurrentProviderName = "LocalAI";
                    CurrentKeyId = null;
                    var swLocal = Stopwatch.StartNew();
                    try
                    {
                        string localResult = await LocalAiProvider.TranslateAsync(text, context);
                        swLocal.Stop();
                        if (!string.IsNullOrEmpty(localResult))
                        {
                            _healthTracker.ReportSuccess("LocalAI", swLocal.Elapsed);
                            Metrics?.RecordTranslationRequest(swLocal.Elapsed.TotalMilliseconds, true);
                            return localResult;
                        }
                    }
                    catch (Exception ex)
                    {
                        swLocal.Stop();
                        _healthTracker.ReportFailure("LocalAI", ex.Message);
                        AppLogger.Warn($"[ROUTER_OFFLINE_FAIL] Local AI lỗi: {ex.Message}");
                    }
                }

                AppLogger.Warn("[ROUTER_OFFLINE] Đang ở chế độ Offline nhưng không thể dịch qua Local AI.");
                return string.Empty;
            }

            // 1. Thử qua Gemini Key Pool (Cloud Primary)
            var primaryKey = _keyPool.AcquireKey("Gemini");
            if (primaryKey != null)
            {
                var result = await TryTranslateWithGeminiKeyAsync(primaryKey, text, context);
                if (!string.IsNullOrEmpty(result))
                {
                    return result;
                }

                // Nếu key ban đầu bị 429 hoặc lỗi, thử ngay key tiếp theo trong Pool
                var secondaryKey = _keyPool.AcquireKey("Gemini");
                if (secondaryKey != null && secondaryKey.Id != primaryKey.Id)
                {
                    AppLogger.Info($"[ROUTER_ROTATION] Tự động xoay sang key [{secondaryKey.Id}]...");
                    if (Metrics != null) Metrics.KeyRotationsCount++;

                    var result2 = await TryTranslateWithGeminiKeyAsync(secondaryKey, text, context);
                    if (!string.IsNullOrEmpty(result2))
                    {
                        return result2;
                    }
                }
            }

            // 2. Thử DeepL Provider (Cloud Secondary)
            if (DeepLProvider != null && !string.IsNullOrWhiteSpace(DeepLProvider.ApiKey))
            {
                CurrentProviderName = "DeepL";
                CurrentKeyId = null;
                var swDeepL = Stopwatch.StartNew();
                try
                {
                    string deepLResult = await DeepLProvider.TranslateAsync(text, context);
                    swDeepL.Stop();
                    if (!string.IsNullOrEmpty(deepLResult))
                    {
                        AppLogger.Info("[ROUTER_SUCCESS] Dịch thành công qua DeepL.");
                        _healthTracker.ReportSuccess("DeepL", swDeepL.Elapsed);
                        Metrics?.RecordTranslationRequest(swDeepL.Elapsed.TotalMilliseconds, true);
                        return deepLResult;
                    }
                    _healthTracker.ReportFailure("DeepL", "Kết quả trả về rỗng");
                }
                catch (Exception ex)
                {
                    swDeepL.Stop();
                    _healthTracker.ReportFailure("DeepL", ex.Message);
                    AppLogger.Warn($"[ROUTER_DEEPL_FAIL] DeepL lỗi: {ex.Message}");
                }
            }

            // 3. Thử Custom API Provider (OpenAI-compatible)
            if (CustomApiProvider != null && !string.IsNullOrWhiteSpace(CustomApiProvider.EndpointUrl))
            {
                CurrentProviderName = "CustomAPI";
                CurrentKeyId = null;
                var swCustom = Stopwatch.StartNew();
                try
                {
                    string customResult = await CustomApiProvider.TranslateAsync(text, context);
                    swCustom.Stop();
                    if (!string.IsNullOrEmpty(customResult))
                    {
                        AppLogger.Info("[ROUTER_SUCCESS] Dịch thành công qua Custom API.");
                        _healthTracker.ReportSuccess("CustomAPI", swCustom.Elapsed);
                        Metrics?.RecordTranslationRequest(swCustom.Elapsed.TotalMilliseconds, true);
                        return customResult;
                    }
                    _healthTracker.ReportFailure("CustomAPI", "Kết quả trả về rỗng");
                }
                catch (Exception ex)
                {
                    swCustom.Stop();
                    _healthTracker.ReportFailure("CustomAPI", ex.Message);
                    AppLogger.Warn($"[ROUTER_CUSTOM_API_FAIL] Custom API lỗi: {ex.Message}");
                }
            }

            // 4. Thử Local AI (Ollama)
            if (LocalAiProvider != null)
            {
                CurrentProviderName = "LocalAI";
                CurrentKeyId = null;
                var swLocal = Stopwatch.StartNew();
                try
                {
                    string localResult = await LocalAiProvider.TranslateAsync(text, context);
                    swLocal.Stop();
                    if (!string.IsNullOrEmpty(localResult))
                    {
                        AppLogger.Info("[ROUTER_SUCCESS] Dịch thành công qua Local AI (Ollama).");
                        _healthTracker.ReportSuccess("LocalAI", swLocal.Elapsed);
                        Metrics?.RecordTranslationRequest(swLocal.Elapsed.TotalMilliseconds, true);
                        return localResult;
                    }
                    _healthTracker.ReportFailure("LocalAI", "Kết quả trả về rỗng");
                }
                catch (Exception ex)
                {
                    swLocal.Stop();
                    _healthTracker.ReportFailure("LocalAI", ex.Message);
                    AppLogger.Warn($"[ROUTER_LOCAL_AI_FAIL] Local AI lỗi: {ex.Message}");
                }
            }

            // 5. Toàn bộ Cloud & Local Provider chính lỗi -> Fallback sang Google Web Translate
            CurrentProviderName = "GoogleWebTranslate";
            CurrentKeyId = null;
            AppLogger.Warn("[ROUTER_FALLBACK] Chuyển tiếp sang Google Web Translate để không ngắt quãng trải nghiệm.");

            var swFallback = Stopwatch.StartNew();
            try
            {
                string fallbackResult = await _fallbackProvider.TranslateAsync(text, context);
                swFallback.Stop();
                bool isOk = !string.IsNullOrEmpty(fallbackResult);
                if (isOk) _healthTracker.ReportSuccess("GoogleWebTranslate", swFallback.Elapsed);
                else _healthTracker.ReportFailure("GoogleWebTranslate", "Rỗng");

                Metrics?.RecordTranslationRequest(swFallback.Elapsed.TotalMilliseconds, isOk);
                return fallbackResult;
            }
            catch (Exception ex)
            {
                swFallback.Stop();
                _healthTracker.ReportFailure("GoogleWebTranslate", ex.Message);
                AppLogger.Error($"[ROUTER_FALLBACK_ERROR] Nhà cung cấp dự phòng lỗi: {ex.Message}");
                Metrics?.RecordTranslationRequest(swFallback.Elapsed.TotalMilliseconds, false);
                return string.Empty;
            }
        }

        private async Task<string> TryTranslateWithGeminiKeyAsync(ApiCredential key, string text, TranslationContext? context)
        {
            CurrentProviderName = "Gemini";
            CurrentKeyId = key.Id;

            var sw = Stopwatch.StartNew();
            try
            {
                string result = await _geminiProvider.TranslateWithKeyAsync(text, key.Secret, context);
                sw.Stop();

                bool isSuccess = !string.IsNullOrEmpty(result) && !result.StartsWith("[Chưa dịch được");
                if (isSuccess)
                {
                    _keyPool.ReportSuccess(key, sw.Elapsed);
                    _healthTracker.ReportSuccess("Gemini", sw.Elapsed);
                    Metrics?.RecordTranslationRequest(sw.Elapsed.TotalMilliseconds, true);
                    return result;
                }
                else
                {
                    _keyPool.ReportTransientError(key, 0, "Kết quả trả về rỗng");
                    _healthTracker.ReportFailure("Gemini", "Rỗng");
                    Metrics?.RecordTranslationRequest(sw.Elapsed.TotalMilliseconds, false);
                    return string.Empty;
                }
            }
            catch (GeminiApiException ex)
            {
                sw.Stop();
                _healthTracker.ReportFailure("Gemini", ex.Message);
                if (ex.StatusCode == 429)
                {
                    int delay = ex.RetryDelaySeconds ?? 60;
                    _keyPool.ReportRateLimit(key, TimeSpan.FromSeconds(delay), "429 Quota Exceeded");
                    Metrics?.RecordTranslationRequest(sw.Elapsed.TotalMilliseconds, false, isRateLimit: true);
                }
                else if (ex.StatusCode == 401)
                {
                    _keyPool.ReportInvalid(key, "401 Invalid Token / Unauthorized");
                    Metrics?.RecordTranslationRequest(sw.Elapsed.TotalMilliseconds, false);
                }
                else
                {
                    _keyPool.ReportTransientError(key, ex.StatusCode, ex.Message);
                    Metrics?.RecordTranslationRequest(sw.Elapsed.TotalMilliseconds, false);
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _healthTracker.ReportFailure("Gemini", ex.Message);
                _keyPool.ReportTransientError(key, 0, ex.Message);
                Metrics?.RecordTranslationRequest(sw.Elapsed.TotalMilliseconds, false);
                return string.Empty;
            }
        }
    }
}
