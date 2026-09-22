using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Translation;
using TranslateBot.Translation.Providers;

namespace TranslateBot.Tests
{
    [TestClass]
    public class MultiProviderTests
    {
        [TestMethod]
        public void DeepLProvider_DetectsFreePlan_AndSelectsEndpoint()
        {
            var freeProvider = new DeepLProvider("abc-123-xyz:fx");
            Assert.IsTrue(freeProvider.IsFreePlan);
            Assert.AreEqual("https://api-free.deepl.com/v2/translate", freeProvider.EndpointUrl);

            var proProvider = new DeepLProvider("abc-123-pro-key");
            Assert.IsFalse(proProvider.IsFreePlan);
            Assert.AreEqual("https://api.deepl.com/v2/translate", proProvider.EndpointUrl);
        }

        [TestMethod]
        public void DeepLProvider_ParsesResponseJson()
        {
            string sampleJson = @"{
                ""translations"": [
                    {
                        ""detected_source_language"": ""JA"",
                        ""text"": ""Chào tiền bối, hôm nay trời đẹp quá!""
                    }
                ]
            }";

            string parsed = DeepLProvider.ParseDeepLResponse(sampleJson);
            Assert.AreEqual("Chào tiền bối, hôm nay trời đẹp quá!", parsed);
        }

        [TestMethod]
        public void CustomApiTranslationProvider_ParsesOpenAiResponseJson()
        {
            string sampleOpenAiJson = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""choices"": [{
                    ""index"": 0,
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": ""Vâng, thưa Master!""
                    },
                    ""finish_reason"": ""stop""
                }]
            }";

            string parsed = CustomApiTranslationProvider.ParseOpenAiResponse(sampleOpenAiJson);
            Assert.AreEqual("Vâng, thưa Master!", parsed);
        }

        [TestMethod]
        public void OllamaTranslationProvider_ParsesOllamaResponseJson()
        {
            string sampleOllamaJson = @"{
                ""model"": ""qwen2.5:7b"",
                ""created_at"": ""2026-09-22T08:00:00.000Z"",
                ""response"": ""Bảo Khí đã sẵn sàng khai hỏa."",
                ""done"": true
            }";

            string parsed = OllamaTranslationProvider.ParseOllamaResponse(sampleOllamaJson);
            Assert.AreEqual("Bảo Khí đã sẵn sàng khai hỏa.", parsed);
        }

        [TestMethod]
        public void ProviderHealthTracker_DegradesOnFailures_AndRecoversOnSuccess()
        {
            var tracker = new ProviderHealthTracker();
            string provider = "Gemini";

            // Ban đầu: Healthy, HealthScore = 1.0
            var initial = tracker.GetOrCreate(provider);
            Assert.AreEqual(ProviderStatus.Healthy, initial.Status);
            Assert.AreEqual(1.0, initial.HealthScore);

            // Báo lỗi lần 1 -> Trừ 0.25 -> 0.75 -> Degraded
            tracker.ReportFailure(provider, "Timeout");
            Assert.AreEqual(1, initial.FailureCount);
            Assert.AreEqual(1, initial.ConsecutiveFailures);
            Assert.AreEqual(0.75, initial.HealthScore, 0.01);
            Assert.AreEqual(ProviderStatus.Degraded, initial.Status);

            // Báo lỗi lần 2 -> 0.50 -> Degraded
            tracker.ReportFailure(provider, "500 Error");
            Assert.AreEqual(2, initial.ConsecutiveFailures);

            // Báo lỗi lần 3 -> 0.25 -> Unhealthy (do >= 3 lần lỗi liên tiếp)
            tracker.ReportFailure(provider, "429 RateLimit");
            Assert.AreEqual(3, initial.ConsecutiveFailures);
            Assert.AreEqual(ProviderStatus.Unhealthy, initial.Status);

            // Phục hồi: Báo thành công -> Hồi điểm và reset ConsecutiveFailures
            tracker.ReportSuccess(provider, TimeSpan.FromMilliseconds(200));
            Assert.AreEqual(0, initial.ConsecutiveFailures);
            Assert.AreEqual(1, initial.SuccessCount);
            Assert.IsTrue(initial.HealthScore > 0.25);
        }
    }
}
