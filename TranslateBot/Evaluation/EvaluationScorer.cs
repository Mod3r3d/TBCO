using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TranslateBot.Evaluation
{
    public class SampleScoreResult
    {
        public string SampleId { get; set; } = "";
        public string SourceText { get; set; } = "";
        public string OutputText { get; set; } = "";
        public string Provider { get; set; } = "";
        public double LatencyMs { get; set; }
        public bool IsCacheHit { get; set; }

        public bool IsPass { get; set; }
        public bool LockedTermsPass { get; set; } = true;
        public bool ForbiddenTermsPass { get; set; } = true;
        public bool PronounConsistencyPass { get; set; } = true;
        public bool NotEmptyOrErrorPass { get; set; } = true;

        public double SimilarityToBestReference { get; set; }
        public string BestMatchingReference { get; set; } = "";
        public double OverallScore { get; set; } // Thang điểm 1.0 đến 5.0

        public List<string> FailureReasons { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public static class EvaluationScorer
    {
        private static readonly string[] GenericErrorKeywords = new[]
        {
            "Chưa thiết lập",
            "HTTP 4",
            "HTTP 5",
            "Quota Exceeded",
            "Exception:",
            "Error:",
            "Safety Block",
            "Fallback:"
        };

        public static SampleScoreResult Score(
            EvaluationSample sample, 
            string translation, 
            double latencyMs = 0, 
            bool isCacheHit = false, 
            string provider = "")
        {
            var result = new SampleScoreResult
            {
                SampleId = sample.SampleId,
                SourceText = sample.SourceCleaned,
                OutputText = translation?.Trim() ?? "",
                Provider = provider,
                LatencyMs = latencyMs,
                IsCacheHit = isCacheHit
            };

            // 1. Kiểm tra đầu ra không rỗng và không chứa chuỗi lỗi
            if (string.IsNullOrWhiteSpace(result.OutputText))
            {
                result.NotEmptyOrErrorPass = false;
                result.FailureReasons.Add("Bản dịch trả về rỗng (Empty output).");
            }
            else
            {
                foreach (var err in GenericErrorKeywords)
                {
                    if (result.OutputText.Contains(err, StringComparison.OrdinalIgnoreCase))
                    {
                        result.NotEmptyOrErrorPass = false;
                        result.FailureReasons.Add($"Bản dịch chứa thông báo lỗi hệ thống: '{err}'.");
                        break;
                    }
                }

                // Không để nguyên xi câu nguồn tiếng Nhật/tiếng Anh mà không dịch
                if (result.OutputText.Equals(sample.SourceCleaned, StringComparison.OrdinalIgnoreCase) && 
                    sample.SourceCleaned.Length > 5)
                {
                    result.NotEmptyOrErrorPass = false;
                    result.FailureReasons.Add("Bản dịch giữ nguyên 100% câu nguồn không dịch.");
                }
            }

            // 2. Kiểm tra các thuật ngữ bắt buộc (Locked Terms)
            if (sample.LockedTerms.Count > 0 && !string.IsNullOrEmpty(result.OutputText))
            {
                foreach (var locked in sample.LockedTerms)
                {
                    if (!result.OutputText.Contains(locked, StringComparison.OrdinalIgnoreCase))
                    {
                        result.LockedTermsPass = false;
                        result.FailureReasons.Add($"Thiếu thuật ngữ bắt buộc (Locked Term): '{locked}'.");
                    }
                }
            }

            // 3. Kiểm tra các từ cấm kỵ (Forbidden Terms như 'bạn', 'chủ nhân', 'tiền bối')
            if (sample.ForbiddenTerms.Count > 0 && !string.IsNullOrEmpty(result.OutputText))
            {
                foreach (var forbidden in sample.ForbiddenTerms)
                {
                    // Dùng regex word boundary để tránh bắt nhầm (ví dụ 'bạn' trong 'bản lĩnh')
                    string pattern = $@"(?<!\p{{L}}){Regex.Escape(forbidden)}(?!\p{{L}})";
                    if (Regex.IsMatch(result.OutputText, pattern, RegexOptions.IgnoreCase))
                    {
                        result.ForbiddenTermsPass = false;
                        result.FailureReasons.Add($"Xuất hiện từ cấm kỵ vi phạm văn phong game: '{forbidden}'.");
                    }
                }
            }

            // 4. Kiểm tra nhất quán đại từ xưng hô (Pronouns Consistency)
            if (sample.ExpectedPronounPairs.Count > 0 && !string.IsNullOrEmpty(result.OutputText))
            {
                bool matchedAnyPair = false;
                foreach (var pair in sample.ExpectedPronounPairs)
                {
                    bool allWordsInPairMatched = true;
                    foreach (var word in pair)
                    {
                        string pattern = $@"(?<!\p{{L}}){Regex.Escape(word)}(?!\p{{L}})";
                        if (!Regex.IsMatch(result.OutputText, pattern, RegexOptions.IgnoreCase))
                        {
                            allWordsInPairMatched = false;
                            break;
                        }
                    }
                    if (allWordsInPairMatched)
                    {
                        matchedAnyPair = true;
                        break;
                    }
                }

                if (!matchedAnyPair)
                {
                    result.PronounConsistencyPass = false;
                    string expectedDesc = string.Join(" HOẶC ", sample.ExpectedPronounPairs.Select(p => $"[{string.Join(", ", p)}]"));
                    result.Warnings.Add($"Không tìm thấy cặp đại từ xưng hô kỳ vọng ({expectedDesc}).");
                }
            }

            // 5. Tính độ tương đồng với tập bản dịch tham chiếu chuẩn (Reference Translations)
            if (sample.ReferenceTranslations.Count > 0 && !string.IsNullOrEmpty(result.OutputText))
            {
                double maxSim = 0.0;
                string bestRef = sample.ReferenceTranslations[0];

                foreach (var refText in sample.ReferenceTranslations)
                {
                    double sim = CalculateTokenSimilarity(result.OutputText, refText);
                    if (sim > maxSim)
                    {
                        maxSim = sim;
                        bestRef = refText;
                    }
                }

                result.SimilarityToBestReference = Math.Round(maxSim, 3);
                result.BestMatchingReference = bestRef;
            }
            else
            {
                result.SimilarityToBestReference = result.NotEmptyOrErrorPass ? 1.0 : 0.0;
            }

            // 6. Tính điểm tổng hợp (Overall Score 1.0 - 5.0)
            if (!result.NotEmptyOrErrorPass)
            {
                result.OverallScore = 1.0;
                result.IsPass = false;
            }
            else if (!result.LockedTermsPass)
            {
                // Mất thuật ngữ khóa là lỗi chặn
                result.OverallScore = 2.0;
                result.IsPass = false;
            }
            else if (!result.ForbiddenTermsPass)
            {
                // Dùng từ cấm kỵ như 'bạn'
                result.OverallScore = 2.5;
                result.IsPass = false;
            }
            else
            {
                // Cơ sở: 3.5 điểm + (1.5 điểm * độ tương đồng tham chiếu)
                double baseScore = 3.5 + (1.5 * result.SimilarityToBestReference);
                if (!result.PronounConsistencyPass)
                {
                    baseScore -= 0.5; // Trừ điểm nhẹ nếu xưng hô chưa sát cặp kỳ vọng
                }
                result.OverallScore = Math.Round(Math.Clamp(baseScore, 1.0, 5.0), 2);
                result.IsPass = result.OverallScore >= 3.5;
            }

            return result;
        }

        /// <summary>
        /// Tính hệ số Sorensen-Dice trên tập từ (Token-based similarity)
        /// giúp đánh giá ngữ nghĩa tương đồng mà không bị phạt nặng bởi dấu câu hay thứ tự nhỏ.
        /// </summary>
        public static double CalculateTokenSimilarity(string textA, string textB)
        {
            if (string.IsNullOrWhiteSpace(textA) || string.IsNullOrWhiteSpace(textB)) return 0.0;

            var tokensA = Tokenize(textA);
            var tokensB = Tokenize(textB);

            if (tokensA.Count == 0 && tokensB.Count == 0) return 1.0;
            if (tokensA.Count == 0 || tokensB.Count == 0) return 0.0;

            int intersection = 0;
            var multisetB = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tokensB)
            {
                multisetB[t] = multisetB.TryGetValue(t, out int count) ? count + 1 : 1;
            }

            foreach (var t in tokensA)
            {
                if (multisetB.TryGetValue(t, out int count) && count > 0)
                {
                    intersection++;
                    multisetB[t] = count - 1;
                }
            }

            return (2.0 * intersection) / (tokensA.Count + tokensB.Count);
        }

        private static List<string> Tokenize(string text)
        {
            // Tách từ theo khoảng trắng và bỏ dấu câu thừa
            var cleaned = Regex.Replace(text.ToLowerInvariant(), @"[.,!?;:\""'\(\)―—\-~]+", " ");
            return cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }
    }
}
