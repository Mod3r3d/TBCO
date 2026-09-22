using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TranslateBot.Translation;

namespace TranslateBot.Tests
{
    public enum FakeScenario
    {
        AlwaysSuccess,
        RateLimit429,
        Unauthorized401,
        ServerError503,
        Timeout,
        EmptyResponse
    }

    /// <summary>
    /// Giả lập nhà cung cấp API cho các bài kiểm thử Fault Injection (Phase 16).
    /// </summary>
    public class FakeTranslationProvider : ITranslationProvider
    {
        public FakeScenario Scenario { get; set; } = FakeScenario.AlwaysSuccess;
        public int CallCount { get; private set; }
        public List<string> CallLog { get; } = new();
        public int CooldownSeconds { get; set; } = 60;

        public Task<string> TranslateAsync(string text)
            => TranslateAsync(text, null);

        public Task<string> TranslateAsync(string text, TranslationContext? context)
        {
            CallCount++;
            CallLog.Add(text);

            switch (Scenario)
            {
                case FakeScenario.AlwaysSuccess:
                    return Task.FromResult($"[Dịch giả lập: {text}]");

                case FakeScenario.RateLimit429:
                    throw new GeminiApiException(429, "Rate limit exceeded (Fake 429)", CooldownSeconds, "{\"error\":{\"code\":429}}");

                case FakeScenario.Unauthorized401:
                    throw new GeminiApiException(401, "API key not valid (Fake 401)", null, "{\"error\":{\"code\":401}}");

                case FakeScenario.ServerError503:
                    throw new GeminiApiException(503, "Service unavailable (Fake 503)", null, "{\"error\":{\"code\":503}}");

                case FakeScenario.Timeout:
                    throw new TimeoutException("Fake request timeout");

                case FakeScenario.EmptyResponse:
                    return Task.FromResult(string.Empty);

                default:
                    return Task.FromResult($"[Dịch: {text}]");
            }
        }
    }
}
