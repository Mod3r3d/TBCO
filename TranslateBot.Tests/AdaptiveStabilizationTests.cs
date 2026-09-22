using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Dialogue;

namespace TranslateBot.Tests
{
    [TestClass]
    public class AdaptiveStabilizationTests
    {
        [TestMethod]
        public void StabilityConfig_Presets_ConfigureExpectedDefaults()
        {
            var fast = StabilityConfig.CreatePreset(StabilityPreset.Fast);
            Assert.AreEqual(150, fast.StableDurationMs);
            Assert.AreEqual(2, fast.MinTextLength);

            var balanced = StabilityConfig.CreatePreset(StabilityPreset.Balanced);
            Assert.AreEqual(300, balanced.StableDurationMs);
            Assert.AreEqual(4, balanced.MinTextLength);

            var safe = StabilityConfig.CreatePreset(StabilityPreset.Safe);
            Assert.AreEqual(600, safe.StableDurationMs);
            Assert.AreEqual(6, safe.MinTextLength);
        }

        [TestMethod]
        public void StabilityConfig_CalculatesAdaptiveDuration_BasedOnLengthAndPunctuation()
        {
            var config = StabilityConfig.CreatePreset(StabilityPreset.Balanced); // Base = 300ms
            
            // Short complete text
            int shortDur = config.GetEffectiveStableDuration("Hello world.");
            Assert.AreEqual(300, shortDur);

            // Long complete text (should scale up)
            string longText = "This is a very long Fate Grand Order dialogue line that is being typed out by typewriter effect.";
            int longDur = config.GetEffectiveStableDuration(longText);
            Assert.IsTrue(longDur > shortDur, $"Long text duration ({longDur}) must be greater than base duration ({shortDur})");

            // Text ending with incomplete punctuation (comma or ellipsis)
            string incompleteText = "Wait, don't go yet...";
            int incompleteDur = config.GetEffectiveStableDuration(incompleteText);
            Assert.IsTrue(incompleteDur > shortDur, "Incomplete text ending with ellipsis should receive penalty extension");
        }

        [TestMethod]
        public void DialogueTracker_FiltersText_ShorterThanMinTextLength()
        {
            var config = new StabilityConfig
            {
                MinTextLength = 5,
                StableDurationMs = 200
            };
            var tracker = new DialogueTracker(config);

            // Shorter than 5 chars -> should be ignored
            tracker.ProcessOcrResult("Abc");
            Assert.IsNull(tracker.CurrentDialogue);

            // 5 chars or longer -> should be tracked
            tracker.ProcessOcrResult("Abcde");
            Assert.IsNotNull(tracker.CurrentDialogue);
            Assert.AreEqual("Abcde", tracker.CurrentDialogue.RawText);
        }

        [TestMethod]
        public void DialogueTracker_WithFastPreset_TransitionsToStable()
        {
            var config = new StabilityConfig
            {
                Preset = StabilityPreset.Fast,
                StableDurationMs = 80,
                MinTextLength = 2
            };
            var tracker = new DialogueTracker(config);

            tracker.ProcessOcrResult("Ready!");
            Assert.AreEqual(DialogueState.Partial, tracker.CurrentDialogue!.State);

            // Ngay khi văn bản dừng thay đổi (elapsed < 80ms), chuyển sang Stable
            tracker.CheckStability();
            Assert.AreEqual(DialogueState.Stable, tracker.CurrentDialogue.State);

            // Khi vượt mốc StableDurationMs (80ms), chuyển sang Confirmed và đưa vào hàng đợi dịch
            Thread.Sleep(100);
            tracker.CheckStability();
            var pending = tracker.GetPendingTranslations();
            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual("Ready!", pending[0].RawText);
        }
    }
}
