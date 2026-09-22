using System;

namespace TranslateBot.Session
{
    // Section 37: Một bản ghi chi tiết của câu thoại trong phiên chơi game.
    public class SessionRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public int SequenceId { get; set; }
        public string? Speaker { get; set; }
        public string SourceText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string Provider { get; set; } = "Gemini";
        public double LatencyMs { get; set; }
        public bool IsCacheHit { get; set; }

        public string FormattedTime => Timestamp.ToString("HH:mm:ss");
        public string FormattedDate => Timestamp.ToString("yyyy-MM-dd");
    }
}
