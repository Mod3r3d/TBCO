using System.Collections.Generic;

namespace TranslateBot.Translation
{
    // Section 27: Hồ sơ nhân vật — mỗi character có phong cách nói riêng.
    // Khi speaker được nhận dạng (qua Speaker OCR area hoặc manual),
    // profile được inject vào prompt để Gemini dịch đúng giọng điệu.
    //
    // VD: Mash nói lịch sự "Vâng, thưa Senpai" vs Gilgamesh ngạo nghễ "Hừ, đồ phàm nhân"
    public class CharacterProfile
    {
        public string Name { get; set; } = "";

        // Tên khác / phiên bản khác (VD: "Mash", "Mashu", "マシュ", "Shielder")
        public List<string> Aliases { get; set; } = new();

        // Mô tả phong cách nói (inject vào prompt)
        // VD: "Lịch sự, nhẹ nhàng, hay dùng kính ngữ. Xưng 'em', gọi Master là 'Senpai'"
        public string SpeakingStyle { get; set; } = "";

        // Đại từ ưu tiên: "em / tiền bối", "ta / ngươi"
        public string PreferredPronouns { get; set; } = "";

        // Quy tắc xưng hô đặc biệt
        // VD: "Gọi Ritsuka là 'Senpai', gọi Da Vinci là 'Da Vinci-chan'"
        public string AddressingRules { get; set; } = "";

        // Phase 7: Ngữ cảnh xuất hiện (Chaldea, Lostbelt, Singularity, My Room...)
        public List<string> Contexts { get; set; } = new();

        // Độ ưu tiên khi có nhiều profile trùng tên/alias
        public int Priority { get; set; } = 100;

        // Thuật ngữ ghi đè riêng cho nhân vật này
        public List<GlossaryEntry> GlossaryOverrides { get; set; } = new();
    }
}
