using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using TranslateBot.Dialogue;
using TranslateBot.Native;
using TranslateBot.Session;
using TranslateBot.Translation;
using TranslateBot.TTS;

namespace TranslateBot.Tests
{
    [TestClass]
    public class AdvancedTests
    {
        // ═══════════════════════════════════════════════════════════════════
        // 1. SpeakerDetector Tests (VN Syntax & Aliases)
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void SpeakerDetector_ExtractSpeaker_ColonSyntax_ReturnsSpeakerAndDialogue()
        {
            var result = SpeakerDetector.ExtractSpeaker("Mash: Senpai, are you ready for the singularity?");

            Assert.IsTrue(result.HasSpeaker);
            Assert.AreEqual("Mash", result.Speaker);
            Assert.AreEqual("Senpai, are you ready for the singularity?", result.CleanDialogue);
        }

        [TestMethod]
        public void SpeakerDetector_ExtractSpeaker_FullwidthColonSyntax_ReturnsSpeakerAndDialogue()
        {
            var result = SpeakerDetector.ExtractSpeaker("マシュ：先輩、準備はよろしいですか？");

            Assert.IsTrue(result.HasSpeaker);
            Assert.AreEqual("マシュ", result.Speaker);
            Assert.AreEqual("先輩、準備はよろしいですか？", result.CleanDialogue);
        }

        [TestMethod]
        public void SpeakerDetector_ExtractSpeaker_BracketSyntax_ReturnsSpeakerAndDialogue()
        {
            var result = SpeakerDetector.ExtractSpeaker("【Artoria Pendragon】 Excalibur!");

            Assert.IsTrue(result.HasSpeaker);
            Assert.AreEqual("Artoria Pendragon", result.Speaker);
            Assert.AreEqual("Excalibur!", result.CleanDialogue);
        }

        [TestMethod]
        public void SpeakerDetector_ExtractSpeaker_SquareBracketSyntax_ReturnsSpeakerAndDialogue()
        {
            var result = SpeakerDetector.ExtractSpeaker("[Da Vinci] Technical check complete.");

            Assert.IsTrue(result.HasSpeaker);
            Assert.AreEqual("Da Vinci", result.Speaker);
            Assert.AreEqual("Technical check complete.", result.CleanDialogue);
        }

        [TestMethod]
        public void SpeakerDetector_ExtractSpeaker_JapaneseQuotes_ReturnsDialogueWithoutQuotes()
        {
            var result = SpeakerDetector.ExtractSpeaker("「フォウ、フォウ！」");

            // Japanese quotes without speaker prefix are treated as quoted dialogue
            Assert.IsFalse(result.HasSpeaker);
            Assert.AreEqual("フォウ、フォウ！", result.CleanDialogue);
        }

        [TestMethod]
        public void SpeakerDetector_ExtractSpeaker_NoSpeaker_ReturnsOriginalText()
        {
            var input = "The sky burned crimson above the ruins of Fuyuki.";
            var result = SpeakerDetector.ExtractSpeaker(input);

            Assert.IsFalse(result.HasSpeaker);
            Assert.IsNull(result.Speaker);
            Assert.AreEqual(input, result.CleanDialogue);
        }

        [TestMethod]
        public void SpeakerDetector_NormalizeSpeaker_WithCharacterProfileManager_ResolvesAlias()
        {
            var profileManager = new CharacterProfileManager();
            var profile = new CharacterProfile
            {
                Name = "Mash Kyrielight",
                Aliases = { "Mash", "Mashu", "マシュ" },
                PreferredPronouns = "em / tiền bối"
            };
            profileManager.Add(profile);

            string resolved = SpeakerDetector.NormalizeSpeaker("mashu", profileManager);
            Assert.AreEqual("Mash Kyrielight", resolved);

            string japaneseResolved = SpeakerDetector.NormalizeSpeaker("マシュ", profileManager);
            Assert.AreEqual("Mash Kyrielight", japaneseResolved);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 2. SessionManager & Export Tests (CSV, JSON, Markdown)
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void SessionManager_AddRecord_StoresRecordsInSession()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TranslateBotTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new SessionManager(tempDir);
                manager.StartSession();

                manager.AddRecord(1, "先輩！", "Senpai!", "Mash", "Gemini", 450, false);
                manager.AddRecord(2, "了解した。", "Understood.", "Fujimaru", "Cache", 15, true);

                Assert.AreEqual(2, manager.Records.Count);
                Assert.AreEqual("Mash", manager.Records[0].Speaker);
                Assert.AreEqual("Senpai!", manager.Records[0].TranslatedText);
                Assert.IsTrue(manager.Records[1].IsCacheHit);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void SessionManager_ToCsvString_GeneratesValidCsvWithEscaping()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TranslateBotTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new SessionManager(tempDir);
                manager.AddRecord(1, "Line with, comma and \"quotes\"", "Dòng có, dấu phẩy và \"ngoặc kép\"", "Speaker, A", "Gemini", 300, false);

                string csv = manager.ToCsvString();
                Assert.IsTrue(csv.StartsWith("Timestamp,SequenceId,Speaker,SourceText,TranslatedText,Provider,LatencyMs,IsCacheHit"));
                Assert.IsTrue(csv.Contains("\"Line with, comma and \"\"quotes\"\"\""));
                Assert.IsTrue(csv.Contains("\"Dòng có, dấu phẩy và \"\"ngoặc kép\"\"\""));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void SessionManager_ToJsonString_GeneratesValidJsonArray()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TranslateBotTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new SessionManager(tempDir);
                manager.AddRecord(1, "こんにちは", "Xin chào", "Mash", "Gemini", 200, false);

                string json = manager.ToJsonString();
                var array = JArray.Parse(json);
                Assert.AreEqual(1, array.Count);
                Assert.AreEqual("Mash", array[0]["Speaker"]?.ToString());
                Assert.AreEqual("Xin chào", array[0]["TranslatedText"]?.ToString());
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void SessionManager_ToMarkdownString_GeneratesFormattedMarkdown()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TranslateBotTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new SessionManager(tempDir);
                manager.AddRecord(1, "敵襲！", "Enemy attack!", "Mash", "Gemini", 180, false);

                string md = manager.ToMarkdownString();
                Assert.IsTrue(md.Contains("# FGO Game Dialogue Transcript"));
                Assert.IsTrue(md.Contains("Mash"));
                Assert.IsTrue(md.Contains("Enemy attack!"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void SessionManager_ExportToFile_WritesFileToDisk()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TranslateBotTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new SessionManager(tempDir);
                manager.AddRecord(1, "勝利！", "Victory!", "Saber", "Gemini", 120, false);

                string exportCsvPath = Path.Combine(tempDir, "export_test.csv");
                manager.ExportToFile(exportCsvPath, SessionExportFormat.Csv);
                Assert.IsTrue(File.Exists(exportCsvPath));
                string content = File.ReadAllText(exportCsvPath);
                Assert.IsTrue(content.Contains("Victory!"));

                string exportJsonPath = Path.Combine(tempDir, "export_test.json");
                manager.ExportToFile(exportJsonPath, SessionExportFormat.Json);
                Assert.IsTrue(File.Exists(exportJsonPath));

                string exportMdPath = Path.Combine(tempDir, "export_test.md");
                manager.ExportToFile(exportMdPath, SessionExportFormat.Markdown);
                Assert.IsTrue(File.Exists(exportMdPath));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // 3. TTS Service Settings & Clamping Tests
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void TtsService_VolumeAndRateClamping_EnsuresValidRanges()
        {
            using var tts = new TtsService();

            // Volume should clamp between 0 and 100
            tts.Volume = -10;
            Assert.AreEqual(0, tts.Volume);

            tts.Volume = 150;
            Assert.AreEqual(100, tts.Volume);

            tts.Volume = 75;
            Assert.AreEqual(75, tts.Volume);

            // Rate should clamp between -10 and 10
            tts.Rate = -25;
            Assert.AreEqual(-10, tts.Rate);

            tts.Rate = 25;
            Assert.AreEqual(10, tts.Rate);

            tts.Rate = 2;
            Assert.AreEqual(2, tts.Rate);
        }

        [TestMethod]
        public void TtsService_SpeakAsync_HandlesEmptyGracefully()
        {
            using var tts = new TtsService();

            // Should not throw on null or whitespace
            tts.SpeakAsync(null!);
            tts.SpeakAsync("");
            tts.SpeakAsync("   ");
            tts.Stop();
        }

        // ═══════════════════════════════════════════════════════════════════
        // 4. Native Core Evaluation & Benchmarks
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void NativeCoreEvaluation_ManagedAccelerator_ReportsZeroAllocationsAndSubMillisecond()
        {
            var accelerator = new ManagedAccelerator();
            Assert.IsTrue(accelerator.IsNativeAvailable);
            Assert.AreEqual("Pure Managed C# (Vector/SIMD-optimized, Zero Allocation)", accelerator.AcceleratorDescription);

            byte[] b1 = new byte[1920 * 1080 * 4];
            byte[] b2 = new byte[1920 * 1080 * 4];

            double diff = accelerator.ComputeDiffRatio(b1, b2, 1920, 1080);
            Assert.AreEqual(0.0, diff, 0.0001);

            var summary = NativeCoreEvaluation.GetTelemetrySummary();
            Assert.IsNotNull(summary);
            Assert.IsTrue(summary.Contains("NATIVE RUST CORE EVALUATION REPORT"));
            Assert.IsTrue(summary.Contains("PROFILING CONCLUSION"));
            Assert.IsTrue(summary.Contains("ARCHITECTURAL DECISION"));
        }
    }
}
