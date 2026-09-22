using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TranslateBot.Dialogue;
using TranslateBot.Infrastructure;
using TranslateBot.OCR;
using TranslateBot.OCR.Preprocessing;
using TranslateBot.Translation;

namespace TranslateBot.GameProfiles
{
    /// <summary>
    /// Bộ quản lý hồ sơ Game (Section 15 của Master Plan)
    /// Hỗ trợ nạp, lưu trữ, phát hiện tự động và kích hoạt hồ sơ game cho TranslationEngine.
    /// </summary>
    public class GameProfileManager
    {
        private readonly string _profilesDirectory;
        private readonly List<GameProfile> _profiles = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() }
        };

        public string ProfilesDirectory => _profilesDirectory;
        public IReadOnlyList<GameProfile> Profiles => _profiles.AsReadOnly();
        public GameProfile? ActiveProfile { get; private set; }

        public GameProfileManager(string? profilesDirectory = null)
        {
            _profilesDirectory = profilesDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
        }

        /// <summary>
        /// Nạp tất cả hồ sơ từ thư mục profiles/. Nếu chưa có file nào, tự sinh các hồ sơ mẫu.
        /// </summary>
        public void LoadAllProfiles()
        {
            try
            {
                if (!Directory.Exists(_profilesDirectory))
                {
                    Directory.CreateDirectory(_profilesDirectory);
                }

                var files = Directory.GetFiles(_profilesDirectory, "*.json");
                if (files.Length == 0)
                {
                    AppLogger.Info("[PROFILE_MGR] Chưa có hồ sơ game nào, tiến hành khởi tạo bộ hồ sơ mặc định.");
                    var defaults = CreateDefaultProfiles();
                    foreach (var p in defaults)
                    {
                        SaveProfile(p);
                    }
                    return;
                }

                _profiles.Clear();
                foreach (var file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var profile = JsonSerializer.Deserialize<GameProfile>(json, JsonOpts);
                        if (profile != null)
                        {
                            _profiles.Add(profile);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"[PROFILE_MGR] Lỗi đọc file hồ sơ {file}: {ex.Message}");
                    }
                }

                AppLogger.Info($"[PROFILE_MGR] Đã nạp thành công {_profiles.Count} hồ sơ game từ {_profilesDirectory}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[PROFILE_MGR] Lỗi khi nạp danh sách hồ sơ: {ex.Message}");
            }
        }

        /// <summary>
        /// Lưu một hồ sơ game ra file JSON
        /// </summary>
        public void SaveProfile(GameProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            if (!Directory.Exists(_profilesDirectory))
            {
                Directory.CreateDirectory(_profilesDirectory);
            }

            string safeFileName = string.Join("_", profile.Id.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                safeFileName = Guid.NewGuid().ToString("N")[..8];
            }

            string filePath = Path.Combine(_profilesDirectory, $"{safeFileName}.json");
            string json = JsonSerializer.Serialize(profile, JsonOpts);
            File.WriteAllText(filePath, json);

            int existingIdx = _profiles.FindIndex(p => p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIdx >= 0)
            {
                _profiles[existingIdx] = profile;
            }
            else
            {
                _profiles.Add(profile);
            }

            AppLogger.Info($"[PROFILE_SAVED] \"{profile.Name}\" -> {filePath}");
        }

        /// <summary>
        /// Xóa một hồ sơ game theo Id
        /// </summary>
        public bool DeleteProfile(string id)
        {
            int idx = _profiles.FindIndex(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                var profile = _profiles[idx];
                _profiles.RemoveAt(idx);

                string safeFileName = string.Join("_", profile.Id.Split(Path.GetInvalidFileNameChars()));
                string filePath = Path.Combine(_profilesDirectory, $"{safeFileName}.json");
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"[PROFILE_MGR] Không thể xóa file hồ sơ: {ex.Message}");
                    }
                }

                if (ActiveProfile?.Id == id)
                {
                    ActiveProfile = null;
                }

                return true;
            }
            return false;
        }

        /// <summary>
        /// Tìm hồ sơ theo Id hoặc Tên
        /// </summary>
        public GameProfile? GetProfile(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return null;

            return _profiles.FirstOrDefault(p => p.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase))
                   ?? _profiles.FirstOrDefault(p => p.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Áp dụng hồ sơ game vào TranslationEngine hiện thời
        /// </summary>
        public void ApplyProfile(GameProfile profile, TranslationEngine engine)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            ActiveProfile = profile;

            // 1. Cấu hình OCR Area & Exclusion Area
            engine.OcrAreaManager.LoadFrom(profile.OcrAreas);
            engine.ExclusionManager.LoadFrom(profile.ExclusionAreas);

            // 2. Cấu hình Nhân vật & Thuật ngữ
            engine.CharacterProfileManager.LoadFrom(profile.Characters);
            engine.GlossaryManager.LoadFrom(profile.Glossary);

            // 3. Cấu hình Ngữ cảnh Game và Phân cảnh (Scene)
            engine.FgoContextEngine.GameName = profile.GameName;

            var activeScene = profile.GetActiveScene();
            if (activeScene != null)
            {
                engine.FgoContextEngine.SceneContext = !string.IsNullOrWhiteSpace(activeScene.Description)
                    ? $"{activeScene.Name}: {activeScene.Description}"
                    : activeScene.Name;
                engine.FgoContextEngine.SceneGlossaryOverrides = activeScene.GlossaryOverrides;
            }
            else
            {
                engine.FgoContextEngine.SceneContext = null;
                engine.FgoContextEngine.SceneGlossaryOverrides = null;
            }

            // 4. Cấu hình bộ ổn định thoại theo Preset
            engine.DialogueTracker.Config = StabilityConfig.FromPreset(profile.StabilityPreset);

            // 5. Cập nhật reference hồ sơ trên TranslationEngine
            engine.SetCurrentGameProfile(profile);

            AppLogger.Info($"[GAME_PROFILE_APPLIED] \"{profile.Name}\" ({profile.GameName}) - {profile.OcrAreas.Count} OCR areas, {profile.Characters.Count} characters, {profile.Glossary.Count} glossary entries, Scene: {activeScene?.Name ?? "None"}");
        }

        /// <summary>
        /// Chuyển đổi phân cảnh (Scene) trong hồ sơ đang hoạt động
        /// </summary>
        public void SwitchScene(string sceneName, TranslationEngine engine)
        {
            if (ActiveProfile == null || engine == null) return;

            ActiveProfile.ActiveSceneName = sceneName;
            var scene = ActiveProfile.GetActiveScene();

            if (scene != null)
            {
                engine.FgoContextEngine.SceneContext = !string.IsNullOrWhiteSpace(scene.Description)
                    ? $"{scene.Name}: {scene.Description}"
                    : scene.Name;
                engine.FgoContextEngine.SceneGlossaryOverrides = scene.GlossaryOverrides;

                AppLogger.Info($"[SCENE_SWITCHED] Đã chuyển sang phân cảnh: \"{scene.Name}\"");
            }
        }

        /// <summary>
        /// Tạo danh sách các hồ sơ mẫu chuẩn hoá theo Master Plan
        /// </summary>
        public static List<GameProfile> CreateDefaultProfiles()
        {
            return new List<GameProfile>
            {
                CreateFgoJpProfile(),
                CreateFgoNaProfile(),
                CreateBlueArchiveProfile(),
                CreateVisualNovelProfile()
            };
        }

        private static GameProfile CreateFgoJpProfile()
        {
            return new GameProfile
            {
                Id = "fgo-jp",
                Name = "Fate/Grand Order (JP)",
                GameName = "Fate/Grand Order",
                ProcessMatch = "HD-Player|Nox|MuMu|FateGO",
                TitleMatch = "Fate/Grand Order|FateGO|FGO",
                SourceLanguage = "ja",
                TargetLanguage = "vi",
                StabilityPreset = StabilityPreset.Balanced,
                PreprocessPreset = PreprocessPreset.FgoDialogue,
                PreferredOcrEngine = "WindowsOCR",
                OcrAreas = new List<OcrArea>
                {
                    new()
                    {
                        Id = "fgo-dialogue",
                        Name = "Hộp thoại chính",
                        Type = OcrAreaType.Dialogue,
                        RegionX = 0,
                        RegionY = 700,
                        RegionWidth = 1280,
                        RegionHeight = 220,
                        Preset = PreprocessPreset.FgoDialogue
                    },
                    new()
                    {
                        Id = "fgo-speaker",
                        Name = "Tên nhân vật",
                        Type = OcrAreaType.Speaker,
                        RegionX = 180,
                        RegionY = 650,
                        RegionWidth = 320,
                        RegionHeight = 50,
                        Preset = PreprocessPreset.FgoSpeaker
                    }
                },
                ExclusionAreas = new List<ExclusionArea>
                {
                    new()
                    {
                        Id = "fgo-skip-btn",
                        Name = "Nút Skip & Menu",
                        RegionX = 1100,
                        RegionY = 10,
                        RegionWidth = 180,
                        RegionHeight = 60
                    }
                },
                Characters = new List<CharacterProfile>
                {
                    new()
                    {
                        Name = "Mash Kyrielight",
                        Aliases = new List<string> { "Mash", "Mashu", "Shielder", "マシュ" },
                        SpeakingStyle = "Lịch sự, nhẹ nhàng, kính ngữ. Giọng điệu chân thành, tận tụy vì Master.",
                        PreferredPronouns = "em / Senpai",
                        AddressingRules = "Gọi Ritsuka là 'Senpai'. Xưng 'em'."
                    },
                    new()
                    {
                        Name = "Ritsuka Fujimaru",
                        Aliases = new List<string> { "Master", "Gudao", "Gudako", "藤丸立香" },
                        SpeakingStyle = "Thân thiện, dũng cảm, quyết đoán nhưng gần gũi.",
                        PreferredPronouns = "tôi / cậu"
                    },
                    new()
                    {
                        Name = "Leonardo da Vinci",
                        Aliases = new List<string> { "Da Vinci", "Da Vinci-chan", "ダ・ヴィンチ" },
                        SpeakingStyle = "Vui tươi, tự tin, thông thái và dí dỏm.",
                        PreferredPronouns = "tôi / cậu",
                        AddressingRules = "Gọi Master là 'Master-kun'."
                    },
                    new()
                    {
                        Name = "Romani Archaman",
                        Aliases = new List<string> { "Roman", "Dr. Roman", "ロマニ" },
                        SpeakingStyle = "Ấm áp, chu đáo, đôi khi nhút nhát và lo lắng.",
                        PreferredPronouns = "tôi / cậu"
                    },
                    new()
                    {
                        Name = "Gilgamesh",
                        Aliases = new List<string> { "Gil", "King of Heroes", "AUO", "ギルガメッシュ" },
                        SpeakingStyle = "Ngạo nghễ, trịch thượng, tự xưng là Vua.",
                        PreferredPronouns = "ta / ngươi"
                    }
                },
                Glossary = new List<GlossaryEntry>
                {
                    new() { Source = "Master", Target = "Master", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Servant", Target = "Servant", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Senpai", Target = "tiền bối", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Noble Phantasm", Target = "Bảo Khí", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Saint Quartz", Target = "Thánh Tinh Thạch", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Chaldea", Target = "Chaldea", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Holy Grail", Target = "Chén Thánh", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Command Spells", Target = "Lệnh Chú", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Singularity", Target = "Điểm Đặc Dị", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Lostbelt", Target = "Dị Văn Đới", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked }
                },
                Scenes = new List<SceneProfile>
                {
                    new()
                    {
                        Name = "Chaldea",
                        Description = "Căn cứ Tổ chức Bảo tồn Trật tự Nhân lý Chaldea, không khí thường nhật ấm áp và thảo luận chiến thuật.",
                        SpeakingStyleContext = "Thường nhật, nhẹ nhàng, đầm ấm",
                        ActiveCharacters = new List<string> { "Mash Kyrielight", "Leonardo da Vinci", "Romani Archaman" }
                    },
                    new()
                    {
                        Name = "Lostbelt",
                        Description = "Bối cảnh Dị Văn Đới hoang tàn, thế giới bị đào thải đầy nguy hiểm, căng thẳng sinh tồn.",
                        SpeakingStyleContext = "Bi tráng, nghiêm nghị, cảnh giác",
                        ActiveCharacters = new List<string> { "Mash Kyrielight", "Ritsuka Fujimaru" },
                        GlossaryOverrides = new List<GlossaryEntry>
                        {
                            new() { Source = "Tree of Emptiness", Target = "Cây Hư Không", Tier = GlossaryTier.Story, Level = GlossaryLevel.Locked }
                        }
                    }
                }
            };
        }

        private static GameProfile CreateFgoNaProfile()
        {
            var p = CreateFgoJpProfile();
            p.Id = "fgo-na";
            p.Name = "Fate/Grand Order (NA)";
            p.SourceLanguage = "en";
            return p;
        }

        private static GameProfile CreateBlueArchiveProfile()
        {
            return new GameProfile
            {
                Id = "blue-archive",
                Name = "Blue Archive",
                GameName = "Blue Archive",
                ProcessMatch = "HD-Player|Nox|MuMu|BlueArchive",
                TitleMatch = "Blue Archive",
                SourceLanguage = "ja",
                TargetLanguage = "vi",
                StabilityPreset = StabilityPreset.Fast,
                PreprocessPreset = PreprocessPreset.WhiteTextOnDark,
                PreferredOcrEngine = "WindowsOCR",
                OcrAreas = new List<OcrArea>
                {
                    new()
                    {
                        Id = "ba-dialogue",
                        Name = "Khung thoại Blue Archive",
                        Type = OcrAreaType.Dialogue,
                        RegionX = 0,
                        RegionY = 720,
                        RegionWidth = 1280,
                        RegionHeight = 200,
                        Preset = PreprocessPreset.WhiteTextOnDark
                    }
                },
                Characters = new List<CharacterProfile>
                {
                    new()
                    {
                        Name = "Arona",
                        Aliases = new List<string> { "アロナ" },
                        SpeakingStyle = "Vui vẻ, nhí nhảnh, giọng điệu AI trợ lý dễ thương, tận tâm vì Thầy.",
                        PreferredPronouns = "em / Sensei",
                        AddressingRules = "Gọi người chơi là Sensei."
                    },
                    new()
                    {
                        Name = "Plana",
                        Aliases = new List<string> { "プラナ" },
                        SpeakingStyle = "Điềm tĩnh, ít nói, ngoan ngoãn và trung thành.",
                        PreferredPronouns = "em / Sensei"
                    },
                    new()
                    {
                        Name = "Yuuka",
                        Aliases = new List<string> { "Hayase Yuuka", "ユウカ" },
                        SpeakingStyle = "Nghiêm túc, trách nhiệm, hay cằn nhằn về chi tiêu nhưng quan tâm chân thành.",
                        PreferredPronouns = "tôi / Sensei"
                    }
                },
                Glossary = new List<GlossaryEntry>
                {
                    new() { Source = "Sensei", Target = "Thầy", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Kivotos", Target = "Kivotos", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Schale", Target = "Schale", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Halo", Target = "vòng hào quang", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked },
                    new() { Source = "Mystic", Target = "thần bí", Tier = GlossaryTier.Game, Level = GlossaryLevel.Locked }
                },
                Scenes = new List<SceneProfile>
                {
                    new()
                    {
                        Name = "Schale Office",
                        Description = "Văn phòng câu lạc bộ liên bang Schale, gặp gỡ và hướng dẫn các học sinh.",
                        SpeakingStyleContext = "Hài hước, gần gũi, đời thường",
                        ActiveCharacters = new List<string> { "Arona", "Plana", "Yuuka" }
                    },
                    new()
                    {
                        Name = "Eden Treaty",
                        Description = "Hiệp ước Eden, xung đột chính trị và trận chiến căng thẳng giữa các học viện.",
                        SpeakingStyleContext = "Kịch tính, căng thẳng, trang trọng"
                    }
                }
            };
        }

        private static GameProfile CreateVisualNovelProfile()
        {
            return new GameProfile
            {
                Id = "vn-generic",
                Name = "Visual Novel (Generic)",
                GameName = "Visual Novel",
                ProcessMatch = string.Empty,
                TitleMatch = string.Empty,
                SourceLanguage = "ja",
                TargetLanguage = "vi",
                StabilityPreset = StabilityPreset.Safe,
                PreprocessPreset = PreprocessPreset.VnDialogue,
                PreferredOcrEngine = "WindowsOCR",
                OcrAreas = new List<OcrArea>
                {
                    new()
                    {
                        Id = "vn-dialogue",
                        Name = "Khung thoại Visual Novel",
                        Type = OcrAreaType.Dialogue,
                        RegionX = 0,
                        RegionY = 650,
                        RegionWidth = 1280,
                        RegionHeight = 250,
                        Preset = PreprocessPreset.VnDialogue
                    }
                }
            };
        }
    }
}
