using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using TranslateBot.Dialogue;
using TranslateBot.Infrastructure;
using TranslateBot.Overlay;

namespace TranslateBot.Tests
{
    [TestClass]
    public class OverlayTests
    {
        // ═══════════════════════════════════════════════════════════════════
        // 1. OverlayStyle & Theme Preset Tests
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void OverlayStyle_CreatePreset_FgoChaldea_ShouldHaveChaldeaColors()
        {
            var style = OverlayStyle.CreatePreset(OverlayTheme.FgoChaldea, OverlayMode.Overlay);

            Assert.AreEqual(OverlayTheme.FgoChaldea, style.Theme);
            Assert.AreEqual(OverlayMode.Overlay, style.Mode);
            Assert.IsTrue(style.HasTextShadow);
            Assert.AreEqual(0.88, style.CardOpacity, 0.01);
            Assert.IsNotNull(style.SpeakerBadgeBrush);
            Assert.IsNotNull(style.BackgroundBrush);
        }

        [TestMethod]
        public void OverlayStyle_CreatePreset_HighContrast_ShouldHaveHighContrastProperties()
        {
            var style = OverlayStyle.CreatePreset(OverlayTheme.HighContrast, OverlayMode.Overlay);

            Assert.AreEqual(OverlayTheme.HighContrast, style.Theme);
            Assert.AreEqual(FontWeights.Bold, style.FontWeight);
            Assert.AreEqual(2.0, style.BorderThickness.Left);
            Assert.AreEqual(0.96, style.CardOpacity, 0.01);
        }

        [TestMethod]
        public void OverlayStyle_CreatePreset_LayerMode_ShouldBeBorderlessAndTranslucent()
        {
            var style = OverlayStyle.CreatePreset(OverlayTheme.Dark, OverlayMode.Layer);

            Assert.AreEqual(OverlayMode.Layer, style.Mode);
            Assert.AreEqual(0, style.BorderThickness.Left);
            Assert.AreEqual(0, style.BorderThickness.Top);
            Assert.AreEqual(0.50, style.CardOpacity, 0.01);
            Assert.IsTrue(style.HasTextShadow, "Layer HUD phải có bóng đổ chữ để đọc rõ trên nền game");
        }

        [TestMethod]
        public void OverlayStyle_CreatePreset_LightMode_ShouldHaveLightColors()
        {
            var style = OverlayStyle.CreatePreset(OverlayTheme.Light, OverlayMode.Overlay);

            Assert.AreEqual(OverlayTheme.Light, style.Theme);
            Assert.AreEqual(0.94, style.CardOpacity, 0.01);
            Assert.IsNotNull(style.TextBrush);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 2. DialogueHistory Stage 6 Tests (Speaker, Capacity, Export)
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void DialogueHistory_Add_WithSpeakerAndLatency_ShouldStoreMetadata()
        {
            var history = new DialogueHistory();
            history.Add(101, "Senpai, daijoubu desu ka?", "Tiền bối, anh có sao không?", "Mash Kyrielight", 350.5);

            Assert.AreEqual(1, history.Count);
            Assert.IsNotNull(history.Current);
            Assert.AreEqual(101, history.Current.SequenceId);
            Assert.AreEqual("Mash Kyrielight", history.Current.Speaker);
            Assert.AreEqual(350.5, history.Current.LatencyMs);
            Assert.IsTrue(history.Current.FormattedSpeaker.Contains("Mash Kyrielight"));
        }

        [TestMethod]
        public void DialogueHistory_SetCapacity_ShouldTrimOldestEntries()
        {
            var history = new DialogueHistory();
            history.SetCapacity(5);

            for (int i = 1; i <= 10; i++)
            {
                history.Add(i, $"Raw {i}", $"Trans {i}", "Speaker", 100);
            }

            Assert.AreEqual(5, history.Count, "Dung lượng tối đa phải được giới hạn ở 5");
            Assert.AreEqual(10, history.Current?.SequenceId, "Current phải là câu mới nhất #10");

            // Câu đầu tiên còn lại phải là câu #6
            int firstSeq = -1;
            foreach (var item in history.Entries)
            {
                firstSeq = item.SequenceId;
                break;
            }
            Assert.AreEqual(6, firstSeq, "Câu cũ hơn phải được tự động trim");
        }

        [TestMethod]
        public void DialogueHistory_ExportToText_ShouldContainAllEntries()
        {
            var history = new DialogueHistory();
            history.Add(1, "Hello", "Xin chào", "Mash", 200);
            history.Add(2, "Master", "Master", "Da Vinci", 180);

            string exportText = history.ExportToText();

            StringAssert.Contains(exportText, "#0001");
            StringAssert.Contains(exportText, "[Mash]");
            StringAssert.Contains(exportText, "Xin chào");
            StringAssert.Contains(exportText, "#0002");
            StringAssert.Contains(exportText, "[Da Vinci]");
            StringAssert.Contains(exportText, "Master");
        }

        [TestMethod]
        public void DialogueHistory_ExportToJson_ShouldBeValidJson()
        {
            var history = new DialogueHistory();
            history.Add(1, "Test original", "Bản dịch thử", "TestSpeaker", 150);

            string json = history.ExportToJson();
            Assert.IsFalse(string.IsNullOrWhiteSpace(json));

            var parsed = JsonConvert.DeserializeObject<DialogueEntry[]>(json);
            Assert.IsNotNull(parsed);
            Assert.AreEqual(1, parsed.Length);
            Assert.AreEqual("Test original", parsed[0].OriginalText);
            Assert.AreEqual("Bản dịch thử", parsed[0].TranslatedText);
            Assert.AreEqual("TestSpeaker", parsed[0].Speaker);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 3. ConfigManager Stage 6 Persistence Tests
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void AppConfig_Stage6Fields_ShouldSerializeAndDeserializeAccurately()
        {
            var config = new AppConfig
            {
                OverlayEnabled = true,
                OverlayMode = "Layer",
                OverlayTheme = "HighContrast",
                DisplayMode = "Debug",
                OverlayPosition = "Top",
                OverlayX = 320,
                OverlayY = 150,
                OverlayWidth = 850,
                OverlayHeight = 160,
                OverlayLocked = true,
                OverlayFontSize = 24.5,
                OverlayOpacity = 0.75,
                MaxHistoryEntries = 200
            };

            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            var deserialized = JsonConvert.DeserializeObject<AppConfig>(json);

            Assert.IsNotNull(deserialized);
            Assert.IsTrue(deserialized.OverlayEnabled);
            Assert.AreEqual("Layer", deserialized.OverlayMode);
            Assert.AreEqual("HighContrast", deserialized.OverlayTheme);
            Assert.AreEqual("Debug", deserialized.DisplayMode);
            Assert.AreEqual("Top", deserialized.OverlayPosition);
            Assert.AreEqual(320, deserialized.OverlayX);
            Assert.AreEqual(150, deserialized.OverlayY);
            Assert.AreEqual(850, deserialized.OverlayWidth);
            Assert.AreEqual(160, deserialized.OverlayHeight);
            Assert.IsTrue(deserialized.OverlayLocked);
            Assert.AreEqual(24.5, deserialized.OverlayFontSize);
            Assert.AreEqual(0.75, deserialized.OverlayOpacity);
            Assert.AreEqual(200, deserialized.MaxHistoryEntries);
        }
    }
}
