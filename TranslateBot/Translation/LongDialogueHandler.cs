using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TranslateBot.Infrastructure;

namespace TranslateBot.Translation
{
    // Section 28: Xử lý đoạn thoại dài (>500 chars).
    // Thay vì cắt mù theo ký tự (gây hỏng ngữ nghĩa), chia theo ranh giới câu
    // (dấu chấm, chấm hỏi, chấm than) và giữ context overlap giữa các chunks.
    // Sau khi dịch từng chunk, merge lại thành 1 đoạn dịch liền mạch.
    public static class LongDialogueHandler
    {
        private const int ThresholdChars = 500;

        // Regex chia theo câu: kết thúc bằng .!? hoặc tương đương CJK
        private static readonly Regex SentenceEnd = new(@"(?<=[.!?。！？])\s+", RegexOptions.Compiled);

        public static bool IsLongDialogue(string text)
            => text.Length > ThresholdChars;

        // Chia text thành chunks, mỗi chunk không quá ~ThresholdChars,
        // cắt tại ranh giới câu gần nhất. Giữ câu cuối chunk N làm câu đầu chunk N+1
        // (context overlap) để dịch liền mạch.
        public static List<string> SplitIntoChunks(string text)
        {
            if (text.Length <= ThresholdChars)
                return new List<string> { text };

            var sentences = SentenceEnd.Split(text);
            var chunks = new List<string>();
            var current = new StringBuilder();
            string? lastSentence = null;

            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence)) continue;

                // Nếu thêm câu này vào sẽ quá dài → đóng chunk hiện tại
                if (current.Length + sentence.Length > ThresholdChars && current.Length > 0)
                {
                    chunks.Add(current.ToString().Trim());

                    // Context overlap: bắt đầu chunk mới bằng câu cuối chunk trước
                    current.Clear();
                    if (lastSentence != null)
                    {
                        current.Append(lastSentence).Append(' ');
                    }
                }

                current.Append(sentence).Append(' ');
                lastSentence = sentence;
            }

            // Chunk cuối cùng
            if (current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
            }

            // Edge case: nếu không chia được (1 câu siêu dài) → trả nguyên bản
            if (chunks.Count == 0)
            {
                chunks.Add(text);
            }

            AppLogger.Info($"[LONG_DIALOGUE] Chia {text.Length} chars → {chunks.Count} chunks");
            return chunks;
        }

        // Merge kết quả dịch từ nhiều chunks thành 1 đoạn.
        // Loại bỏ context overlap (câu trùng lặp ở đầu chunk sau).
        public static string MergeTranslations(List<string> translatedChunks)
        {
            if (translatedChunks.Count <= 1)
                return translatedChunks.Count == 1 ? translatedChunks[0] : "";

            var result = new StringBuilder();
            result.Append(translatedChunks[0]);

            for (int i = 1; i < translatedChunks.Count; i++)
            {
                string chunk = translatedChunks[i];

                // Bỏ dòng đầu tiên nếu trùng với dòng cuối chunk trước (context overlap)
                string prevChunk = translatedChunks[i - 1];
                string prevLastLine = ExtractLastSentence(prevChunk);
                string currFirstLine = ExtractFirstSentence(chunk);

                if (!string.IsNullOrEmpty(prevLastLine) && !string.IsNullOrEmpty(currFirstLine)
                    && currFirstLine.StartsWith(prevLastLine[..Math.Min(20, prevLastLine.Length)]))
                {
                    // Bỏ phần overlap
                    chunk = chunk[currFirstLine.Length..].TrimStart();
                }

                result.Append(' ').Append(chunk);
            }

            return result.ToString().Trim();
        }

        private static string ExtractFirstSentence(string text)
        {
            var match = SentenceEnd.Match(text);
            return match.Success ? text[..match.Index].Trim() : text;
        }

        private static string ExtractLastSentence(string text)
        {
            var matches = SentenceEnd.Matches(text);
            if (matches.Count == 0) return text;
            var lastMatch = matches[^1];
            return text[(lastMatch.Index + lastMatch.Length)..].Trim();
        }
    }
}
