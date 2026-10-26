using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using TranslateBot.Evaluation;
using TranslateBot.OCR;

namespace TranslateBot.Tests
{
    [TestClass]
    public class OcrBenchmarkTests
    {
        private class MockOcrEngine : IOcrEngine
        {
            private readonly Func<byte[], int, int, OcrResult> _recognizeFunc;

            public MockOcrEngine(Func<byte[], int, int, OcrResult> recognizeFunc)
            {
                _recognizeFunc = recognizeFunc;
            }

            public Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height)
            {
                return Task.FromResult(_recognizeFunc(bgraPixels, width, height));
            }
        }

        [TestMethod]
        public void OcrEvaluator_CalculateCer_CalculatesAccurately()
        {
            // Exact match
            Assert.AreEqual(0.0, OcrEvaluator.CalculateCer("Artoria", "Artoria"), 0.001);

            // 1 ký tự sai trên 7 ký tự -> 1/7 = 0.1428
            double cer = OcrEvaluator.CalculateCer("Artoria", "Artoriax");
            Assert.AreEqual(1.0 / 7.0, cer, 0.01);

            // Hyp rỗng
            Assert.AreEqual(1.0, OcrEvaluator.CalculateCer("Hello", ""), 0.001);

            // Cả 2 rỗng
            Assert.AreEqual(0.0, OcrEvaluator.CalculateCer("", ""), 0.001);
        }

        [TestMethod]
        public void OcrEvaluator_CalculateWer_CalculatesAccurately()
        {
            string gt = "Fate Grand Order Chaldea";
            string hyp = "Fate Grand Order Camelot";

            // 1 từ sai trên 4 từ -> 1/4 = 0.25
            double wer = OcrEvaluator.CalculateWer(gt, hyp);
            Assert.AreEqual(0.25, wer, 0.01);

            Assert.AreEqual(0.0, OcrEvaluator.CalculateWer(gt, gt), 0.001);
        }

        [TestMethod]
        public void OcrEvaluator_ProperNouns_ValidatesPresence()
        {
            var sample = new OcrBenchmarkSample
            {
                SampleId = "test-01",
                GroundTruthText = "Mash and Gilgamesh are standing near Chaldea.",
                ProperNouns = new List<string> { "Mash", "Gilgamesh", "Chaldea" }
            };

            // Kết quả đầy đủ tên riêng
            var passResult = OcrEvaluator.Evaluate(sample, "Mash and Gilgamesh are standing near Chaldea.", "TestEngine");
            Assert.IsTrue(passResult.ProperNounsPass);
            Assert.AreEqual(0, passResult.MissingProperNouns.Count);

            // Kết quả thiếu "Gilgamesh" (bị nhận diện sai thành "Gil")
            var failResult = OcrEvaluator.Evaluate(sample, "Mash and Gil are standing near Chaldea.", "TestEngine");
            Assert.IsFalse(failResult.ProperNounsPass);
            Assert.IsTrue(failResult.MissingProperNouns.Contains("Gilgamesh"));
        }

        private static string GetOcrDatasetPath()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string probe = Path.Combine(basePath, "..", "..", "..", "..", "evaluation", "ocr", "samples", "ocr_benchmark_samples.json");
            string fullPath = Path.GetFullPath(probe);

            if (!File.Exists(fullPath))
            {
                probe = Path.Combine(Directory.GetCurrentDirectory(), "evaluation", "ocr", "samples", "ocr_benchmark_samples.json");
                fullPath = Path.GetFullPath(probe);
            }

            return fullPath;
        }

        [TestMethod]
        public void OcrBenchmarkDataset_Integrity_AllSamplesValid()
        {
            string jsonPath = GetOcrDatasetPath();
            Assert.IsTrue(File.Exists(jsonPath), $"Tệp dataset mẫu OCR phải tồn tại tại: {jsonPath}");

            string json = File.ReadAllText(jsonPath);
            var samples = JsonConvert.DeserializeObject<List<OcrBenchmarkSample>>(json);

            Assert.IsNotNull(samples);
            Assert.IsTrue(samples.Count >= 5, "Dataset phải có ít nhất 5 mẫu");

            foreach (var s in samples)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.SampleId));
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.ProfileId));
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.GroundTruthText));
                Assert.IsTrue(s.Width > 0 && s.Height > 0);

                // Kiểm tra hàm tạo pixel nhân tạo hoạt động không lỗi
                byte[] pixels = s.GenerateBgraPixels();
                Assert.IsNotNull(pixels);
                Assert.AreEqual(s.Width * s.Height * 4, pixels.Length);
            }
        }

        [TestMethod]
        public async Task OcrBenchmarkRunner_ExecutesAndGeneratesReport()
        {
            var samples = new List<OcrBenchmarkSample>
            {
                new OcrBenchmarkSample
                {
                    SampleId = "fgo-sample-01",
                    ProfileId = "fgo-jp",
                    GroundTruthText = "先輩、レイシフトの準備が完了しました。",
                    ProperNouns = new List<string> { "先輩" },
                    Width = 300,
                    Height = 80
                },
                new OcrBenchmarkSample
                {
                    SampleId = "vn-sample-01",
                    ProfileId = "vn-generic",
                    GroundTruthText = "The journey has just begun.",
                    ProperNouns = new List<string> { "journey" },
                    Width = 300,
                    Height = 80
                }
            };

            var mockWindowsOcr = new MockOcrEngine((pixels, w, h) => new OcrResult
            {
                Text = "先輩、レイシフトの準備が完了しました。",
                RawText = "先輩、レイシフトの準備が完了しました。",
                EngineName = "WindowsOCR",
                Success = true,
                LatencyMs = 25
            });

            var mockOneOcr = new MockOcrEngine((pixels, w, h) => new OcrResult
            {
                Text = "先輩、レイシフトの準備が完了しました。",
                RawText = "先輩、レイシフトの準備が完了しました。",
                EngineName = "OneOCR",
                Success = true,
                LatencyMs = 15
            });

            var summaryWin = await OcrBenchmarkRunner.RunEngineBenchmarkAsync(mockWindowsOcr, "WindowsOCR", samples, applyPreprocessing: false);
            var summaryOne = await OcrBenchmarkRunner.RunEngineBenchmarkAsync(mockOneOcr, "OneOCR", samples, applyPreprocessing: false);

            Assert.AreEqual(2, summaryWin.TotalSamples);
            Assert.AreEqual(2, summaryOne.TotalSamples);
            Assert.IsTrue(summaryWin.AverageLatencyMs > 0);
            Assert.IsTrue(summaryOne.AverageLatencyMs > 0);

            var summaries = new Dictionary<string, OcrBenchmarkSummary>
            {
                ["WindowsOCR"] = summaryWin,
                ["OneOCR"] = summaryOne
            };

            string md = OcrBenchmarkRunner.GenerateComparisonMarkdown(summaries);
            StringAssert.Contains(md, "Báo cáo Benchmark OCR");
            StringAssert.Contains(md, "WindowsOCR");
            StringAssert.Contains(md, "OneOCR");
            StringAssert.Contains(md, "fgo-jp");
            StringAssert.Contains(md, "vn-generic");

            // Lưu report thử nghiệm
            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_ocr_reports");
            OcrBenchmarkRunner.SaveReport(outDir, md, summaries);
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "ocr-benchmark-report.md")));
        }
    }
}
