using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TranslateBot.Evaluation;
using TranslateBot.OCR;
using TranslateBot.Translation;

namespace TBCO.Eval
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("==================================================");
            Console.WriteLine("TBCO Evaluation & OCR Benchmark CLI Runner");
            Console.WriteLine("==================================================");

            string mode = "all";
            string outputDir = "";

            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--mode" || args[i] == "-m") && i + 1 < args.Length)
                {
                    mode = args[++i].ToLowerInvariant();
                }
                else if ((args[i] == "--output" || args[i] == "-o") && i + 1 < args.Length)
                {
                    outputDir = args[++i];
                }
            }

            string projectRoot = FindProjectRoot();
            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.Combine(projectRoot, "evaluation", "reports");
            }
            Directory.CreateDirectory(outputDir);

            string gitSha = GetGitSha(projectRoot);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string osDescription = RuntimeInformation.OSDescription;
            string framework = RuntimeInformation.FrameworkDescription;

            Console.WriteLine($"Git Commit : {gitSha}");
            Console.WriteLine($"OS         : {osDescription}");
            Console.WriteLine($"Runtime    : {framework}");
            Console.WriteLine($"Mode       : {mode}");
            Console.WriteLine($"Output Dir : {outputDir}");
            Console.WriteLine("--------------------------------------------------");

            try
            {
                if (mode == "ocr" || mode == "all")
                {
                    await RunOcrBenchmarkAsync(projectRoot, outputDir, gitSha, timestamp);
                }

                if (mode == "harness" || mode == "all")
                {
                    await RunTranslationHarnessAsync(projectRoot, outputDir, gitSha, timestamp);
                }

                Console.WriteLine("\n[SUCCESS] Hoàn tất toàn bộ quy trình đánh giá!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR] Thất bại trong quá trình đánh giá: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                return 1;
            }
        }

        private static async Task RunOcrBenchmarkAsync(string root, string outputDir, string gitSha, string timestamp)
        {
            Console.WriteLine("\n>>> Bắt đầu chạy OCR Benchmark...");
            string samplePath = Path.Combine(root, "evaluation", "ocr", "samples", "ocr_benchmark_samples.json");
            if (!File.Exists(samplePath))
            {
                Console.WriteLine($"[WARN] Không tìm thấy file mẫu: {samplePath}");
                return;
            }

            string json = await File.ReadAllTextAsync(samplePath);
            var samples = JsonConvert.DeserializeObject<List<OcrBenchmarkSample>>(json) ?? new();
            Console.WriteLine($"Đã nạp {samples.Count} mẫu benchmark OCR.");

            var summaries = new Dictionary<string, OcrBenchmarkSummary>();

            // 1. Thử Windows OCR nếu môi trường hỗ trợ
            try
            {
                var winOcr = new WindowsOcrEngine("en-US");
                Console.WriteLine("Đang benchmark Windows OCR...");
                var summary = await OcrBenchmarkRunner.RunEngineBenchmarkAsync(winOcr, "WindowsOCR", samples, applyPreprocessing: true);
                summaries["WindowsOCR"] = summary;
                Console.WriteLine($"Windows OCR: CER={summary.AverageCer:F2}%, AvgLatency={summary.AverageLatencyMs:F1}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] Bỏ qua Windows OCR: {ex.Message}");
            }

            // 2. Chạy Mock Baseline để đảm bảo luôn có kết quả so sánh
            var mockEngine = new MockOcrEngine();
            Console.WriteLine("Đang benchmark Mock Baseline Engine...");
            var mockSummary = await OcrBenchmarkRunner.RunEngineBenchmarkAsync(mockEngine, "MockBaseline", samples, applyPreprocessing: false);
            summaries["MockBaseline"] = mockSummary;

            // Xuất báo cáo Markdown
            string reportFile = Path.Combine(outputDir, $"ocr-benchmark-{timestamp}.md");
            var sb = new StringBuilder();
            sb.AppendLine($"# Báo cáo Benchmark OCR TBCO ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            sb.AppendLine();
            sb.AppendLine($"- **Git Commit SHA:** `{gitSha}`");
            sb.AppendLine($"- **Hệ điều hành:** {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
            sb.AppendLine($"- **.NET Runtime:** {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"- **Số lượng mẫu:** {samples.Count}");
            sb.AppendLine();
            sb.AppendLine("> [!WARNING]");
            sb.AppendLine("> Các mẫu thử nghiệm được tạo bằng cơ chế kết xuất đồ họa GDI+ tổng hợp (Synthetic GDI+ Rendering).");
            sb.AppendLine("> Kết quả dùng để kiểm tra tính toàn vẹn (regression/smoke) của pipeline OCR.");
            sb.AppendLine();
            sb.AppendLine(OcrBenchmarkRunner.GenerateComparisonMarkdown(summaries));

            await File.WriteAllTextAsync(reportFile, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"Đã xuất báo cáo OCR: {reportFile}");
        }

        private static async Task RunTranslationHarnessAsync(string root, string outputDir, string gitSha, string timestamp)
        {
            Console.WriteLine("\n>>> Bắt đầu chạy Translation Quality Harness...");
            string samplePath = Path.Combine(root, "evaluation", "translation", "samples", "evaluation_samples.json");
            if (!File.Exists(samplePath))
            {
                Console.WriteLine($"[WARN] Không tìm thấy file mẫu: {samplePath}");
                return;
            }

            string json = await File.ReadAllTextAsync(samplePath);
            var samples = JsonConvert.DeserializeObject<List<EvaluationSample>>(json) ?? new();
            Console.WriteLine($"Đã nạp {samples.Count} mẫu kiểm tra ngữ nghĩa bản dịch.");

            var mockDict = samples.ToDictionary(s => s.SourceCleaned, s => s.ReferenceTranslations.FirstOrDefault() ?? s.SourceCleaned);
            var mockProvider = new MockCliProvider(mockDict);

            var options = new EvaluationRunOptions
            {
                RunName = $"harness_{timestamp}",
                ProviderName = "BaselineReferenceProvider",
                ModelName = "baseline-reference",
                OutputDirectory = outputDir
            };

            var report = await EvaluationRunner.RunAsync(samplePath, mockProvider, options);

            Console.WriteLine($"Translation Harness: PassRate={report.PassRate:F1}%, AvgScore={report.AverageScore:F2}/5.0, LockedTermsPassRate={report.LockedTermsPassRate:F1}%");
            Console.WriteLine($"Đã xuất báo cáo Harness tại: {Path.Combine(outputDir, $"{options.RunName}_report.md")}");
        }

        private static string FindProjectRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "TranslateBot.sln")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir) ?? "";
            }
            return Directory.GetCurrentDirectory();
        }

        private static string GetGitSha(string root)
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse HEAD")
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    if (!string.IsNullOrEmpty(output)) return output;
                }
            }
            catch { }
            return "UNKNOWN";
        }

        private class MockOcrEngine : IOcrEngine
        {
            public Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height)
            {
                return Task.FromResult(new OcrResult
                {
                    Text = "Mock Ground Truth Dialogue",
                    EngineName = "MockBaseline",
                    Success = true,
                    LatencyMs = 12.0
                });
            }
        }

        private class MockCliProvider : ITranslationProvider
        {
            private readonly Dictionary<string, string> _dict;
            public MockCliProvider(Dictionary<string, string> dict) => _dict = dict;
            public string Name => "BaselineReferenceProvider";
            public bool IsAvailable => true;

            public Task<string> TranslateAsync(string text)
            {
                if (_dict.TryGetValue(text, out var res)) return Task.FromResult(res);
                return Task.FromResult(text);
            }

            public Task<string> TranslateAsync(string text, TranslationContext? context)
            {
                if (_dict.TryGetValue(text, out var res)) return Task.FromResult(res);
                return Task.FromResult(text);
            }

            public Task<string> TranslateAsync(string text, string targetLanguage, string sourceLanguage = "auto")
            {
                if (_dict.TryGetValue(text, out var res)) return Task.FromResult(res);
                return Task.FromResult(text);
            }
        }
    }
}
