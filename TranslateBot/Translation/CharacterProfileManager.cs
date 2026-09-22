using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TranslateBot.Infrastructure;

namespace TranslateBot.Translation
{
    // Quản lý hồ sơ nhân vật (Section 27). Load từ characters.json,
    // tìm profile bằng tên hoặc alias khi speaker được nhận dạng.
    public class CharacterProfileManager
    {
        private static readonly string ProfilesPath = "characters.json";
        private List<CharacterProfile> _profiles = new();

        public IReadOnlyList<CharacterProfile> Profiles => _profiles.AsReadOnly();

        public void Load()
        {
            if (!File.Exists(ProfilesPath))
            {
                AppLogger.Info("[CHARACTERS] characters.json không tồn tại, tự động khởi tạo profiles mặc định");
                _profiles = GetDefaultProfiles();
                Save();
                return;
            }

            try
            {
                string json = File.ReadAllText(ProfilesPath);
                _profiles = JsonConvert.DeserializeObject<List<CharacterProfile>>(json) ?? new();
                AppLogger.Info($"[CHARACTERS_LOADED] {_profiles.Count} profiles từ {ProfilesPath}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[CHARACTERS_ERROR] Lỗi đọc profiles: {ex.Message}");
            }
        }

        private static List<CharacterProfile> GetDefaultProfiles()
        {
            return new List<CharacterProfile>
            {
                new CharacterProfile
                {
                    Name = "Mash Kyrielight",
                    Aliases = new List<string> { "Mash", "Mashu", "Mashu Kyrielight", "Shielder", "マシュ" },
                    SpeakingStyle = "Lịch sự, nhẹ nhàng, hay dùng kính ngữ. Giọng điệu chân thành, lo lắng cho Master.",
                    PreferredPronouns = "em / Senpai",
                    AddressingRules = "Gọi Ritsuka là 'Senpai'. Gọi Da Vinci là 'Da Vinci-san'. Xưng 'em' với tất cả."
                },
                new CharacterProfile
                {
                    Name = "Ritsuka Fujimaru",
                    Aliases = new List<string> { "Ritsuka", "Fujimaru", "Master", "Gudao", "Gudako" },
                    SpeakingStyle = "Thân thiện, quyết đoán, dũng cảm. Giọng điệu bình thường, không quá trang trọng.",
                    PreferredPronouns = "tôi / cậu / bạn",
                    AddressingRules = "Gọi Mash là 'Mash'. Gọi Servant bằng tên."
                },
                new CharacterProfile
                {
                    Name = "Gilgamesh",
                    Aliases = new List<string> { "Gil", "King of Heroes", "AUO", "ギルガメッシュ" },
                    SpeakingStyle = "Ngạo nghễ, kiêu căng, tự xưng là Vua. Giọng điệu trịch thượng, coi thường người khác.",
                    PreferredPronouns = "ta / ngươi / đồ phàm nhân",
                    AddressingRules = "Gọi mọi người là 'ngươi' hoặc 'đồ phàm nhân'. Tự xưng 'ta' hoặc 'Vua'."
                },
                new CharacterProfile
                {
                    Name = "Artoria Pendragon",
                    Aliases = new List<string> { "Artoria", "Saber", "King Arthur", "Altria", "アルトリア" },
                    SpeakingStyle = "Trang trọng, đĩnh đạc, phong cách hiệp sĩ. Giữ khoảng cách lịch sự.",
                    PreferredPronouns = "tôi / ngài",
                    AddressingRules = "Xưng 'tôi'. Gọi Master là 'Master'."
                },
                new CharacterProfile
                {
                    Name = "Da Vinci",
                    Aliases = new List<string> { "Leonardo da Vinci", "Da Vinci-chan", "ダ・ヴィンチ" },
                    SpeakingStyle = "Vui vẻ, tự tin, đôi khi nghịch ngợm. Thích khoe khoang tài năng.",
                    PreferredPronouns = "tôi / cậu",
                    AddressingRules = "Gọi Ritsuka là 'Master-kun'. Tự xưng là 'thiên tài da Vinci này'."
                },
                new CharacterProfile
                {
                    Name = "Romani Archaman",
                    Aliases = new List<string> { "Romani", "Roman", "Dr. Roman", "ロマニ" },
                    SpeakingStyle = "Hiền lành, lo lắng, đôi khi nhút nhát. Giọng điệu ấm áp.",
                    PreferredPronouns = "tôi / cậu",
                    AddressingRules = "Gọi Ritsuka là 'Fujimaru-kun'. Gọi Mash là 'Mash'."
                }
            };
        }

        public void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_profiles, Formatting.Indented);
                File.WriteAllText(ProfilesPath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[CHARACTERS_ERROR] Lỗi lưu profiles: {ex.Message}");
            }
        }

        // Tìm profile bằng tên chính xác hoặc alias (case-insensitive).
        // Sắp xếp theo Priority giảm dần để chọn profile ưu tiên nhất.
        public CharacterProfile? FindBySpeakerName(string speakerName, string? currentContext = null)
        {
            if (string.IsNullOrWhiteSpace(speakerName)) return null;

            string normalized = speakerName.Trim();

            var matches = _profiles
                .Where(p =>
                    p.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                    p.Aliases.Any(a => a.Equals(normalized, StringComparison.OrdinalIgnoreCase)));

            if (!string.IsNullOrEmpty(currentContext))
            {
                // Ưu tiên profile có match ngữ cảnh nếu có nhiều profile
                matches = matches.OrderByDescending(p => p.Contexts.Any(c => c.Equals(currentContext, StringComparison.OrdinalIgnoreCase)));
            }

            return matches
                .OrderByDescending(p => p.Priority)
                .FirstOrDefault();
        }

        public void Add(CharacterProfile profile) => _profiles.Add(profile);
        public void Clear() => _profiles.Clear();

        public void LoadFrom(IEnumerable<CharacterProfile>? profiles)
        {
            _profiles.Clear();
            if (profiles != null)
            {
                _profiles.AddRange(profiles);
            }
        }

        public List<CharacterProfile> ToList() => new(_profiles);
    }
}
