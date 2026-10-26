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

        public string PrimaryProvider { get; set; } = "Gemini";

        public string CurrentProviderName { get; private set; } = "Gemini";
        public string? CurrentKeyId { get; private set; }

        public event Action<string, string, string>? OnFallbackTriggered;

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
            var request = new TranslationRequest(text, context);
            var result = await TranslateRequestAsync(request);
            return result.Text;
        }

        // Stage 3: Hợp đồng dữ liệu translation có kiểu rõ ràng và metadata snapshot theo request
        public async Task<TranslationResult> TranslateRequestAsync(TranslationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return TranslationResult.CreateFailure(PrimaryProvider, TranslationStatus.EmptyOutput, "Văn bản yêu cầu rỗng");
            }

            var swTotal = Stopwatch.StartNew();

            // 0. Chế độ Offline Mode: Chỉ dịch qua Local AI (Ollama)
            if (OfflineMode)
            {
                var localSw = Stopwatch.StartNew();
                string local = await TryTranslateWithLocalAiAsync(request.Text, request.Context);
                localSw.Stop();
                if (!string.IsNullOrEmpty(local))
                {
                    CurrentProviderName = "LocalAI";
                    return TranslationResult.CreateSuccess(local, "LocalAI", "Ollama", localSw.Elapsed.TotalMilliseconds);
                }
                AppLogger.Warn("[ROUTER_OFFLINE] Đang ở chế độ Offline nhưng không thể dịch qua Local AI.");
                return TranslationResult.CreateFailure("LocalAI", TranslationStatus.ProviderError, "Offline Mode: Không thể dịch qua Local AI", localSw.Elapsed.TotalMilliseconds);
            }

            // A. Nếu người dùng chọn DeepL làm Primary
            if (PrimaryProvider.Equals("DeepL", StringComparison.OrdinalIgnoreCase))
            {
                var dSw = Stopwatch.StartNew();
                string deepL = await TryTranslateWithDeepLAsync(request.Text, request.Context);
                dSw.Stop();
                if (!string.IsNullOrEmpty(deepL))
                {
                    return TranslationResult.CreateSuccess(deepL, "DeepL", "DeepL-API", dSw.Elapsed.TotalMilliseconds);
                }

                // Fallback 1: Gemini Key Pool
                var (gemini, gemKey) = await TryTranslateWithGeminiPoolWithKeyAsync(request.Text, request.Context);
                if (!string.IsNullOrEmpty(gemini))
                {
                    OnFallbackTriggered?.Invoke("DeepL", "Gemini", "DeepL gặp sự cố -> Fallback sang Gemini");
                    return TranslationResult.CreateFallback(gemini, "DeepL", "Gemini", "DeepL không khả dụng", swTotal.Elapsed.TotalMilliseconds);
                }

                // Fallback 2: Custom API / Local AI
                string custom = await TryTranslateWithCustomApiAsync(request.Text, request.Context);
                if (!string.IsNullOrEmpty(custom))
                {
                    OnFallbackTriggered?.Invoke("DeepL", "CustomAPI", "Gemini không khả dụng -> Fallback sang Custom API");
                    return TranslationResult.CreateFallback(custom, "DeepL", "CustomAPI", "Gemini không khả dụng", swTotal.Elapsed.TotalMilliseconds);
                }

                string local = await TryTranslateWithLocalAiAsync(request.Text, request.Context);
                if (!string.IsNullOrEmpty(local))
                {
                    OnFallbackTriggered?.Invoke("DeepL", "LocalAI", "Custom API không khả dụng -> Fallback sang Local AI");
                    return TranslationResult.CreateFallback(local, "DeepL", "LocalAI", "Custom API không khả dụng", swTotal.Elapsed.TotalMilliseconds);
                }

                // Fallback cuối: Google Web Translate
                string web = await TryTranslateWithFallbackAsync(request.Text, request.Context);
                return TranslationResult.CreateFallback(web, "DeepL", "GoogleWebTranslate", "Toàn bộ AI Provider không khả dụng", swTotal.Elapsed.TotalMilliseconds);
            }

            // B. Nếu người dùng chọn GoogleWeb làm Primary
            if (PrimaryProvider.Equals("GoogleWeb", StringComparison.OrdinalIgnoreCase) ||
                PrimaryProvider.Equals("GoogleWebTranslate", StringComparison.OrdinalIgnoreCase))
            {
                var wSw = Stopwatch.StartNew();
                string web = await TryTranslateWithFallbackAsync(request.Text, request.Context);
                wSw.Stop();
                if (!string.IsNullOrEmpty(web))
                {
                    return TranslationResult.CreateSuccess(web, "GoogleWebTranslate", "WebAPI", wSw.Elapsed.TotalMilliseconds);
                }

                // Fallback: Gemini -> DeepL
                var (gemini, gemKey) = await TryTranslateWithGeminiPoolWithKeyAsync(request.Text, request.Context);
                if (!string.IsNullOrEmpty(gemini))
                {
                    return TranslationResult.CreateFallback(gemini, "GoogleWeb", "Gemini", "GoogleWeb không khả dụng", swTotal.Elapsed.TotalMilliseconds);
                }

                string deepL = await TryTranslateWithDeepLAsync(request.Text, request.Context);
                if (!string.IsNullOrEmpty(deepL))
                {
                    return TranslationResult.CreateFallback(deepL, "GoogleWeb", "DeepL", "Gemini không khả dụng", swTotal.Elapsed.TotalMilliseconds);
                }

                return TranslationResult.CreateFailure("GoogleWebTranslate", TranslationStatus.ProviderError, "Không thể dịch qua Google Web Translate", swTotal.Elapsed.TotalMilliseconds);
            }

            // C. Mặc định: Gemini làm Primary
            {
                var (gemini, gemKey) = await TryTranslateWithGeminiPoolWithKeyAsync(request.Text, request.Context);
                if (!string.IsNullOrEmpty(gemini))
                {
                    return TranslationResult.CreateSuccess(gemini, "Gemini", _geminiProvider.ModelName, swTotal.Elapsed.TotalMilliseconds, keyId: gemKey?.Id);
                }

                // Fallback 1: DeepL
                string deepL = await TryTranslateWithDeepLAsync(request.Text, request.Context);
                if (!string.IsNullOrEmpty(deepL))
                {
                    OnFallbackTriggered?.Invoke("Gemini", "DeepL", "Gemini Key Pool cạn kiệt hoặc lỗi -> Fallback sang DeepL");
                    return TranslationResult.CreateFallback(deepL, "Gemini", "DeepL", "Gemini Key Pool cạn kiệt hoặc lỗi", swTotal.Elapsed.TotalMilliseconds);
                }

                // Fallback 2: Custom API
                string custom = await TryTranslateWithCustomApiAsync(request.Text, request.Context);
                if (!string.IsNullOrEmpty(custom))
                {
                    OnFallbackTriggered?.Invoke("Gemini", "CustomAPI", "DeepL không khả dụng -> Fallback sang Custom API");
                    return TranslationResult.CreateFallback(custom, "Gemini", "CustomAPI", "DeepL không khả dụng", swTotal.Elapsed.TotalMilliseconds);
                }

                // Fallback 3: Local AI
                string local = await TryTranslateWithLocalAiAsync(request.Text, request.Context);
                if (!string.IsNullOrEmpty(local))
                {
                    OnFallbackTriggered?.Invoke("Gemini", "LocalAI", "Custom API không khả dụng -> Fallback sang Local AI");
                    return TranslationResult.CreateFallback(local, "Gemini", "LocalAI", "Custom API không khả dụng", swTotal.Elapsed.TotalMilliseconds);
                }

                // Fallback cuối: Google Web Translate
                string web = await TryTranslateWithFallbackAsync(request.Text, request.Context);
                return TranslationResult.CreateFallback(web, "Gemini", "GoogleWebTranslate", "Toàn bộ AI Provider không khả dụng", swTotal.Elapsed.TotalMilliseconds);
            }
        }

        public async Task<(string result, ApiCredential? key)> TryTranslateWithGeminiPoolWithKeyAsync(string text, TranslationContext? context)
        {
            var primaryKey = _keyPool.AcquireKey("Gemini");
            if (primaryKey != null)
            {
                var result = await TryTranslateWithGeminiKeyAsync(primaryKey, text, context);
                if (!string.IsNullOrEmpty(result)) return (result, primaryKey);

                // Nếu key ban đầu bị 429 hoặc lỗi, thử ngay key tiếp theo trong Pool
                var secondaryKey = _keyPool.AcquireKey("Gemini");
                if (secondaryKey != null && secondaryKey.Id != primaryKey.Id)
                {
                    AppLogger.Info($"[ROUTER_ROTATION] Tự động xoay sang key [{secondaryKey.Id}]...");
                    if (Metrics != null) Metrics.KeyRotationsCount++;

                    var result2 = await TryTranslateWithGeminiKeyAsync(secondaryKey, text, context);
                    if (!string.IsNullOrEmpty(result2)) return (result2, secondaryKey);
                }
            }
            return (string.Empty, null);
        }

        public async Task<string> TryTranslateWithGeminiPoolAsync(string text, TranslationContext? context)
        {
            var (result, _) = await TryTranslateWithGeminiPoolWithKeyAsync(text, context);
            return result;
        }

        public async Task<string> TryTranslateWithDeepLAsync(string text, TranslationContext? context)
        {
            if (DeepLProvider == null || string.IsNullOrWhiteSpace(DeepLProvider.ApiKey)) return string.Empty;
            if (!_healthTracker.IsAvailable("DeepL"))
            {
                AppLogger.Warn("[ROUTER_CIRCUIT_BREAKER] DeepL đang ở trạng thái Unhealthy, tạm thời bỏ qua (P1-7).");
                return string.Empty;
            }

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
            return string.Empty;
        }

        public async Task<string> TryTranslateWithCustomApiAsync(string text, TranslationContext? context)
        {
            if (CustomApiProvider == null || string.IsNullOrWhiteSpace(CustomApiProvider.EndpointUrl)) return string.Empty;
            if (!_healthTracker.IsAvailable("CustomAPI"))
            {
                AppLogger.Warn("[ROUTER_CIRCUIT_BREAKER] Custom API đang ở trạng thái Unhealthy, tạm thời bỏ qua (P1-7).");
                return string.Empty;
            }

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
            return string.Empty;
        }

        public async Task<string> TryTranslateWithLocalAiAsync(string text, TranslationContext? context)
        {
            if (LocalAiProvider == null) return string.Empty;
            if (!_healthTracker.IsAvailable("LocalAI"))
            {
                AppLogger.Warn("[ROUTER_CIRCUIT_BREAKER] Local AI đang ở trạng thái Unhealthy, tạm thời bỏ qua (P1-7).");
                return string.Empty;
            }

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
            return string.Empty;
        }

        public async Task<string> TryTranslateWithFallbackAsync(string text, TranslationContext? context)
        {
            CurrentProviderName = "GoogleWebTranslate";
            CurrentKeyId = null;
            AppLogger.Warn("[ROUTER_FALLBACK] Chuyển tiếp sang Google Web Translate để không ngắt quãng trải nghiệm.");
            OnFallbackTriggered?.Invoke(PrimaryProvider, "GoogleWebTranslate", "Bộ dịch chính gặp sự cố, tạm chuyển sang Google Web Translate");

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
                else if (ex.StatusCode == 401 ||
                         (ex.StatusCode == 400 && (ex.RawResponse?.Contains("API_KEY_INVALID") == true ||
                                                   ex.RawResponse?.Contains("API key not valid") == true ||
                                                   ex.Message?.Contains("API key not valid") == true)))
                {
                    _keyPool.ReportInvalid(key, $"HTTP {ex.StatusCode} Invalid API Key");
                    Metrics?.RecordTranslationRequest(sw.Elapsed.TotalMilliseconds, false);
                }
                else
                {
                    _keyPool.ReportTransientError(key, ex.StatusCode, ex.Message ?? "Unknown error");
                    Metrics?.RecordTranslationRequest(sw.Elapsed.TotalMilliseconds, false);
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _healthTracker.ReportFailure("Gemini", ex.Message);
                _keyPool.ReportTransientError(key, 0, ex.Message ?? "Unknown error");
                Metrics?.RecordTranslationRequest(sw.Elapsed.TotalMilliseconds, false);
                return string.Empty;
            }
        }
    }
}
