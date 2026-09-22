using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TranslateBot.Dialogue;

namespace TranslateBot.Translation
{
    // Milestone 2.2: FGO Context Engine 2.0 (Ngữ cảnh phân tầng chuyên sâu FGO)
    // Phân tầng ngữ cảnh 5 lớp:
    // 1. Bản sắc Game & Quy tắc (Game Identity & Universe Lore)
    // 2. Ngữ cảnh phân đoạn/chương hồi (Scene/Chapter/Story Context: Lostbelt, Chaldea, Singularity)
    // 3. Hồ sơ nhân vật (Speaker Profile, SpeakingStyle, PreferredPronouns, AddressingRules)
    // 4. Cửa sổ trượt lịch sử hội thoại (Sliding Window Dialogue History)
    // 5. Bảng thuật ngữ động thích ứng (Active Dynamic Glossary & Overrides)
    public class FgoContextEngine
    {
        private readonly GlossaryManager _glossaryManager;
        private readonly CharacterProfileManager _characterProfileManager;
        private readonly SpeakerResolver _speakerResolver;

        public string GameName { get; set; } = "Fate/Grand Order";
        public string? SceneContext { get; set; } = "Chaldea";
        public int HistoryWindowSize { get; set; } = 5;
        public bool FilterGlossaryByText { get; set; } = true;
        public List<GlossaryEntry>? SceneGlossaryOverrides { get; set; }

        public GlossaryManager GlossaryManager => _glossaryManager;
        public CharacterProfileManager CharacterProfileManager => _characterProfileManager;
        public SpeakerResolver SpeakerResolver => _speakerResolver;

        public FgoContextEngine(
            GlossaryManager glossaryManager,
            CharacterProfileManager characterProfileManager,
            SpeakerResolver? speakerResolver = null)
        {
            _glossaryManager = glossaryManager ?? throw new ArgumentNullException(nameof(glossaryManager));
            _characterProfileManager = characterProfileManager ?? throw new ArgumentNullException(nameof(characterProfileManager));
            _speakerResolver = speakerResolver ?? new SpeakerResolver(_characterProfileManager);
        }

        // Xây dựng TranslationContext đầy đủ và phân tầng từ văn bản và trạng thái hệ thống
        public TranslationContext BuildContext(
            string text,
            string? areaOcrSpeaker = null,
            DialogueHistory? history = null,
            string? explicitSpeaker = null,
            CharacterProfile? explicitProfile = null)
        {
            var ctx = new TranslationContext
            {
                GameName = GameName,
                SceneContext = SceneContext
            };

            // 1. Phân giải người nói & Profile
            if (!string.IsNullOrEmpty(explicitSpeaker))
            {
                ctx.SpeakerName = explicitSpeaker;
                ctx.SpeakerProfile = explicitProfile ?? _characterProfileManager.FindBySpeakerName(explicitSpeaker, SceneContext);
            }
            else
            {
                var resolution = _speakerResolver.Resolve(text, areaOcrSpeaker, history, SceneContext);
                if (resolution.HasSpeaker)
                {
                    ctx.SpeakerName = resolution.Speaker;
                    ctx.SpeakerProfile = explicitProfile ?? resolution.Profile;
                }
            }

            // 2. Lịch sử hội thoại trượt (Dialogue History Sliding Window)
            if (history != null && HistoryWindowSize > 0)
            {
                var recent = history.Entries
                    .Reverse()
                    .Take(HistoryWindowSize)
                    .Reverse();

                foreach (var item in recent)
                {
                    ctx.PreviousLines.Add(new ContextLine
                    {
                        Original = item.OriginalText,
                        Translated = item.TranslatedText,
                        Speaker = item.Speaker
                    });
                }
            }

            // 3. Thuật ngữ thích ứng (Active Glossary)
            var combinedOverrides = new List<GlossaryEntry>();
            if (SceneGlossaryOverrides != null && SceneGlossaryOverrides.Count > 0)
            {
                combinedOverrides.AddRange(SceneGlossaryOverrides);
            }
            if (ctx.SpeakerProfile?.GlossaryOverrides != null && ctx.SpeakerProfile.GlossaryOverrides.Count > 0)
            {
                combinedOverrides.AddRange(ctx.SpeakerProfile.GlossaryOverrides);
            }

            var finalGlossary = new Dictionary<string, GlossaryEntry>(StringComparer.OrdinalIgnoreCase);

            if (FilterGlossaryByText)
            {
                // Chỉ lấy thuật ngữ thực tế xuất hiện trong câu thoại để tiết kiệm token và tránh nhiễu prompt
                var matched = _glossaryManager.GetMatchingEntries(text, ctx.SpeakerName, combinedOverrides);
                foreach (var entry in matched)
                {
                    finalGlossary[entry.Source] = entry;
                }

                // Luôn bổ sung các thuật ngữ Locked quan trọng của game/context
                var lockedEntries = _glossaryManager.GetEntriesForContext(ctx.SpeakerName)
                    .Where(e => e.Level == GlossaryLevel.Locked);

                foreach (var entry in lockedEntries)
                {
                    if (!finalGlossary.ContainsKey(entry.Source))
                    {
                        finalGlossary[entry.Source] = entry;
                    }
                }
            }
            else
            {
                var allContextEntries = _glossaryManager.GetEntriesForContext(ctx.SpeakerName);
                foreach (var entry in allContextEntries)
                {
                    finalGlossary[entry.Source] = entry;
                }
                foreach (var ov in combinedOverrides)
                {
                    finalGlossary[ov.Source] = ov;
                }
            }

            // Sắp xếp thuật ngữ theo độ ưu tiên: Locked trước, Tier cao hơn trước
            var sortedGlossary = finalGlossary.Values
                .OrderByDescending(g => g.Level)
                .ThenByDescending(g => g.Tier)
                .ToList();

            ctx.ActiveGlossary.AddRange(sortedGlossary);

            return ctx;
        }

        // Tạo chuỗi prompt hoàn chỉnh phục vụ kiểm tra hoặc provider chuyên dụng
        public string BuildSystemInstruction(string targetLanguage = "tiếng Việt")
        {
            return $"Bạn là một dịch giả game chuyên nghiệp (Fate/Grand Order, Visual Novel, RPG kỳ ảo).\n" +
                   $"NHIỆM VỤ:\n" +
                   $"1. Tự động sửa các lỗi chính tả phổ biến do OCR (ví dụ: 'witn' -> 'with', 'rn' -> 'm').\n" +
                   $"2. Loại bỏ từ rác hoặc nút bấm giao diện không phải lời thoại.\n" +
                   $"3. DỊCH THOÁT Ý THEO ĐÚNG GIỌNG ĐIỆU NHÂN VẬT & NGỮ CẢNH: Văn phong tự nhiên, trôi chảy, đúng phong cách FGO vietsub.\n" +
                   $"4. CHỈ TRẢ VỀ BẢN DỊCH {targetLanguage.ToUpperInvariant()}. Tuyệt đối KHÔNG giải thích, KHÔNG thêm lời mở đầu.";
        }
    }
}
