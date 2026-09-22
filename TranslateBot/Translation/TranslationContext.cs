using System.Collections.Generic;

namespace TranslateBot.Translation
{
    // Section 25: Context gửi kèm mỗi request dịch thuật.
    // Gemini dịch chính xác hơn khi biết ngữ cảnh (câu trước đó, ai đang nói,
    // thuật ngữ game). VD: "him" → biết "him" là Ritsuka nhờ câu trước nhắc tên.
    public class TranslationContext
    {
        public string GameName { get; set; } = "Fate/Grand Order";
        public string? SceneContext { get; set; }
        public string? SpeakerName { get; set; }
        public CharacterProfile? SpeakerProfile { get; set; }
        public List<ContextLine> PreviousLines { get; } = new();
        public List<GlossaryEntry> ActiveGlossary { get; } = new();
    }

    // Một câu thoại trong chuỗi context (Original + Translated + Speaker nếu biết)
    public class ContextLine
    {
        public string Original { get; set; } = "";
        public string Translated { get; set; } = "";
        public string? Speaker { get; set; }
    }
}
