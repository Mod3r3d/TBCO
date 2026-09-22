using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TranslateBot.Infrastructure;

namespace TranslateBot.Translation
{
    // Quản lý bảng thuật ngữ (Section 26). Glossary lưu file riêng (glossary.json),
    // không nhét vào config.json vì có thể rất lớn (hàng trăm entry cho FGO).
    //
    // Khi build TranslationContext, GlossaryManager cung cấp danh sách entry phù hợp
    // với game + speaker hiện tại, đã sắp xếp theo ưu tiên:
    //   Locked trước Preferred trước Suggested
    //   Character > Story > Game > Global
    public class GlossaryManager
    {
        private static readonly string GlossaryPath = "glossary.json";
        private List<GlossaryEntry> _entries = new();

        public IReadOnlyList<GlossaryEntry> Entries => _entries.AsReadOnly();

        public void Load()
        {
            if (!File.Exists(GlossaryPath))
            {
                AppLogger.Info("[GLOSSARY] glossary.json không tồn tại, tự động khởi tạo glossary mặc định");
                _entries = GetDefaultEntries();
                Save();
                return;
            }

            try
            {
                string json = File.ReadAllText(GlossaryPath);
                _entries = JsonConvert.DeserializeObject<List<GlossaryEntry>>(json) ?? new();
                AppLogger.Info($"[GLOSSARY_LOADED] {_entries.Count} entries từ {GlossaryPath}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[GLOSSARY_ERROR] Lỗi đọc glossary: {ex.Message}");
            }
        }

        public void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_entries, Formatting.Indented);
                File.WriteAllText(GlossaryPath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[GLOSSARY_ERROR] Lỗi lưu glossary: {ex.Message}");
            }
        }

        // Trả về entries phù hợp cho context hiện tại, đã sắp xếp ưu tiên.
        // Locked entries luôn được include. Preferred/Suggested chỉ include nếu
        // tier phù hợp (Global/Game luôn include, Character chỉ include nếu khớp speaker).
        public List<GlossaryEntry> GetEntriesForContext(string? speakerName = null)
        {
            return _entries
                .Where(e =>
                {
                    // Character-tier chỉ áp dụng khi speaker khớp
                    if (e.Tier == GlossaryTier.Character)
                    {
                        if (string.IsNullOrEmpty(speakerName)) return false;
                        if (!string.IsNullOrEmpty(e.Character) && e.Character.Equals(speakerName, StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (!string.IsNullOrEmpty(e.Context) && e.Context.Contains(speakerName, StringComparison.OrdinalIgnoreCase))
                            return true;
                        return false;
                    }
                    return true;
                })
                .OrderByDescending(e => e.Level)    // Locked trước
                .ThenByDescending(e => e.Tier)       // Character > Story > Game > Global
                .ToList();
        }

        // Tìm các thuật ngữ thực sự xuất hiện trong văn bản thoại (Source hoặc Aliases)
        // để tối ưu token cho LLM prompt, hỗ trợ GlossaryOverrides từ CharacterProfile
        public List<GlossaryEntry> GetMatchingEntries(string text, string? speakerName = null, IEnumerable<GlossaryEntry>? extraOverrides = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<GlossaryEntry>();

            var candidateEntries = GetEntriesForContext(speakerName);

            // Hợp nhất glossary chung với extraOverrides nếu có
            var finalDict = new Dictionary<string, GlossaryEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in candidateEntries)
            {
                finalDict[entry.Source] = entry;
            }
            if (extraOverrides != null)
            {
                foreach (var ov in extraOverrides)
                {
                    finalDict[ov.Source] = ov;
                }
            }

            var matches = new List<GlossaryEntry>();
            foreach (var entry in finalDict.Values)
            {
                var comp = entry.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                bool isMatch = text.IndexOf(entry.Source, comp) >= 0;
                if (!isMatch && entry.Aliases != null && entry.Aliases.Count > 0)
                {
                    isMatch = entry.Aliases.Any(alias => !string.IsNullOrEmpty(alias) && text.IndexOf(alias, comp) >= 0);
                }

                if (isMatch)
                {
                    matches.Add(entry);
                }
            }

            return matches
                .OrderByDescending(e => e.Level)
                .ThenByDescending(e => e.Tier)
                .ThenByDescending(e => e.Source.Length)
                .ToList();
        }

        public void Add(GlossaryEntry entry) => _entries.Add(entry);

        public bool Remove(string source)
        {
            return _entries.RemoveAll(e => e.Source.Equals(source, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        public void Clear() => _entries.Clear();

        public void LoadFrom(IEnumerable<GlossaryEntry>? entries)
        {
            _entries.Clear();
            if (entries != null)
            {
                _entries.AddRange(entries);
            }
        }

        public List<GlossaryEntry> ToList() => new(_entries);

        private static List<GlossaryEntry> GetDefaultEntries()
        {
            return new List<GlossaryEntry>
            {
                new() { Source = "Servant", Target = "Servant", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Master", Target = "Master", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Senpai", Target = "tiền bối", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Noble Phantasm", Target = "Bảo Khí", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Holy Grail", Target = "Chén Thánh", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Singularity", Target = "Điểm Kỳ Dị", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred },
                new() { Source = "Lostbelt", Target = "Lostbelt", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Rayshift", Target = "Rayshift", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Mana", Target = "Mana", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Spirit Origin", Target = "Linh Căn", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred },
                new() { Source = "Saint Quartz", Target = "Thạch Anh Thánh", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred },
                new() { Source = "Command Spell", Target = "Lệnh Chú", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred },
                new() { Source = "Grand Order", Target = "Grand Order", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Chaldea", Target = "Chaldea", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "SHEBA", Target = "SHEBA", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Heroic Spirit", Target = "Anh Linh", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred },
                new() { Source = "Mystic Code", Target = "Lễ Trang", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred },
                new() { Source = "Summoning", Target = "Triệu hồi", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred },
                new() { Source = "Berserker", Target = "Berserker", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Saber", Target = "Saber", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Archer", Target = "Archer", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Lancer", Target = "Lancer", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Rider", Target = "Rider", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Caster", Target = "Caster", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Assassin", Target = "Assassin", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Ruler", Target = "Ruler", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Avenger", Target = "Avenger", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Foreigner", Target = "Foreigner", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Alter Ego", Target = "Alter Ego", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Moon Cancer", Target = "Moon Cancer", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Pretender", Target = "Pretender", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Beast", Target = "Beast", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                new() { Source = "Demi-Servant", Target = "Á Servant", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred },
                new() { Source = "Pseudo-Servant", Target = "Giả Servant", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred },
                new() { Source = "Counter Force", Target = "Sức Mạnh Phản Kháng", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred },
                new() { Source = "Phantasm", Target = "Bảo Khí", Tier = GlossaryTier.Game, Level = GlossaryLevel.Preferred }
            };
        }
    }
}
