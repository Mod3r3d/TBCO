using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TranslateBot.Infrastructure;
using TranslateBot.Translation;

namespace TranslateBot.Evaluation
{
    public class EvaluationRunOptions
    {
        public string RunName { get; set; } = "baseline";
        public string ModelName { get; set; } = "";
        public string ProviderName { get; set; } = "default";
        public bool DisableCache { get; set; } = true;
        public string OutputDirectory { get; set; } = "";
    }

    public class EvaluationRunReport
    {
        public string RunName { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ModelName { get; set; } = "";
        public string ProviderName { get; set; } = "";
        public int TotalSamples { get; set; }
        public int PassedCount { get; set; }
        public int FailedCount { get; set; }
        public double PassRate { get; set; }
        public double AverageScore { get; set; }
        public double AverageLatencyMs { get; set; }
        public double LockedTermsPassRate { get; set; }
        public int ForbiddenTermsViolations { get; set; }
        public double PronounConsistencyRate { get; set; }

        public List<SampleScoreResult> Results { get; set; } = new();

        public string ToMarkdownReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Báo cáo Đánh giá Chất lượng Dịch thuật — TBCO Quality Harness");
            sb.AppendLine();
            sb.AppendLine($"| Thông số | Giá trị |");
            sb.AppendLine($"|---|---|");
            sb.AppendLine($"| Tên đợt chạy | **{RunName}** |");
            sb.AppendLine($"| Thời gian | {Timestamp:yyyy-MM-dd HH:mm:ss} UTC |");
            sb.AppendLine($"| Provider / Model | {ProviderName} / {ModelName} |");
            sb.AppendLine($"| Tổng số mẫu | **{TotalSamples}** |");
            sb.AppendLine($"| Tỉ lệ Đạt (Pass Rate) | **{PassRate:F1}%** ({PassedCount}/{TotalSamples}) |");
            sb.AppendLine($"| Điểm chất lượng trung bình | **{AverageScore:F2} / 5.0** |");
            sb.AppendLine($"| Độ trễ trung bình | {AverageLatencyMs:F0} ms |");
            sb.AppendLine($"| Tỉ lệ giữ đúng Locked Terms | **{LockedTermsPassRate:F1}%** |");
            sb.AppendLine($"| Vi phạm từ cấm kỵ (Forbidden) | **{ForbiddenTermsViolations}** lần |");
            sb.AppendLine($"| Độ nhất quán đại từ xưng hô | **{PronounConsistencyRate:F1}%** |");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## Chi tiết từng mẫu kiểm thử");
            sb.AppendLine();
            sb.AppendLine("| Mẫu | Điểm | Kết quả | Bản dịch xuất ra | Lỗi / Ghi chú |");
            sb.AppendLine("|---|:---:|:---:|---|---|");

            foreach (var r in Results)
            {
                string status = r.IsPass ? "✅ PASS" : "❌ FAIL";
                string cleanOut = r.OutputText.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");
                string reasons = string.Join("; ", r.FailureReasons.Concat(r.Warnings)).Replace("|", "\\|");
                if (string.IsNullOrEmpty(reasons)) reasons = "OK";
                sb.AppendLine($"| `{r.SampleId}` | {r.OverallScore:F1} | {status} | {cleanOut} | {reasons} |");
            }

            return sb.ToString();
        }
    }

    public class EvaluationRunner
    {
        public static async Task<EvaluationRunReport> RunAsync(
            string datasetPath,
            ITranslationProvider provider,
            EvaluationRunOptions? options = null)
        {
            options ??= new EvaluationRunOptions();

            if (!File.Exists(datasetPath))
            {
                throw new FileNotFoundException($"Không tìm thấy file dataset đánh giá: {datasetPath}");
            }

            string json = await File.ReadAllTextAsync(datasetPath);
            var samples = JsonConvert.DeserializeObject<List<EvaluationSample>>(json) ?? new List<EvaluationSample>();

            var report = new EvaluationRunReport
            {
                RunName = options.RunName,
                Timestamp = DateTime.UtcNow,
                ModelName = options.ModelName,
                ProviderName = options.ProviderName,
                TotalSamples = samples.Count
            };

            double totalLatency = 0.0;
            double totalScore = 0.0;
            int lockedPassCount = 0;
            int totalWithLocked = 0;
            int pronounPassCount = 0;
            int totalWithPronouns = 0;

            foreach (var sample in samples)
            {
                var context = sample.ToTranslationContext();
                string outputText = "";
                double latency = 0.0;

                var sw = Stopwatch.StartNew();
                try
                {
                    outputText = await provider.TranslateAsync(sample.SourceCleaned, context);
                }
                catch (Exception ex)
                {
                    outputText = $"Exception: {ex.Message}";
                }
                sw.Stop();
                latency = sw.Elapsed.TotalMilliseconds;
                totalLatency += latency;

                var scoreResult = EvaluationScorer.Score(
                    sample, 
                    outputText, 
                    latencyMs: latency, 
                    isCacheHit: false, 
                    provider: options.ProviderName);

                report.Results.Add(scoreResult);
                totalScore += scoreResult.OverallScore;

                if (scoreResult.IsPass) report.PassedCount++;
                else report.FailedCount++;

                if (sample.LockedTerms.Count > 0)
                {
                    totalWithLocked++;
                    if (scoreResult.LockedTermsPass) lockedPassCount++;
                }

                if (!scoreResult.ForbiddenTermsPass)
                {
                    report.ForbiddenTermsViolations++;
                }

                if (sample.ExpectedPronounPairs.Count > 0)
                {
                    totalWithPronouns++;
                    if (scoreResult.PronounConsistencyPass) pronounPassCount++;
                }
            }

            report.PassRate = report.TotalSamples > 0 
                ? (report.PassedCount * 100.0 / report.TotalSamples) 
                : 0.0;

            report.AverageScore = report.TotalSamples > 0 
                ? (totalScore / report.TotalSamples) 
                : 0.0;

            report.AverageLatencyMs = report.TotalSamples > 0 
                ? (totalLatency / report.TotalSamples) 
                : 0.0;

            report.LockedTermsPassRate = totalWithLocked > 0 
                ? (lockedPassCount * 100.0 / totalWithLocked) 
                : 100.0;

            report.PronounConsistencyRate = totalWithPronouns > 0 
                ? (pronounPassCount * 100.0 / totalWithPronouns) 
                : 100.0;

            // Xuất báo cáo ra file nếu có chỉ định thư mục output
            if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                Directory.CreateDirectory(options.OutputDirectory);
                string mdPath = Path.Combine(options.OutputDirectory, $"{options.RunName}_report.md");
                string jsonlPath = Path.Combine(options.OutputDirectory, $"{options.RunName}_results.jsonl");

                await File.WriteAllTextAsync(mdPath, report.ToMarkdownReport());

                var lines = report.Results.Select(r => JsonConvert.SerializeObject(r));
                await File.WriteAllLinesAsync(jsonlPath, lines);
            }

            return report;
        }
    }
}
