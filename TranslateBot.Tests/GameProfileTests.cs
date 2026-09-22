using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Capture;
using TranslateBot.Dialogue;
using TranslateBot.GameProfiles;
using TranslateBot.Infrastructure;
using TranslateBot.OCR;
using TranslateBot.OCR.Preprocessing;
using TranslateBot.Translation;

namespace TranslateBot.Tests
{
    [TestClass]
    public class GameProfileTests
    {
        private class DummyOcrEngine : IOcrEngine
        {
            public Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height)
            {
                return Task.FromResult(new OcrResult { Text = "Dummy Text", IsTextClipped = false });
            }
        }

        private TranslationEngine CreateTestEngine()
        {
            var capture = new CaptureService();
            var changeDetector = new FrameChangeDetector();
            var ocr = new DummyOcrEngine();
            var tracker = new DialogueTracker();
            var fakeProvider = new FakeTranslationProvider();
            var worker = new TranslationWorker(fakeProvider);

            return new TranslationEngine(capture, changeDetector, ocr, tracker, worker);
        }

        [TestMethod]
        public void GameProfile_SerializationRoundtrip_MaintainsDataIntegrity()
        {
            var profile = new GameProfile
            {
                Id = "test-rpg",
                Name = "Test RPG Odyssey",
                GameName = "RPG Odyssey",
                ProcessMatch = "rpg_engine|rpg.exe",
                TitleMatch = "RPG Odyssey",
                SourceLanguage = "ja",
                TargetLanguage = "vi",
                StabilityPreset = StabilityPreset.Safe,
                PreprocessPreset = PreprocessPreset.VnDialogue,
                PreferredOcrEngine = "Tesseract",
                OcrAreas = new List<OcrArea>
                {
                    new() { Id = "d1", Name = "Main Box", Type = OcrAreaType.Dialogue, RegionX = 10, RegionY = 20, RegionWidth = 300, RegionHeight = 80 },
                    new() { Id = "s1", Name = "Speaker", Type = OcrAreaType.Speaker, RegionX = 10, RegionY = 5, RegionWidth = 100, RegionHeight = 20 }
                },
                ExclusionAreas = new List<ExclusionArea>
                {
                    new() { Id = "e1", Name = "Menu", RegionX = 500, RegionY = 0, RegionWidth = 50, RegionHeight = 30 }
                },
                Characters = new List<CharacterProfile>
                {
                    new() { Name = "Hero", PreferredPronouns = "tôi / bạn", SpeakingStyle = "Dũng cảm" }
                },
                Glossary = new List<GlossaryEntry>
                {
                    new() { Source = "Potion", Target = "Thuốc hồi máu", Level = GlossaryLevel.Locked }
                },
                Scenes = new List<SceneProfile>
                {
                    new() { Name = "Town", Description = "Thị trấn yên bình" },
                    new() { Name = "Dungeon", Description = "Hầm ngục tối", GlossaryOverrides = new List<GlossaryEntry> { new() { Source = "Boss", Target = "Trùm" } } }
                },
                ActiveSceneName = "Dungeon"
            };

            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };

            string json = JsonSerializer.Serialize(profile, opts);
            var deserialized = JsonSerializer.Deserialize<GameProfile>(json, opts);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual("test-rpg", deserialized.Id);
            Assert.AreEqual("Test RPG Odyssey", deserialized.Name);
            Assert.AreEqual(StabilityPreset.Safe, deserialized.StabilityPreset);
            Assert.AreEqual(PreprocessPreset.VnDialogue, deserialized.PreprocessPreset);
            Assert.AreEqual(2, deserialized.OcrAreas.Count);
            Assert.AreEqual(1, deserialized.ExclusionAreas.Count);
            Assert.AreEqual(1, deserialized.Characters.Count);
            Assert.AreEqual("Hero", deserialized.Characters[0].Name);
            Assert.AreEqual(1, deserialized.Glossary.Count);
            Assert.AreEqual(2, deserialized.Scenes.Count);
            Assert.AreEqual("Dungeon", deserialized.GetActiveScene()?.Name);
            Assert.AreEqual("Trùm", deserialized.GetActiveScene()?.GlossaryOverrides[0].Target);
        }

        [TestMethod]
        public void GameDetector_MatchesWindowByTitleAndProcess()
        {
            var profiles = GameProfileManager.CreateDefaultProfiles();

            var fgoWindow = new WindowInfo
            {
                Handle = new IntPtr(100),
                Title = "Fate/Grand Order - Lostbelt 6",
                ProcessName = "HD-Player"
            };

            var baWindow = new WindowInfo
            {
                Handle = new IntPtr(200),
                Title = "Blue Archive",
                ProcessName = "HD-Player"
            };

            var notepadWindow = new WindowInfo
            {
                Handle = new IntPtr(300),
                Title = "Notes.txt - Notepad",
                ProcessName = "notepad"
            };

            // FGO matching
            var (bestFgo, winFgo) = GameDetector.DetectFromWindows(profiles, new[] { fgoWindow, notepadWindow });
            Assert.IsNotNull(bestFgo);
            Assert.AreEqual("Fate/Grand Order", bestFgo.GameName);
            Assert.AreEqual(fgoWindow.Handle, winFgo?.Handle);

            // Blue Archive matching (same emulator process HD-Player, but different title!)
            var (bestBa, winBa) = GameDetector.DetectFromWindows(profiles, new[] { baWindow, notepadWindow });
            Assert.IsNotNull(bestBa);
            Assert.AreEqual("Blue Archive", bestBa.GameName);
            Assert.AreEqual(baWindow.Handle, winBa?.Handle);

            // No match
            var (none, winNone) = GameDetector.DetectFromWindows(profiles, new[] { notepadWindow });
            Assert.IsNull(none);
            Assert.IsNull(winNone);
        }

        [TestMethod]
        public void GameProfileManager_AutoGeneratesDefaults_WhenDirectoryEmpty()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "tbco_profiles_" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new GameProfileManager(tempDir);
                manager.LoadAllProfiles();

                Assert.AreEqual(4, manager.Profiles.Count);
                Assert.IsTrue(File.Exists(Path.Combine(tempDir, "fgo-jp.json")));
                Assert.IsTrue(File.Exists(Path.Combine(tempDir, "fgo-na.json")));
                Assert.IsTrue(File.Exists(Path.Combine(tempDir, "blue-archive.json")));
                Assert.IsTrue(File.Exists(Path.Combine(tempDir, "vn-generic.json")));

                var fgo = manager.GetProfile("fgo-jp");
                Assert.IsNotNull(fgo);
                Assert.AreEqual("Fate/Grand Order (JP)", fgo.Name);

                var ba = manager.GetProfile("Blue Archive");
                Assert.IsNotNull(ba);
                Assert.AreEqual("blue-archive", ba.Id);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        public void GameProfileManager_Save_Get_Delete_Operations()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "tbco_profiles_crud_" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new GameProfileManager(tempDir);
                var custom = new GameProfile
                {
                    Id = "custom-game-1",
                    Name = "Custom Tactical RPG",
                    GameName = "Tactical RPG"
                };

                manager.SaveProfile(custom);
                Assert.AreEqual(1, manager.Profiles.Count);
                Assert.IsTrue(File.Exists(Path.Combine(tempDir, "custom-game-1.json")));

                var retrieved = manager.GetProfile("custom-game-1");
                Assert.IsNotNull(retrieved);
                Assert.AreEqual("Custom Tactical RPG", retrieved.Name);

                bool deleted = manager.DeleteProfile("custom-game-1");
                Assert.IsTrue(deleted);
                Assert.AreEqual(0, manager.Profiles.Count);
                Assert.IsFalse(File.Exists(Path.Combine(tempDir, "custom-game-1.json")));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        public void GameProfileManager_ApplyProfile_CorrectlyConfiguresTranslationEngine()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "tbco_profiles_apply_" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new GameProfileManager(tempDir);
                manager.LoadAllProfiles();

                var fgoProfile = manager.GetProfile("fgo-jp");
                Assert.IsNotNull(fgoProfile);

                var engine = CreateTestEngine();

                manager.ApplyProfile(fgoProfile, engine);

                Assert.AreEqual(fgoProfile, engine.CurrentGameProfile);
                Assert.AreEqual(fgoProfile, manager.ActiveProfile);

                // Check OCR & Exclusion areas
                Assert.AreEqual(fgoProfile.OcrAreas.Count, engine.OcrAreaManager.Areas.Count);
                Assert.AreEqual(fgoProfile.ExclusionAreas.Count, engine.ExclusionManager.Exclusions.Count);

                // Check Characters & Glossary
                Assert.AreEqual(fgoProfile.Characters.Count, engine.CharacterProfileManager.Profiles.Count);
                Assert.AreEqual(fgoProfile.Glossary.Count, engine.GlossaryManager.Entries.Count);

                // Check Game name & Stability
                Assert.AreEqual("Fate/Grand Order", engine.FgoContextEngine.GameName);
                Assert.AreEqual(StabilityPreset.Balanced, engine.DialogueTracker.Config.Preset);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        public void GameProfileManager_SceneSwitch_UpdatesContextAndOverrides()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "tbco_profiles_scene_" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new GameProfileManager(tempDir);
                manager.LoadAllProfiles();

                var fgoProfile = manager.GetProfile("fgo-jp");
                Assert.IsNotNull(fgoProfile);

                var engine = CreateTestEngine();
                manager.ApplyProfile(fgoProfile, engine);

                // Default active scene is first scene ("Chaldea")
                Assert.IsTrue(engine.FgoContextEngine.SceneContext?.Contains("Chaldea") == true);

                // Switch to "Lostbelt" scene
                manager.SwitchScene("Lostbelt", engine);

                Assert.IsTrue(engine.FgoContextEngine.SceneContext?.Contains("Lostbelt") == true);
                Assert.IsNotNull(engine.FgoContextEngine.SceneGlossaryOverrides);
                Assert.AreEqual(1, engine.FgoContextEngine.SceneGlossaryOverrides.Count);
                Assert.AreEqual("Tree of Emptiness", engine.FgoContextEngine.SceneGlossaryOverrides[0].Source);

                // Verify BuildContext applies the scene's glossary overrides
                var ctx = engine.FgoContextEngine.BuildContext("Cẩn thận với Tree of Emptiness phía trước!");
                var treeEntry = ctx.ActiveGlossary.FirstOrDefault(g => g.Source == "Tree of Emptiness");

                Assert.IsNotNull(treeEntry);
                Assert.AreEqual("Cây Hư Không", treeEntry.Target);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        public void GameProfile_GetActiveScene_FallbackBehavior()
        {
            var profile = new GameProfile
            {
                Scenes = new List<SceneProfile>
                {
                    new() { Name = "SceneA" },
                    new() { Name = "SceneB" }
                }
            };

            // Default: first scene
            Assert.AreEqual("SceneA", profile.GetActiveScene()?.Name);

            // Set specific scene
            profile.ActiveSceneName = "SceneB";
            Assert.AreEqual("SceneB", profile.GetActiveScene()?.Name);

            // Set invalid scene -> fallback to first
            profile.ActiveSceneName = "NonExistent";
            Assert.AreEqual("SceneA", profile.GetActiveScene()?.Name);

            // Empty scenes -> null
            profile.Scenes.Clear();
            Assert.IsNull(profile.GetActiveScene());
        }
    }
}
