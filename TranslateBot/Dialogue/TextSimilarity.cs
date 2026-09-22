using System;
using System.Collections.Generic;
using TranslateBot.Infrastructure;

namespace TranslateBot.Dialogue
{
    // Theo Section 4.2 của plan: giữ khả năng nhận biết lỗi OCR nhỏ (VD:
    // "prophecies" vs "propneaes" là gần nhau), nhưng KHÔNG chạy full string-distance
    // ở mọi nơi. Chiến lược: lọc rẻ trước, chỉ chạy phép so sánh đắt (Levenshtein) khi
    // 2 lớp lọc rẻ ở trên đã cho thấy khả năng cao là cùng một câu.
    public static class TextSimilarity
    {
        // ═══ Ngưỡng ════════════════════════════════════════════════════════
        // Nâng từ 0.82 lên 0.92 để giảm dương tính giả (2 câu khác nhau bị
        // nhận nhầm là OCR correction → mất câu).
        private const double CharSimilarityThreshold = 0.92;
        private const double LengthDiffThreshold = 0.3;
        private const double TokenOverlapThreshold = 0.5;

        // Điều kiện "edit runs": lỗi OCR thật sự chỉ đổi 1-2 ký tự LẺ TẺ
        // rải rác, mỗi "run" (đoạn ký tự khác biệt liên tục) rất ngắn.
        // 2 câu khác nhau dù giống bao nhiêu vẫn thường khác ở CẢ CỤM TỪ.
        private const int MaxEditRuns = 3;
        private const int MaxRunLength = 3;

        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        }

        // Trả về true nếu "newText" nhiều khả năng là bản OCR sửa lỗi nhỏ của
        // "oldText" (CÙNG một câu thoại), chứ không phải một câu thoại hoàn toàn khác.
        public static bool IsLikelyOcrCorrection(string oldText, string newText)
        {
            if (string.IsNullOrEmpty(oldText) || string.IsNullOrEmpty(newText))
                return false;

            string a = oldText.ToLowerInvariant();
            string b = newText.ToLowerInvariant();

            if (a == b) return true;

            // Lớp 1 (rẻ nhất): độ dài lệch quá nhiều -> chắc chắn không phải chỉ là
            // lỗi OCR vài ký tự trên CÙNG một câu.
            int maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0) return false;
            int lenDiff = Math.Abs(a.Length - b.Length);
            if ((double)lenDiff / maxLen > LengthDiffThreshold)
                return false;

            // Lớp 2 (rẻ): token overlap - không cần duyệt từng ký tự.
            double tokenOverlap = TokenOverlapRatio(a, b);
            if (tokenOverlap < TokenOverlapThreshold)
                return false; // Từ vựng khác nhau quá nhiều -> chắc chắn là câu khác

            // Lớp 3 (đắt hơn, CHỈ chạy khi đã qua 2 lớp lọc rẻ ở trên): char-level
            // similarity qua Levenshtein distance + edit runs analysis.
            var editInfo = LevenshteinWithEditRuns(a, b);
            double charSimilarity = 1.0 - (double)editInfo.Distance / maxLen;

            if (charSimilarity < CharSimilarityThreshold)
                return false;

            // Lớp 4 (MỚI): Phân tích edit runs — lỗi OCR thật sự chỉ sai 1-2 ký tự
            // lẻ tẻ rải rác (VD: "wouId" → "would" = 1 run, 2 ký tự). Nếu số runs
            // hoặc chiều dài run quá lớn → đây là 2 câu khác nhau dù giống nhau.
            if (editInfo.RunCount > MaxEditRuns)
                return false;

            if (editInfo.MaxRunLength > MaxRunLength)
                return false;

            // Tất cả điều kiện đã thỏa → đây nhiều khả năng là OCR correction
            AppLogger.Info($"[OCR_CORRECTION] charSim={charSimilarity:F3} runs={editInfo.RunCount} maxRunLen={editInfo.MaxRunLength} " +
                              $"old=\"{TruncateForLog(oldText, 60)}\" new=\"{TruncateForLog(newText, 60)}\"");
            return true;
        }

        // ═══ Fuzzy prefix matching (dùng cho Extending detection) ═══════
        // Thay thế StartsWith tuyệt đối: cho phép ~10% ký tự sai lệch do OCR jitter
        // ở phần đã đọc trước đó, tránh hiểu nhầm thành chuyển câu.
        public static bool IsFuzzyPrefix(string prefix, string fullText, double threshold = 0.90)
        {
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(fullText))
                return false;

            // fullText phải dài hơn prefix
            if (fullText.Length <= prefix.Length)
                return false;

            // So sánh đoạn đầu của fullText (cùng độ dài prefix) với prefix
            string head = fullText.Substring(0, prefix.Length);

            if (head == prefix) return true; // Fast path: khớp hoàn toàn

            int distance = LevenshteinDistance(prefix, head);
            double similarity = 1.0 - (double)distance / prefix.Length;
            return similarity >= threshold;
        }

        // ═══ Helpers ════════════════════════════════════════════════════════

        private static string TruncateForLog(string text, int maxLen)
        {
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen) + "…";
        }

        private static double TokenOverlapRatio(string a, string b)
        {
            var tokensA = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var tokensB = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (tokensA.Count == 0 || tokensB.Count == 0) return 0;

            int common = 0;
            foreach (var t in tokensA)
            {
                if (tokensB.Contains(t)) common++;
            }
            return (double)common / Math.Max(tokensA.Count, tokensB.Count);
        }

        // Levenshtein distance đơn giản (dùng cho IsFuzzyPrefix và các nơi khác)
        public static int LevenshteinDistance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[a.Length, b.Length];
        }

        // ═══ Levenshtein + Edit Runs ════════════════════════════════════════
        // Tính Levenshtein distance VÀ trích xuất edit runs bằng backtrack.
        // Edit run = đoạn liên tục các thao tác edit (substitute/insert/delete).
        // Lỗi OCR thật: ít runs, mỗi run ngắn (1-2 ký tự).
        // 2 câu khác nhau: nhiều runs hoặc runs dài (cả cụm từ).
        private static EditRunInfo LevenshteinWithEditRuns(string a, string b)
        {
            int m = a.Length, n = b.Length;
            var d = new int[m + 1, n + 1];
            for (int i = 0; i <= m; i++) d[i, 0] = i;
            for (int j = 0; j <= n; j++) d[0, j] = j;

            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            int distance = d[m, n];

            // Backtrack để tìm edit operations
            // 'M' = match, 'S' = substitute, 'I' = insert, 'D' = delete
            var ops = new List<char>();
            int bi = m, bj = n;
            while (bi > 0 || bj > 0)
            {
                if (bi > 0 && bj > 0 && a[bi - 1] == b[bj - 1] && d[bi, bj] == d[bi - 1, bj - 1])
                {
                    ops.Add('M');
                    bi--; bj--;
                }
                else if (bi > 0 && bj > 0 && d[bi, bj] == d[bi - 1, bj - 1] + 1)
                {
                    ops.Add('S'); // Substitute
                    bi--; bj--;
                }
                else if (bj > 0 && d[bi, bj] == d[bi, bj - 1] + 1)
                {
                    ops.Add('I'); // Insert
                    bj--;
                }
                else
                {
                    ops.Add('D'); // Delete
                    bi--;
                }
            }

            // Đếm edit runs (đoạn liên tục các thao tác edit khác 'M')
            int runCount = 0;
            int maxRunLength = 0;
            int currentRunLength = 0;

            for (int k = ops.Count - 1; k >= 0; k--)
            {
                if (ops[k] != 'M')
                {
                    currentRunLength++;
                }
                else
                {
                    if (currentRunLength > 0)
                    {
                        runCount++;
                        maxRunLength = Math.Max(maxRunLength, currentRunLength);
                        currentRunLength = 0;
                    }
                }
            }

            // Run cuối cùng (nếu kết thúc bằng edit)
            if (currentRunLength > 0)
            {
                runCount++;
                maxRunLength = Math.Max(maxRunLength, currentRunLength);
            }

            return new EditRunInfo
            {
                Distance = distance,
                RunCount = runCount,
                MaxRunLength = maxRunLength
            };
        }

        private struct EditRunInfo
        {
            public int Distance;
            public int RunCount;
            public int MaxRunLength;
        }
    }
}
