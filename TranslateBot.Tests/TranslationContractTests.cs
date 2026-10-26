using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Capture;
using TranslateBot.Dialogue;
using TranslateBot.Infrastructure;
using TranslateBot.OCR;
using TranslateBot.Translation;

namespace TranslateBot.Tests
{
    [TestClass]
    public class TranslationContractTests
    {
        [TestMethod]
        public void TranslationRequest_CreationAndSnapshot_MaintainsProperties()
        {
            var ctx = new TranslationContext
            {
                GameName = "Fate/Grand Order",
                SpeakerName = "Mash Kyrielight"
            };
            using var cts = new CancellationTokenSource();

            var req = new TranslationRequest(
                "先輩、敵の反応が急激に上昇しています！",
                ctx,
                sourceLanguage: "ja",
                targetLanguage: "vi",
                sequenceId: 42,
                cancellationToken: cts.Token);

            Assert.AreEqual("先輩、敵の反応が急激に上昇しています！", req.Text);
            Assert.AreEqual("ja", req.SourceLanguage);
            Assert.AreEqual("vi", req.TargetLanguage);
            Assert.AreEqual(42, req.SequenceId);
            Assert.IsNotNull(req.Context);
            Assert.AreEqual("Mash Kyrielight", req.Context.SpeakerName);
            Assert.AreEqual(cts.Token, req.CancellationToken);
        }

        [TestMethod]
        public void TranslationResult_CreateSuccess_HasCorrectMetadata()
        {
            var res = TranslationResult.CreateSuccess(
                "Senpai, phản ứng của kẻ địch đang tăng vọt!",
                "Gemini",
                "gemini-3.5-flash",
                latency: 142.5,
                isCacheHit: false,
                keyId: "gemini-key-1");

            Assert.IsTrue(res.IsSuccess);
            Assert.AreEqual("Senpai, phản ứng của kẻ địch đang tăng vọt!", res.Text);
            Assert.AreEqual("Gemini", res.ProviderName);
            Assert.AreEqual("gemini-3.5-flash", res.ModelName);
            Assert.AreEqual("gemini-key-1", res.KeyIdentifier);
            Assert.AreEqual(142.5, res.LatencyMs);
            Assert.IsFalse(res.IsFallback);
            Assert.AreEqual(TranslationStatus.Success, res.Status);
        }

        [TestMethod]
        public void TranslationResult_CreateFallback_CapturesReasonAndStatus()
        {
            var res = TranslationResult.CreateFallback(
                "Đường truyền từ Chaldea đây.",
                "Gemini",
                "GoogleWebTranslate",
                "Quota 429",
                latency: 350.0);

            Assert.IsTrue(res.IsSuccess);
            Assert.IsTrue(res.IsFallback);
            Assert.AreEqual("GoogleWebTranslate", res.ProviderName);
            Assert.AreEqual(TranslationStatus.Fallback, res.Status);
            Assert.IsTrue(res.FallbackReason?.Contains("Gemini -> GoogleWebTranslate") == true);
            Assert.AreEqual(350.0, res.LatencyMs);
        }

        [TestMethod]
        public async Task TranslationRouter_TranslateRequestAsync_ReturnsTypedResultWithCorrectProvider()
        {
            var keyPool = new ApiKeyPool();
            var fakeGemini = new FakeGeminiWithKeyProvider();
            var fallback = new GoogleWebTranslateProvider();

            var router = new TranslationRouter(keyPool, fakeGemini, fallback)
            {
                PrimaryProvider = "Gemini"
            };

            // Trường hợp 1: Có key Gemini khả dụng
            keyPool.AddOrUpdateCredential(new ApiCredential
            {
                Id = "test-gem-key",
                Provider = "Gemini",
                Secret = "AIzaSyFakeKey123",
                Status = ApiKeyStatus.Active
            });

            var req = new TranslationRequest("こんにちは", sourceLanguage: "ja", targetLanguage: "vi");
            var result = await router.TranslateRequestAsync(req);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("Gemini", result.ProviderName);
            Assert.AreEqual("test-gem-key", result.KeyIdentifier);
            Assert.IsFalse(result.IsFallback);
        }

        [TestMethod]
        public async Task TranslationRouter_TranslateRequestAsync_Fallback_ReturnsFallbackMetadata()
        {
            var keyPool = new ApiKeyPool(); // Empty pool -> Gemini unavailable
            var fakeGemini = new FakeGeminiWithKeyProvider();
            var mockFallback = new MockFallbackProvider("Bản dịch dự phòng");

            var router = new TranslationRouter(keyPool, fakeGemini, mockFallback)
            {
                PrimaryProvider = "Gemini"
            };

            var req = new TranslationRequest("Hello world", sourceLanguage: "en", targetLanguage: "vi");
            var result = await router.TranslateRequestAsync(req);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.IsFallback);
            Assert.AreEqual("GoogleWebTranslate", result.ProviderName);
            Assert.IsTrue(result.FallbackReason?.Contains("Gemini") == true);
        }

        private class FakeGeminiWithKeyProvider : GeminiProvider
        {
            public override Task<string> TranslateWithKeyAsync(string text, string apiKey, TranslationContext? context = null)
            {
                return Task.FromResult($"[Gemini: {text}]");
            }
        }

        private class MockFallbackProvider : GoogleWebTranslateProvider
        {
            private readonly string _mockText;
            public MockFallbackProvider(string mockText) => _mockText = mockText;

            public override Task<string> TranslateAsync(string text, TranslationContext? context)
            {
                return Task.FromResult(_mockText);
            }
        }
    }
}
