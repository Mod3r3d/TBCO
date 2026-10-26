using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using TranslateBot.Evaluation;
using TranslateBot.Translation;

namespace TranslateBot.Tests
{
    [TestClass]
    public class QualityHarnessTests
    {
        private static string GetDatasetPath()
        {
            // Tìm file dataset evaluation_samples.json
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string probe = Path.Combine(basePath, "..", "..", "..", "..", "evaluation", "translation", "samples", "evaluation_samples.json");
            string fullPath = Path.GetFullPath(probe);

            if (!File.Exists(fullPath))
            {
                // Fallback nếu chạy ở thư mục khác
                probe = Path.Combine(Directory.GetCurrentDirectory(), "evaluation", "translation", "samples", "evaluation_samples.json");
                fullPath = Path.GetFullPath(probe);
            }

            return fullPath;
        }

        [TestMethod]
        public void Dataset_Integrity_AllSamplesHaveRequiredFields()
        {
            string path = GetDatasetPath();
            Assert.IsTrue(File.Exists(path), $"File dataset không tồn tại: {path}");

            string json = File.ReadAllText(path);
            var samples = JsonConvert.DeserializeObject<List<EvaluationSample>>(json);

            Assert.IsNotNull(samples);
            Assert.IsTrue(samples.Count >= 20, $"Số lượng mẫu tối thiểu phải >= 20, thực tế: {samples.Count}");

            var ids = new HashSet<string>();
            foreach (var s in samples)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.SampleId), "sampleId không được rỗng");
                Assert.IsTrue(ids.Add(s.SampleId), $"Trùng lặp sampleId: {s.SampleId}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.SourceCleaned), $"SourceCleaned rỗng ở {s.SampleId}");
                Assert.IsTrue(s.ReferenceTranslations.Count > 0, $"Không có referenceTranslations ở {s.SampleId}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.GameId), $"GameId rỗng ở {s.SampleId}");

                // Kiểm tra chuyển đổi sang TranslationContext chuẩn
                var ctx = s.ToTranslationContext();
                Assert.IsNotNull(ctx);
                Assert.AreEqual(s.GameId, ctx.GameName);
            }
        }

        [TestMethod]
        public void EvaluationScorer_LockedTerms_DetectsMissingAndPresent()
        {
            var sample = new EvaluationSample
            {
                SampleId = "test_locked",
                SourceCleaned = "我が剣をマスターに捧げよう。エクスカリバー！",
                LockedTerms = new List<string> { "Master", "Excalibur" },
                ReferenceTranslations = new List<string> { "Ta dâng kiếm cho Master. Excalibur!" }
            };

            // Trường hợp 1: Có đầy đủ locked terms
            var passResult = EvaluationScorer.Score(sample, "Ta dâng thanh kiếm này cho Master. Excalibur!");
            Assert.IsTrue(passResult.LockedTermsPass);
            Assert.IsTrue(passResult.IsPass);

            // Trường hợp 2: Thiếu locked term "Excalibur"
            var failResult = EvaluationScorer.Score(sample, "Ta dâng thanh kiếm này cho Master. Bảo Khí Ánh Sáng!");
            Assert.IsFalse(failResult.LockedTermsPass);
            Assert.IsFalse(failResult.IsPass);
            Assert.IsTrue(failResult.FailureReasons.Any(r => r.Contains("Excalibur")));
        }

        [TestMethod]
        public void EvaluationScorer_ForbiddenTerms_DetectsViolations()
        {
            var sample = new EvaluationSample
            {
                SampleId = "test_forbidden",
                SourceCleaned = "大丈夫か？無理しなくていいんだぞ。",
                ForbiddenTerms = new List<string> { "bạn" },
                ReferenceTranslations = new List<string> { "Cậu có sao không? Không cần phải cố quá đâu." }
            };

            // Dùng 'cậu' -> hợp lệ
            var passResult = EvaluationScorer.Score(sample, "Cậu ổn chứ? Không cần phải gượng ép bản thân đâu.");
            Assert.IsTrue(passResult.ForbiddenTermsPass);

            // Dùng 'bạn' -> vi phạm
            var failResult = EvaluationScorer.Score(sample, "Bạn có sao không? Bạn không cần phải cố đâu.");
            Assert.IsFalse(failResult.ForbiddenTermsPass);
            Assert.IsFalse(failResult.IsPass);
            Assert.IsTrue(failResult.FailureReasons.Any(r => r.Contains("bạn")));
        }

        [TestMethod]
        public void EvaluationScorer_PronounConsistency_ValidatesPairs()
        {
            var sample = new EvaluationSample
            {
                SampleId = "test_pronouns",
                SourceCleaned = "先輩、ここは私にお任せください！",
                ExpectedPronounPairs = new List<List<string>>
                {
                    new List<string> { "em", "senpai" },
                    new List<string> { "em", "Senpai" }
                },
                ReferenceTranslations = new List<string> { "Senpai, chỗ này xin hãy để em lo!" }
            };

            // Khớp cả 'em' và 'Senpai'
            var passResult = EvaluationScorer.Score(sample, "Senpai, nơi này xin hãy giao lại cho em!");
            Assert.IsTrue(passResult.PronounConsistencyPass);

            // Thiếu 'em' hoặc 'Senpai'
            var failResult = EvaluationScorer.Score(sample, "Tiền bối, chỗ này cứ để tôi lo!");
            Assert.IsFalse(failResult.PronounConsistencyPass);
        }

        [TestMethod]
        public void EvaluationScorer_TokenSimilarity_CalculatesAccurately()
        {
            string refA = "Senpai, phản ứng của kẻ địch đang tăng vọt! Xin hãy cẩn thận!";
            string candA = "Senpai, phản ứng kẻ địch đang tăng vọt! Hãy cẩn thận ạ!";

            double sim = EvaluationScorer.CalculateTokenSimilarity(candA, refA);
            Assert.IsTrue(sim > 0.60, $"Độ tương đồng kỳ vọng > 0.60, thực tế: {sim}");

            // Hai câu hoàn toàn khác biệt
            double diffSim = EvaluationScorer.CalculateTokenSimilarity("Hôm nay trời nắng đẹp.", refA);
            Assert.IsTrue(diffSim < 0.20, $"Độ tương đồng câu khác biệt kỳ vọng < 0.20, thực tế: {diffSim}");
        }

        [TestMethod]
        public async Task EvaluationRunner_ReplayHarness_RunsWithMockProvider()
        {
            string path = GetDatasetPath();
            Assert.IsTrue(File.Exists(path));

            string json = await File.ReadAllTextAsync(path);
            var samples = JsonConvert.DeserializeObject<List<EvaluationSample>>(json)!;

            // Mock provider trả về chính reference translation tốt nhất của mỗi mẫu
            var mockDict = samples.ToDictionary(s => s.SourceCleaned, s => s.ReferenceTranslations[0]);
            var mockProvider = new MockEvaluationProvider(mockDict);

            string outDir = Path.Combine(Path.GetTempPath(), "tbco_harness_test_" + Guid.NewGuid().ToString("N"));

            var options = new EvaluationRunOptions
            {
                RunName = "unit_test_harness",
                ProviderName = "MockPerfectProvider",
                ModelName = "mock-model",
                OutputDirectory = outDir
            };

            var report = await EvaluationRunner.RunAsync(path, mockProvider, options);

            Assert.IsNotNull(report);
            Assert.AreEqual(samples.Count, report.TotalSamples);
            Assert.AreEqual(samples.Count, report.PassedCount);
            Assert.AreEqual(0, report.FailedCount);
            Assert.AreEqual(100.0, report.PassRate);
            Assert.IsTrue(report.AverageScore >= 4.5);
            Assert.AreEqual(100.0, report.LockedTermsPassRate);
            Assert.AreEqual(0, report.ForbiddenTermsViolations);

            // Kiểm tra các file báo cáo Markdown và JSONL được xuất
            string mdFile = Path.Combine(outDir, "unit_test_harness_report.md");
            string jsonlFile = Path.Combine(outDir, "unit_test_harness_results.jsonl");

            Assert.IsTrue(File.Exists(mdFile), "Báo cáo Markdown phải được tạo");
            Assert.IsTrue(File.Exists(jsonlFile), "Báo cáo JSONL phải được tạo");

            string mdContent = File.ReadAllText(mdFile);
            Assert.IsTrue(mdContent.Contains("TBCO Quality Harness"));
            Assert.IsTrue(mdContent.Contains("100.0%"));

            // Dọn dẹp thư mục tạm
            try { Directory.Delete(outDir, true); } catch { }
        }

        private class MockEvaluationProvider : ITranslationProvider
        {
            private readonly Dictionary<string, string> _responses;

            public MockEvaluationProvider(Dictionary<string, string> responses)
            {
                _responses = responses;
            }

            public Task<string> TranslateAsync(string text)
            {
                return Task.FromResult(_responses.TryGetValue(text, out var res) ? res : "Dịch tự động");
            }

            public Task<string> TranslateAsync(string text, TranslationContext? context)
            {
                return TranslateAsync(text);
            }
        }
    }
}
