namespace TranslateBot.Translation
{
    // Phân cấp ưu tiên glossary (Section 26):
    // Character > Story > Game > Global — thuật ngữ cụ thể overrides thuật ngữ chung.
    public enum GlossaryTier
    {
        Global,     // Áp dụng mọi game
        Game,       // Áp dụng cho 1 game cụ thể (VD: FGO)
        Story,      // Áp dụng cho 1 chapter/arc
        Character,  // Áp dụng cho 1 nhân vật cụ thể
        Session     // Chỉ tồn tại trong phiên hiện tại
    }

    // Mức độ ràng buộc:
    // Locked = bắt buộc dùng, Preferred = ưu tiên dùng, Suggested = gợi ý.
    public enum GlossaryLevel
    {
        Suggested,  // Gợi ý — AI có thể bỏ qua nếu ngữ cảnh không phù hợp
        Preferred,  // Ưu tiên — AI nên dùng trừ khi lý do rõ ràng
        Locked      // Bắt buộc — PHẢI dùng chính xác (VD: "Master" = "Master")
    }

    // Một mục trong bảng thuật ngữ (Schema 2.0).
    public class GlossaryEntry
    {
        public string Source { get; set; } = "";     // Từ gốc: "Senpai"
        public string Target { get; set; } = "";     // Bản dịch: "tiền bối"
        public List<string> Aliases { get; set; } = new(); // Biến thể: ["NP", "宝具"]
        public GlossaryTier Tier { get; set; } = GlossaryTier.Game;
        public GlossaryLevel Level { get; set; } = GlossaryLevel.Preferred;
        public string? Character { get; set; }        // Nhân vật áp dụng (nếu Tier == Character)
        public string? Context { get; set; }         // Ghi chú ngữ cảnh (optional)
        public string Notes { get; set; } = "";       // Ghi chú bổ sung
        public bool CaseSensitive { get; set; } = false;
    }
}
