using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.OCR;
using TranslateBot.OCR.Preprocessing;

namespace TranslateBot.Tests
{
    [TestClass]
    public class OcrRouterTests
    {
        private class MockOcrEngine : IOcrEngine
        {
            public string Name { get; }
            public Func<byte[], int, int, Task<OcrResult>> Handler { get; set; }
            public int CallCount { get; private set; }
            public int LastReceivedWidth { get; private set; }
            public int LastReceivedHeight { get; private set; }

            public MockOcrEngine(string name, string returnText = "")
            {
                Name = name;
                Handler = (pixels, w, h) => Task.FromResult(new OcrResult { Text = returnText });
            }

            public Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height)
            {
                CallCount++;
                LastReceivedWidth = width;
                LastReceivedHeight = height;
                return Handler(bgraPixels, width, height);
            }
        }

        [TestMethod]
        public async Task OcrRouter_FastestStrategy_PrioritizesWindowsOcr()
        {
            var router = new OcrRouter(OcrRoutingStrategy.Fastest);
            var winEngine = new MockOcrEngine("WindowsOCR", "Windows Text");
            var oneEngine = new MockOcrEngine("OneOCR", "OneOCR Text");

            router.RegisterEngine("OneOCR", oneEngine);
            router.RegisterEngine("WindowsOCR", winEngine);

            var result = await router.ExtractTextAsync(new byte[16], 2, 2);

            Assert.AreEqual("Windows Text", result.Text);
            Assert.AreEqual(1, winEngine.CallCount);
            Assert.AreEqual(0, oneEngine.CallCount);
            Assert.AreEqual("WindowsOCR", router.ActiveEngineName);
        }

        [TestMethod]
        public async Task OcrRouter_Fallback_SwitchesWhenPrimaryThrowsException()
        {
            var router = new OcrRouter(OcrRoutingStrategy.Balanced);
            var oneEngine = new MockOcrEngine("OneOCR")
            {
                Handler = (p, w, h) => throw new InvalidOperationException("Native DLL init failed")
            };
            var winEngine = new MockOcrEngine("WindowsOCR", "Fallback Result");

            router.RegisterEngine("OneOCR", oneEngine);
            router.RegisterEngine("WindowsOCR", winEngine);

            var result = await router.ExtractTextAsync(new byte[16], 2, 2);

            Assert.AreEqual("Fallback Result", result.Text);
            Assert.AreEqual(1, oneEngine.CallCount);
            Assert.AreEqual(1, winEngine.CallCount);
            Assert.AreEqual("WindowsOCR", router.ActiveEngineName);
        }

        [TestMethod]
        public async Task OcrRouter_Fallback_SwitchesWhenPrimaryReturnsEmpty()
        {
            var router = new OcrRouter(OcrRoutingStrategy.Balanced);
            var oneEngine = new MockOcrEngine("OneOCR", ""); // Primary returns empty
            var winEngine = new MockOcrEngine("WindowsOCR", "Found in Fallback");

            router.RegisterEngine("OneOCR", oneEngine);
            router.RegisterEngine("WindowsOCR", winEngine);

            var result = await router.ExtractTextAsync(new byte[16], 2, 2);

            Assert.AreEqual("Found in Fallback", result.Text);
            Assert.AreEqual(1, oneEngine.CallCount);
            Assert.AreEqual(1, winEngine.CallCount);
            Assert.AreEqual("WindowsOCR", router.ActiveEngineName);
        }

        [TestMethod]
        public async Task OcrRouter_AppliesPreprocessing_BeforeCallingEngine()
        {
            var router = new OcrRouter();
            router.DefaultPreprocessing = PreprocessingOptions.FromPreset(PreprocessPreset.FgoDialogue); // 2x scale

            var engine = new MockOcrEngine("WindowsOCR", "Result");
            router.RegisterEngine("WindowsOCR", engine);

            int initialW = 10, initialH = 10;
            byte[] pixels = new byte[initialW * initialH * 4];

            await router.ExtractTextAsync(pixels, initialW, initialH);

            Assert.AreEqual(20, engine.LastReceivedWidth, "Engine phải nhận ảnh đã upscale 2x");
            Assert.AreEqual(20, engine.LastReceivedHeight);
        }

        [TestMethod]
        public void OcrArea_GetEffectivePreprocessing_ResolvesTypeDefaults()
        {
            var speakerArea = new OcrArea
            {
                Name = "Speaker Name",
                Type = OcrAreaType.Speaker
            };

            var prepSpeaker = speakerArea.GetEffectivePreprocessing();
            Assert.AreEqual(PreprocessPreset.FgoSpeaker, prepSpeaker.Preset);
            Assert.AreEqual(2, prepSpeaker.ScaleFactor);
            Assert.IsTrue(prepSpeaker.EnableLuminanceFilter);

            var dialogueArea = new OcrArea
            {
                Name = "Main Dialogue",
                Type = OcrAreaType.Dialogue
            };

            var prepDialogue = dialogueArea.GetEffectivePreprocessing();
            Assert.AreEqual(PreprocessPreset.FgoDialogue, prepDialogue.Preset);
            Assert.AreEqual(2, prepDialogue.ScaleFactor);

            // Custom override
            dialogueArea.CustomPreprocessing = new PreprocessingOptions
            {
                Preset = PreprocessPreset.Custom,
                ContrastBoost = 2.0
            };
            Assert.AreEqual(PreprocessPreset.Custom, dialogueArea.GetEffectivePreprocessing().Preset);
        }
    }
}
