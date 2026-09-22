using System;
using System.Text.RegularExpressions;
using TranslateBot.Translation;

namespace TranslateBot.Dialogue
{
    public class SpeakerDetectionResult
    {
        public bool HasSpeaker => !string.IsNullOrWhiteSpace(Speaker);
        public string? Speaker { get; set; }
        public string CleanedDialogue { get; set; } = string.Empty;
        public string CleanDialogue => CleanedDialogue;
        public CharacterProfile? Profile { get; set; }
    }

    // Section 34, 35 & 27: Nhận diện người nói nâng cao.
    // Phân tách cấu trúc thoại visual novel (cú pháp hai chấm, ngoặc vuông, ngoặc vuông Nhật...)
    // giúp:
    // 1. Tách sạch tên nhân vật khỏi câu thoại gửi lên LLM (tránh dịch sai tên, tăng cache hit).
    // 2. Tự động liên kết với CharacterProfileManager để nạp đại từ nhân xưng chuẩn.
    public static class SpeakerDetector
    {
        // 1. Cú pháp hai chấm: "Mash: Senpai, are you ready?" hoặc "マシュ：先輩..."
        private static readonly Regex ColonPattern = new(
            @"^\s*(?<speaker>[\w\s\.\(\)\-'\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff]{2,30})\s*[:：]\s*(?<text>.+)$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // 2. Cú pháp ngoặc vuông / ngoặc góc: "【Mash】Senpai..." hoặc "[Mash] Senpai..."
        private static readonly Regex BracketPattern = new(
            @"^\s*[【\[［](?<speaker>[^】\]］]{1,30})[】\]］]\s*(?<text>.+)$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // 3. Cú pháp ngoặc thoại tiếng Nhật có tên người nói: "Mash「先輩...」"
        private static readonly Regex JapaneseQuotePattern = new(
            @"^\s*(?<speaker>[^「『]{2,30})\s*[「『](?<text>[^」』]+)[」』]?\s*$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // 4. Cú pháp ngoặc thoại độc lập không có tên: "「フォウ、フォウ！」"
        private static readonly Regex StandaloneJapaneseQuotePattern = new(
            @"^\s*[「『](?<text>[^」』]+)[」』]?\s*$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public static SpeakerDetectionResult ExtractSpeaker(string rawText) => Detect(rawText, null);

        public static string NormalizeSpeaker(string speakerName, CharacterProfileManager? profileManager)
        {
            if (string.IsNullOrWhiteSpace(speakerName) || profileManager == null)
                return speakerName;
            return profileManager.FindBySpeakerName(speakerName)?.Name ?? speakerName;
        }

        public static SpeakerDetectionResult Detect(string rawText, CharacterProfileManager? profileManager = null)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return new SpeakerDetectionResult { CleanedDialogue = "" };
            }

            string text = rawText.Trim();
            string? detectedSpeaker = null;
            string cleanedText = text;

            // Kiểm tra mẫu 1: Brackets 【Speaker】 hoặc [Speaker]
            var bracketMatch = BracketPattern.Match(text);
            if (bracketMatch.Success)
            {
                detectedSpeaker = bracketMatch.Groups["speaker"].Value.Trim();
                cleanedText = bracketMatch.Groups["text"].Value.Trim();
            }
            else
            {
                // Kiểm tra mẫu 2: Japanese Quotes Speaker「...」
                var quoteMatch = JapaneseQuotePattern.Match(text);
                if (quoteMatch.Success)
                {
                    detectedSpeaker = quoteMatch.Groups["speaker"].Value.Trim();
                    cleanedText = quoteMatch.Groups["text"].Value.Trim();
                }
                else
                {
                    // Kiểm tra mẫu 3: Colon Speaker: ...
                    var colonMatch = ColonPattern.Match(text);
                    if (colonMatch.Success)
                    {
                        string candidate = colonMatch.Groups["speaker"].Value.Trim();
                        // Tránh match nhầm URL "http:" hoặc time "12:00"
                        if (!candidate.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                            !candidate.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                            !int.TryParse(candidate, out _))
                        {
                            detectedSpeaker = candidate;
                            cleanedText = colonMatch.Groups["text"].Value.Trim();
                        }
                    }
                    else
                    {
                        // Kiểm tra mẫu 4: Standalone Japanese Quotes 「...」
                        var standaloneMatch = StandaloneJapaneseQuotePattern.Match(text);
                        if (standaloneMatch.Success)
                        {
                            cleanedText = standaloneMatch.Groups["text"].Value.Trim();
                        }
                    }
                }
            }

            CharacterProfile? profile = null;
            if (!string.IsNullOrWhiteSpace(detectedSpeaker) && profileManager != null)
            {
                profile = profileManager.FindBySpeakerName(detectedSpeaker);
                if (profile != null)
                {
                    // Chuẩn hóa tên nhân vật về tên chính thức trong hồ sơ
                    detectedSpeaker = profile.Name;
                }
            }

            return new SpeakerDetectionResult
            {
                Speaker = detectedSpeaker,
                CleanedDialogue = string.IsNullOrWhiteSpace(cleanedText) ? text : cleanedText,
                Profile = profile
            };
        }
    }
}
