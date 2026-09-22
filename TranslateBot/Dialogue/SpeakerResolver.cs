using System;
using TranslateBot.Translation;

namespace TranslateBot.Dialogue
{
    public class SpeakerResolutionResult
    {
        public bool HasSpeaker => !string.IsNullOrWhiteSpace(Speaker) && Confidence >= 0.6;
        public string? Speaker { get; set; }
        public CharacterProfile? Profile { get; set; }
        public string CleanedDialogue { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string Source { get; set; } = "None"; // "AreaOcr", "InTextSyntax", "HistoryContinuity", "None"
    }

    // Milestone 2.2: Phân giải người nói đa nguồn với tính điểm tin cậy (Multi-Source Speaker Resolution)
    // 1. Khu vực OCR riêng biệt (Area OCR) - Tin cậy cao nhất (~0.95)
    // 2. Cú pháp trong văn bản (Brackets 【...】, Quotes Speaker「...」, Colon Speaker: ...) (~0.80 - 0.90)
    // 3. Tính liên tục hội thoại (Continuity từ lịch sử nếu là câu thoại trong ngoặc tiếp theo) (~0.70)
    public class SpeakerResolver
    {
        private readonly CharacterProfileManager _profileManager;
        private double _confidenceThreshold;
        private string? _lastKnownSpeaker;
        private DateTime _lastSpeakerTime = DateTime.MinValue;
        private static readonly TimeSpan ContinuityTimeout = TimeSpan.FromSeconds(15);

        public SpeakerResolver(CharacterProfileManager profileManager, double confidenceThreshold = 0.6)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _confidenceThreshold = confidenceThreshold;
        }

        public double ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set => _confidenceThreshold = value;
        }

        public string? LastKnownSpeaker => _lastKnownSpeaker;

        public void ResetHistory()
        {
            _lastKnownSpeaker = null;
            _lastSpeakerTime = DateTime.MinValue;
        }

        public SpeakerResolutionResult Resolve(
            string rawText,
            string? areaOcrSpeaker = null,
            DialogueHistory? history = null,
            string? currentContext = null)
        {
            string cleanText = (rawText ?? "").Trim();

            // 1. Area OCR có độ ưu tiên cao nhất
            if (!string.IsNullOrWhiteSpace(areaOcrSpeaker))
            {
                string candidate = areaOcrSpeaker.Trim();
                var profile = _profileManager.FindBySpeakerName(candidate, currentContext);
                string resolvedName = profile?.Name ?? candidate;
                double confidence = profile != null ? 0.99 : 0.95;

                // Tách bỏ nếu trong nội dung thoại cũng có tiền tố người nói dư thừa
                var syntax = SpeakerDetector.Detect(cleanText, _profileManager);
                string dialogue = syntax.HasSpeaker ? syntax.CleanedDialogue : cleanText;

                _lastKnownSpeaker = resolvedName;
                _lastSpeakerTime = DateTime.Now;

                return new SpeakerResolutionResult
                {
                    Speaker = resolvedName,
                    Profile = profile,
                    CleanedDialogue = dialogue,
                    Confidence = confidence,
                    Source = "AreaOcr"
                };
            }

            // 2. Phân tích cú pháp trong văn bản (In-text syntax)
            var detection = SpeakerDetector.Detect(cleanText, _profileManager);
            if (detection.HasSpeaker)
            {
                string candidate = detection.Speaker!;
                var profile = detection.Profile ?? _profileManager.FindBySpeakerName(candidate, currentContext);
                string resolvedName = profile?.Name ?? candidate;
                double confidence = profile != null ? 0.90 : 0.80;

                _lastKnownSpeaker = resolvedName;
                _lastSpeakerTime = DateTime.Now;

                return new SpeakerResolutionResult
                {
                    Speaker = resolvedName,
                    Profile = profile,
                    CleanedDialogue = detection.CleanedDialogue,
                    Confidence = confidence,
                    Source = "InTextSyntax"
                };
            }

            // 3. Kiểm tra tính liên tục qua dấu thoại (Continuity)
            // Nếu câu bắt đầu bằng 「 hoặc 『 hoặc " và người nói trước đó vẫn đang trong phiên nói chuyện gần
            bool isQuote = cleanText.StartsWith("「") || cleanText.StartsWith("『") ||
                           cleanText.StartsWith("\"") || cleanText.StartsWith("“");

            if (isQuote && !string.IsNullOrEmpty(_lastKnownSpeaker) && (DateTime.Now - _lastSpeakerTime) < ContinuityTimeout)
            {
                var profile = _profileManager.FindBySpeakerName(_lastKnownSpeaker, currentContext);
                _lastSpeakerTime = DateTime.Now;

                return new SpeakerResolutionResult
                {
                    Speaker = _lastKnownSpeaker,
                    Profile = profile,
                    CleanedDialogue = detection.CleanedDialogue,
                    Confidence = 0.70,
                    Source = "HistoryContinuity"
                };
            }

            // 4. Không phát hiện hoặc không đủ độ tin cậy
            return new SpeakerResolutionResult
            {
                Speaker = null,
                Profile = null,
                CleanedDialogue = detection.CleanedDialogue,
                Confidence = 0.0,
                Source = "None"
            };
        }
    }
}
