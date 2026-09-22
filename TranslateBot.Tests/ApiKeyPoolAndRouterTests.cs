using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Diagnostics;
using TranslateBot.Translation;

namespace TranslateBot.Tests
{
    [TestClass]
    public class ApiKeyPoolAndRouterTests
    {
        private class TestableGeminiProvider : GeminiProvider
        {
            public Func<string, string, string>? TranslationHandler { get; set; }
            public List<string> KeysUsed { get; } = new();

            public override Task<string> TranslateWithKeyAsync(string text, string apiKey, TranslationContext? context = null)
            {
                KeysUsed.Add(apiKey);
                if (TranslationHandler != null)
                {
                    return Task.FromResult(TranslationHandler(text, apiKey));
                }
                return Task.FromResult($"[Gemini: {text}]");
            }
        }

        [TestMethod]
        public void ApiKeyPool_AcquiresKey_WhenAvailable()
        {
            var pool = new ApiKeyPool();
            var key1 = new ApiCredential { Id = "key-01", Provider = "Gemini", Secret = "secret-01" };
            pool.AddOrUpdateCredential(key1);

            var acquired = pool.AcquireKey("Gemini");
            Assert.IsNotNull(acquired);
            Assert.AreEqual("key-01", acquired.Id);
        }

        [TestMethod]
        public void ApiKeyPool_RotatesBetweenKeys_RoundRobin()
        {
            var pool = new ApiKeyPool();
            pool.AddOrUpdateCredential(new ApiCredential { Id = "key-01", Provider = "Gemini", Secret = "sec-01" });
            pool.AddOrUpdateCredential(new ApiCredential { Id = "key-02", Provider = "Gemini", Secret = "sec-02" });

            var first = pool.AcquireKey("Gemini");
            var second = pool.AcquireKey("Gemini");

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreNotEqual(first.Id, second.Id, "Round-Robin phải luân chuyển qua các key khác nhau");
        }

        [TestMethod]
        public void ApiKeyPool_429Cooldown_SkipsKeyUntilExpiry()
        {
            var pool = new ApiKeyPool();
            var key1 = new ApiCredential { Id = "key-01", Provider = "Gemini", Secret = "sec-01" };
            var key2 = new ApiCredential { Id = "key-02", Provider = "Gemini", Secret = "sec-02" };
            pool.AddOrUpdateCredential(key1);
            pool.AddOrUpdateCredential(key2);

            // Báo key 1 bị 429 cooldown 60s
            pool.ReportRateLimit(key1, TimeSpan.FromSeconds(60));

            Assert.AreEqual(ApiKeyStatus.Cooldown, key1.Status);
            Assert.IsFalse(key1.IsAvailable);

            // Lần acquire tiếp theo bắt buộc phải là key 2
            var acquired = pool.AcquireKey("Gemini");
            Assert.IsNotNull(acquired);
            Assert.AreEqual("key-02", acquired.Id);
        }

        [TestMethod]
        public void ApiKeyPool_401Invalid_NeverReturnsKeyAgain()
        {
            var pool = new ApiKeyPool();
            var key1 = new ApiCredential { Id = "key-01", Provider = "Gemini", Secret = "bad-key" };
            pool.AddOrUpdateCredential(key1);

            pool.ReportInvalid(key1, "Test 401 Unauthorized");

            Assert.AreEqual(ApiKeyStatus.Invalid, key1.Status);
            Assert.IsFalse(key1.IsAvailable);

            var acquired = pool.AcquireKey("Gemini");
            Assert.IsNull(acquired, "Key Invalid không bao giờ được cấp phát lại");
        }

        [TestMethod]
        public void ApiKeyPool_AutoRecoversFromCooldown_WhenTimePasses()
        {
            var key = new ApiCredential
            {
                Id = "key-01",
                Provider = "Gemini",
                Secret = "sec-01",
                Status = ApiKeyStatus.Cooldown,
                CooldownUntil = DateTime.UtcNow.AddSeconds(-5) // Đã hết hạn cách đây 5 giây
            };

            Assert.IsTrue(key.IsAvailable, "Key hết hạn Cooldown phải tự động chuyển thành khả dụng");
            key.CheckAndResetCooldown();
            Assert.AreEqual(ApiKeyStatus.Active, key.Status);
        }

        [TestMethod]
        public async Task TranslationRouter_TranslatesSuccessfully_WithActiveKey()
        {
            var pool = new ApiKeyPool();
            pool.AddOrUpdateCredential(new ApiCredential { Id = "key-01", Provider = "Gemini", Secret = "valid-secret" });

            var gemini = new TestableGeminiProvider();
            var fallback = new FakeTranslationProvider();
            var metrics = new PerformanceMetrics();

            var router = new TranslationRouter(pool, gemini, fallback, metrics);

            string result = await router.TranslateAsync("Chaldea Security Organization");

            Assert.IsTrue(result.Contains("Chaldea Security Organization"));
            Assert.AreEqual("Gemini", router.CurrentProviderName);
            Assert.AreEqual("key-01", router.CurrentKeyId);
            Assert.AreEqual(1, gemini.KeysUsed.Count);
        }

        [TestMethod]
        public async Task TranslationRouter_RotatesToNextKey_When429Occurs()
        {
            var pool = new ApiKeyPool();
            var key1 = new ApiCredential { Id = "key-01", Provider = "Gemini", Secret = "key-429" };
            var key2 = new ApiCredential { Id = "key-02", Provider = "Gemini", Secret = "key-healthy" };
            pool.AddOrUpdateCredential(key1);
            pool.AddOrUpdateCredential(key2);

            var gemini = new TestableGeminiProvider
            {
                TranslationHandler = (text, apiKey) =>
                {
                    if (apiKey == "key-429")
                    {
                        throw new GeminiApiException(429, "Rate limit", 60);
                    }
                    return $"[Dịch thành công từ {apiKey}: {text}]";
                }
            };

            var fallback = new FakeTranslationProvider();
            var metrics = new PerformanceMetrics();
            var router = new TranslationRouter(pool, gemini, fallback, metrics);

            string result = await router.TranslateAsync("Attack enemy");

            Assert.IsTrue(result.Contains("Dịch thành công"));
            Assert.AreEqual("Gemini", router.CurrentProviderName);
            Assert.AreEqual("key-02", router.CurrentKeyId);
            Assert.AreEqual(ApiKeyStatus.Cooldown, key1.Status);
            Assert.AreEqual(1, metrics.RateLimit429Count);
            Assert.AreEqual(1, metrics.KeyRotationsCount);
        }

        [TestMethod]
        public async Task TranslationRouter_FallsBackToSecondary_WhenAllKeysUnavailable()
        {
            var pool = new ApiKeyPool();
            // Không có key nào trong pool

            var gemini = new TestableGeminiProvider();
            var fallback = new FakeTranslationProvider();
            var metrics = new PerformanceMetrics();

            var router = new TranslationRouter(pool, gemini, fallback, metrics);

            string result = await router.TranslateAsync("Senpai, look!");

            Assert.IsTrue(result.Contains("Dịch giả lập"), "Phải chuyển tiếp thành công sang fallback provider");
            Assert.AreEqual("GoogleWebTranslate", router.CurrentProviderName);
            Assert.IsNull(router.CurrentKeyId);
            Assert.AreEqual(1, fallback.CallCount);
        }
    }
}
