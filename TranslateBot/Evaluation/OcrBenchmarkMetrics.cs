using System;
using System.Collections.Generic;
using System.Linq;

namespace TranslateBot.Evaluation
{
    public class SampleOcrMetric
    {
        public string SampleId { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string GroundTruth { get; set; } = string.Empty;
        public string RecognizedText { get; set; } = string.Empty;
        public string EngineName { get; set; } = string.Empty;

        public double CharacterErrorRate { get; set; }
        public double WordErrorRate { get; set; }
        public bool ProperNounsPass { get; set; } = true;
        public List<string> MissingProperNouns { get; set; } = new();

        public bool IsClipped { get; set; }
        public bool IsEmpty { get; set; }
        public double LatencyMs { get; set; }
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }

    public class ProfileOcrSummary
    {
        public string ProfileId { get; set; } = string.Empty;
        public int TotalSamples { get; set; }
        public double AverageCer { get; set; }
        public double AverageWer { get; set; }
        public double ProperNounExactMatchRate { get; set; }
        public double AverageLatencyMs { get; set; }
        public double FailureRate { get; set; }
    }

    public class OcrBenchmarkSummary
    {
        public string EngineName { get; set; } = string.Empty;
        public int TotalSamples { get; set; }
        public double AverageCer { get; set; }
        public double AverageWer { get; set; }
        public double ProperNounExactMatchRate { get; set; }
        public double EmptyRate { get; set; }
        public double ClippedRate { get; set; }
        public double FailureRate { get; set; }
        public double AverageLatencyMs { get; set; }

        public Dictionary<string, ProfileOcrSummary> ByProfile { get; set; } = new();
        public List<SampleOcrMetric> SampleMetrics { get; set; } = new();
    }

    public static class OcrEvaluator
    {
        public static SampleOcrMetric Evaluate(
            OcrBenchmarkSample sample, 
            string recognizedText, 
            string engineName, 
            double latencyMs = 0, 
            bool isClipped = false, 
            bool success = true, 
            string? errorMessage = null)
        {
            string gt = sample.GroundTruthText ?? string.Empty;
            string rec = recognizedText?.Trim() ?? string.Empty;

            var metric = new SampleOcrMetric
            {
                SampleId = sample.SampleId,
                ProfileId = sample.ProfileId,
                Category = sample.Category,
                GroundTruth = gt,
                RecognizedText = rec,
                EngineName = engineName,
                LatencyMs = latencyMs,
                IsClipped = isClipped,
                Success = success,
                ErrorMessage = errorMessage,
                IsEmpty = string.IsNullOrWhiteSpace(rec)
            };

            metric.CharacterErrorRate = CalculateCer(gt, rec);
            metric.WordErrorRate = CalculateWer(gt, rec);

            // Kiểm tra exact match các danh từ riêng
            if (sample.ProperNouns.Count > 0)
            {
                foreach (var noun in sample.ProperNouns)
                {
                    if (!rec.Contains(noun, StringComparison.OrdinalIgnoreCase))
                    {
                        metric.ProperNounsPass = false;
                        metric.MissingProperNouns.Add(noun);
                    }
                }
            }

            return metric;
        }

        public static OcrBenchmarkSummary Summarize(string engineName, List<SampleOcrMetric> metrics)
        {
            var summary = new OcrBenchmarkSummary
            {
                EngineName = engineName,
                TotalSamples = metrics.Count,
                SampleMetrics = metrics
            };

            if (metrics.Count == 0) return summary;

            summary.AverageCer = metrics.Average(m => m.CharacterErrorRate);
            summary.AverageWer = metrics.Average(m => m.WordErrorRate);
            summary.ProperNounExactMatchRate = (double)metrics.Count(m => m.ProperNounsPass) / metrics.Count * 100.0;
            summary.EmptyRate = (double)metrics.Count(m => m.IsEmpty) / metrics.Count * 100.0;
            summary.ClippedRate = (double)metrics.Count(m => m.IsClipped) / metrics.Count * 100.0;
            summary.FailureRate = (double)metrics.Count(m => !m.Success) / metrics.Count * 100.0;
            summary.AverageLatencyMs = metrics.Average(m => m.LatencyMs);

            var grouped = metrics.GroupBy(m => m.ProfileId);
            foreach (var group in grouped)
            {
                var profileMetrics = group.ToList();
                summary.ByProfile[group.Key] = new ProfileOcrSummary
                {
                    ProfileId = group.Key,
                    TotalSamples = profileMetrics.Count,
                    AverageCer = profileMetrics.Average(m => m.CharacterErrorRate),
                    AverageWer = profileMetrics.Average(m => m.WordErrorRate),
                    ProperNounExactMatchRate = (double)profileMetrics.Count(m => m.ProperNounsPass) / profileMetrics.Count * 100.0,
                    AverageLatencyMs = profileMetrics.Average(m => m.LatencyMs),
                    FailureRate = (double)profileMetrics.Count(m => !m.Success) / profileMetrics.Count * 100.0
                };
            }

            return summary;
        }

        public static double CalculateCer(string groundTruth, string hypothesis)
        {
            if (string.IsNullOrEmpty(groundTruth))
                return string.IsNullOrEmpty(hypothesis) ? 0.0 : 1.0;

            int dist = LevenshteinDistance(groundTruth, hypothesis);
            return (double)dist / Math.Max(1, groundTruth.Length);
        }

        public static double CalculateWer(string groundTruth, string hypothesis)
        {
            var gtWords = (groundTruth ?? "").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var hypWords = (hypothesis ?? "").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            if (gtWords.Length == 0)
                return hypWords.Length == 0 ? 0.0 : 1.0;

            int dist = LevenshteinWordDistance(gtWords, hypWords);
            return (double)dist / Math.Max(1, gtWords.Length);
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        private static int LevenshteinWordDistance(string[] s, string[] t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = string.Equals(s[i - 1], t[j - 1], StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }
    }
}
