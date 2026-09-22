using System;
using System.Collections.Generic;
using System.Linq;
using TranslateBot.Dialogue;
using TranslateBot.OCR;
using TranslateBot.OCR.Preprocessing;
using TranslateBot.Translation;

namespace TranslateBot.GameProfiles
{
    /// <summary>
    /// Schema hồ sơ Game toàn diện (Section 15 của Master Plan)
    /// Đóng gói trọn vẹn: nhận diện cửa sổ, vùng quét OCR, exclusion zones,
    /// thuật ngữ, nhân vật, và phân cảnh câu chuyện.
    /// </summary>
    public class GameProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "Untitled Game";
        public string GameName { get; set; } = "General Game";
        
        // Điều kiện nhận diện tiến trình và tiêu đề cửa sổ
        public string ProcessMatch { get; set; } = string.Empty;
        public string TitleMatch { get; set; } = string.Empty;

        // Cấu hình ngôn ngữ
        public string SourceLanguage { get; set; } = "ja";
        public string TargetLanguage { get; set; } = "vi";

        // Cấu hình OCR & Ổn định thoại
        public StabilityPreset StabilityPreset { get; set; } = StabilityPreset.Balanced;
        public PreprocessPreset PreprocessPreset { get; set; } = PreprocessPreset.FgoDialogue;
        public string PreferredOcrEngine { get; set; } = "WindowsOCR";

        // Vùng nhận diện OCR và vùng loại trừ
        public List<OcrArea> OcrAreas { get; set; } = new();
        public List<ExclusionArea> ExclusionAreas { get; set; } = new();

        // Dữ liệu ngữ cảnh chuyên biệt của game
        public List<CharacterProfile> Characters { get; set; } = new();
        public List<GlossaryEntry> Glossary { get; set; } = new();
        public List<SceneProfile> Scenes { get; set; } = new();

        public string? ActiveSceneName { get; set; }

        public SceneProfile? GetActiveScene()
        {
            if (string.IsNullOrWhiteSpace(ActiveSceneName) || Scenes.Count == 0)
                return Scenes.FirstOrDefault();

            return Scenes.FirstOrDefault(s => s.Name.Equals(ActiveSceneName, StringComparison.OrdinalIgnoreCase))
                   ?? Scenes.FirstOrDefault();
        }
    }
}
