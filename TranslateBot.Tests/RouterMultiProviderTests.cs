using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Diagnostics;
using TranslateBot.Translation;
using TranslateBot.Translation.Providers;

namespace TranslateBot.Tests
{
    [TestClass]
    public class RouterMultiProviderTests
    {
        private class TestableGeminiProvider : GeminiProvider
        {
            public Func<string, string>? OnTranslate { get; set; }

            public override Task<string> TranslateWithKeyAsync(string text, string apiKey, TranslationContext? context = null)
            {
                if (OnTranslate != null)
                {
                    return Task.FromResult(OnTranslate(text));
                }
                return Task.FromResult($"[Gemini: {text}]");
            }
        }

        private class TestableDeepLProvider : DeepLProvider
        {
            public Func<string, string>? OnTranslate { get; set; }

            public TestableDeepLProvider(string apiKey) : base(apiKey) { }

            public override Task<string> SendHttpRequestAsync(string endpoint, HttpContent content, string apiKey)
            {
                if (OnTranslate != null)
                {
                    string res = OnTranslate(endpoint);
                    return Task.FromResult($@"{'{'} ""translations"": [ {'{'} ""text"": ""{res}"" {'}'} ] {'}'}");
                }
                return Task.FromResult(@"{ ""translations"": [ { ""text"": ""DeepL Default"" } ] }");
            }
        }

        private class TestableCustomApiProvider : CustomApiTranslationProvider
        {
            public Func<string, string>? OnTranslate { get; set; }

            public TestableCustomApiProvider() : base("http://localhost:1234/v1/chat/completions", "test-key") { }

            public override Task<string> SendHttpRequestAsync(string endpoint, string jsonPayload, string apiKey)
            {
                if (OnTranslate != null)
                {
                    string res = OnTranslate(jsonPayload);
                    return Task.FromResult($@"{'{'} ""choices"": [ {'{'} ""message"": {'{'} ""content"": ""{res}"" {'}'} {'}'} ] {'}'}");
                }
                return Task.FromResult(@"{ ""choices"": [ { ""message"": { ""content"": ""CustomAPI Default"" } } ] }");
            }
        }

        private class TestableOllamaProvider : OllamaTranslationProvider
        {
            public Func<string, string>? OnTranslate { get; set; }

            public TestableOllamaProvider() : base("http://localhost:11434") { }

            public override Task<string> SendHttpRequestAsync(string endpoint, string jsonPayload)
            {
                if (OnTranslate != null)
                {
                    string res = OnTranslate(jsonPayload);
                    return Task.FromResult($@"{'{'} ""response"": ""{res}"" {'}'}");
                }
                return Task.FromResult(@"{ ""response"": ""Ollama Default"" }");
            }
        }

        private class MockFallbackProvider : ITranslationProvider
        {
            public Task<string> TranslateAsync(string text) => Task.FromResult($"[WebFallback: {text}]");
            public Task<string> TranslateAsync(string text, TranslationContext? context) => Task.FromResult($"[WebFallback: {text}]");
        }

        [TestMethod]
        public async Task TranslationRouter_OfflineMode_RoutesExclusivelyToLocalAi()
        {
            var keyPool = new ApiKeyPool();
            keyPool.AddOrUpdateCredential(new ApiCredential { Id = "k1", Provider = "Gemini", Secret = "sec1" });

            bool geminiCalled = false;
            var gemini = new TestableGeminiProvider
            {
                OnTranslate = t => { geminiCalled = true; return "Gemini"; }
            };

            var localAi = new TestableOllamaProvider
            {
                OnTranslate = t => "Local AI Result"
            };

            var fallback = new MockFallbackProvider();
            var router = new TranslationRouter(keyPool, gemini, fallback, localAiProvider: localAi)
            {
                OfflineMode = true
            };

            var result = await router.TranslateAsync("Hello");

            Assert.AreEqual("Local AI Result", result);
            Assert.IsFalse(geminiCalled, "Trong OfflineMode, Router không được gọi bất kỳ API cloud nào");
            Assert.AreEqual("LocalAI", router.CurrentProviderName);
        }

        [TestMethod]
        public async Task TranslationRouter_FallsBackToDeepL_WhenGeminiFails()
        {
            var keyPool = new ApiKeyPool();
            keyPool.AddOrUpdateCredential(new ApiCredential { Id = "k1", Provider = "Gemini", Secret = "sec1" });

            var gemini = new TestableGeminiProvider
            {
                OnTranslate = t => throw new GeminiApiException(500, "Gemini Internal Error")
            };

            var deepL = new TestableDeepLProvider("valid-deepl-key")
            {
                OnTranslate = t => "DeepL Translation"
            };

            var fallback = new MockFallbackProvider();
            var router = new TranslationRouter(keyPool, gemini, fallback, deepLProvider: deepL);

            var result = await router.TranslateAsync("Hello");

            Assert.AreEqual("DeepL Translation", result);
            Assert.AreEqual("DeepL", router.CurrentProviderName);
        }

        [TestMethod]
        public async Task TranslationRouter_FallsBackToCustomApi_WhenGeminiAndDeepLFail()
        {
            var keyPool = new ApiKeyPool();
            keyPool.AddOrUpdateCredential(new ApiCredential { Id = "k1", Provider = "Gemini", Secret = "sec1" });

            var gemini = new TestableGeminiProvider
            {
                OnTranslate = t => throw new GeminiApiException(429, "Rate Limit")
            };

            var deepL = new TestableDeepLProvider("valid-deepl-key")
            {
                OnTranslate = t => throw new HttpRequestException("DeepL Service Unavailable")
            };

            var customApi = new TestableCustomApiProvider
            {
                OnTranslate = t => "Custom API Translation"
            };

            var fallback = new MockFallbackProvider();
            var router = new TranslationRouter(keyPool, gemini, fallback, deepLProvider: deepL, customApiProvider: customApi);

            var result = await router.TranslateAsync("Hello");

            Assert.AreEqual("Custom API Translation", result);
            Assert.AreEqual("CustomAPI", router.CurrentProviderName);
        }

        [TestMethod]
        public async Task TranslationRouter_FallsBackToLocalAi_WhenAllCloudFail()
        {
            var keyPool = new ApiKeyPool(); // Empty pool -> Gemini unavailable

            var gemini = new TestableGeminiProvider();
            var deepL = new TestableDeepLProvider("valid-deepl-key")
            {
                OnTranslate = t => throw new HttpRequestException("Network down")
            };

            var localAi = new TestableOllamaProvider
            {
                OnTranslate = t => "Ollama Local Translation"
            };

            var fallback = new MockFallbackProvider();
            var router = new TranslationRouter(keyPool, gemini, fallback, deepLProvider: deepL, localAiProvider: localAi);

            var result = await router.TranslateAsync("Hello");

            Assert.AreEqual("Ollama Local Translation", result);
            Assert.AreEqual("LocalAI", router.CurrentProviderName);
        }

        [TestMethod]
        public async Task TranslationRouter_FallsBackToGoogleWeb_WhenEverythingElseFails()
        {
            var keyPool = new ApiKeyPool(); // No keys
            var gemini = new TestableGeminiProvider();
            var fallback = new MockFallbackProvider();

            var router = new TranslationRouter(keyPool, gemini, fallback);

            var result = await router.TranslateAsync("Test Text");

            Assert.AreEqual("[WebFallback: Test Text]", result);
            Assert.AreEqual("GoogleWebTranslate", router.CurrentProviderName);
        }
    }
}
