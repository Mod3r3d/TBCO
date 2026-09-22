using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Capture;
using TranslateBot.Dialogue;
using TranslateBot.Translation;

namespace TranslateBot.Tests
{
    [TestClass]
    public class ReliabilityAndFixesTests
    {
        [TestMethod]
        public void FrameChangeDetector_SubtleTextChange_0_8_Percent_ShouldDetectChange()
        {
            // Trong video 4.mkv tại 00:30, khi đổi sang câu thoại mới, tỷ lệ thay đổi là 0.8%
            // Trước đây ngưỡng là 1.5% khiến app đứng hình.
            // Test này đảm bảo ngưỡng mới (0.5%) phát hiện được thay đổi 0.8%.
            var detector = new FrameChangeDetector { SampleScale = 1 };
            int width = 100, height = 100;
            int totalPixels = width * height;
            byte[] frame1 = new byte[totalPixels * 4];

            // Frame 1
            detector.CheckAreaChange("fgo_dialogue", frame1, width, height);

            // Frame 2: thay đổi 80 pixel trên tổng số 10,000 pixel = 0.8%
            byte[] frame2 = (byte[])frame1.Clone();
            for (int i = 0; i < 80; i++)
            {
                frame2[i * 4 + 0] = 255; // B
                frame2[i * 4 + 1] = 255; // G
                frame2[i * 4 + 2] = 255; // R
            }

            var result = detector.CheckAreaChange("fgo_dialogue", frame2, width, height);

            Assert.IsTrue(result.HasChanged, "Thay đổi 0.8% phải được nhận diện là có thay đổi khung hình");
            Assert.IsTrue(result.ChangedRatio >= 0.007 && result.ChangedRatio <= 0.009, $"Tỷ lệ thay đổi đo được: {result.ChangedRatio}");
        }

        [TestMethod]
        public void DialogueTracker_ShouldFilterVideoPlayerTimestamps()
        {
            var tracker = new DialogueTracker();
            // Mô phỏng video 5.mkv dính thanh YouTube seekbar
            string rawOcr = "Though they may not be the most diligent of folk\n1:38 / 17:10";
            tracker.ProcessOcrResult(rawOcr);

            Assert.IsNotNull(tracker.CurrentDialogue);
            Assert.IsFalse(tracker.CurrentDialogue.NormalizedText.Contains("1:38 / 17:10"), "Timestamp video phải được lọc sạch");
            Assert.IsTrue(tracker.CurrentDialogue.NormalizedText.Contains("Though they may not be the most diligent of folk"));
        }

        [TestMethod]
        public void DialogueTracker_ShouldIgnoreShortGarbageFragments()
        {
            var tracker = new DialogueTracker();
            // Mô phỏng video 5.mkv rê chuột trúng rác OCR "g" hoặc "?"
            tracker.ProcessOcrResult("g");
            Assert.IsNull(tracker.CurrentDialogue, "Ký tự đơn 'g' không được coi là thoại");

            tracker.ProcessOcrResult("...");
            Assert.IsNull(tracker.CurrentDialogue, "Dấu chấm ba chấm '...' không được coi là thoại");

            tracker.ProcessOcrResult("•");
            Assert.IsNull(tracker.CurrentDialogue, "Ký tự bullet không được coi là thoại");
        }

        [TestMethod]
        public void DialogueTracker_Reset_ShouldClearAllActiveState()
        {
            var tracker = new DialogueTracker();
            tracker.ProcessOcrResult("Hello world, this is a test dialogue");
            Assert.IsNotNull(tracker.CurrentDialogue);

            tracker.Reset();
            Assert.IsNull(tracker.CurrentDialogue, "Reset() phải đưa CurrentDialogue về null");
        }

        [TestMethod]
        public void GeminiProvider_DynamicModelAndLanguages_ShouldConfigureProperly()
        {
            var provider = new GeminiProvider("AIzaSyFakeKeyForTesting12345678901234");
            provider.ModelName = "gemini-1.5-pro";
            provider.TargetLanguage = "en";
            provider.SourceLanguage = "ja";

            Assert.AreEqual("gemini-1.5-pro", provider.ModelName);
            Assert.AreEqual("en", provider.TargetLanguage);
            Assert.AreEqual("ja", provider.SourceLanguage);
        }

        [TestMethod]
        public async Task TranslationWorker_OnFailure_ShouldFormatWarningTag()
        {
            // Mock provider trả về empty (giả lập API lỗi)
            var mockProvider = new FailingTranslationProvider();
            var worker = new TranslationWorker(mockProvider);
            worker.Start();

            var job = new DialogueJob(1, "Test original dialogue", "test original dialogue");
            string? receivedResult = null;
            var tcs = new TaskCompletionSource<bool>();

            worker.OnTranslationCompleted = (completedJob, result) =>
            {
                receivedResult = result;
                tcs.TrySetResult(true);
            };

            worker.EnqueueJob(job);
            await Task.WhenAny(tcs.Task, Task.Delay(2000));
            worker.Stop();

            Assert.IsNotNull(receivedResult);
            Assert.IsTrue(receivedResult.Contains("Chưa dịch được"), "Kết quả thất bại phải hiển thị tag cảnh báo thay vì giả vờ dịch thành công");
            Assert.IsTrue(receivedResult.Contains("Test original dialogue"));
        }

        [TestMethod]
        public void FloatingToolbar_STA_Initialization_And_PositionSafety()
        {
            var t = new System.Threading.Thread(() =>
            {
                var toolbar = new UI.FloatingToolbar();
                Assert.IsNotNull(toolbar);

                // Test safe position bounds
                toolbar.RestorePosition(-999, -999);
                Assert.IsTrue(toolbar.Left >= 0);
                Assert.IsTrue(toolbar.Top >= 0);

                toolbar.RestorePosition(99999, 99999);
                Assert.IsTrue(toolbar.Left >= 0);
                Assert.IsTrue(toolbar.Top >= 0);

                toolbar.SetBotRunning(true);
                toolbar.SetBotRunning(false);
                toolbar.SetOverlayActive(true);
                toolbar.SetOverlayLocked(true);
                toolbar.SetTtsActive(true);
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join(3000);
            Assert.IsFalse(t.IsAlive, "Thread STA phải hoàn tất trong 3 giây không bị treo");
        }

        [TestMethod]
        public void OverlayStyle_LightPreset_ShouldConfigureProperly()
        {
            var style = Overlay.OverlayStyle.CreatePreset(Overlay.OverlayTheme.Light, Overlay.OverlayMode.Overlay);
            Assert.IsNotNull(style);
            Assert.AreEqual(Overlay.OverlayTheme.Light, style.Theme);
            Assert.AreEqual(Overlay.OverlayMode.Overlay, style.Mode);
            Assert.IsNotNull(style.TextBrush);
        }

        [TestMethod]
        public void AppConfig_DetachedSubtitleDefaults_ShouldBeValid()
        {
            var config = new Infrastructure.AppConfig();
            Assert.IsFalse(config.IsSubtitleDetached, "Mặc định không tự động tách cửa sổ khi chưa bấm nút");
            Assert.AreEqual(680, config.SubtitleWindowWidth);
            Assert.AreEqual(160, config.SubtitleWindowHeight);
            Assert.AreEqual(16, config.SubtitleFontSize);
            Assert.IsTrue(config.SubtitleTopmost);
            Assert.AreEqual("gemini-3.5-flash", config.AiModel);
        }

        [TestMethod]
        public void GeminiProvider_NormalizeModelName_ShouldMapToValidModels()
        {
            Assert.AreEqual("gemini-3.5-flash", GeminiProvider.NormalizeModelName(null));
            Assert.AreEqual("gemini-3.5-flash", GeminiProvider.NormalizeModelName(""));
            Assert.AreEqual("gemini-3.5-flash", GeminiProvider.NormalizeModelName("gemini-flash-latest"));
            Assert.AreEqual("gemini-3.5-flash", GeminiProvider.NormalizeModelName("gemini-3.5-flash"));
            Assert.AreEqual("gemini-3.6-flash", GeminiProvider.NormalizeModelName("gemini-3.6-flash"));
            Assert.AreEqual("gemini-3.1-flash-lite", GeminiProvider.NormalizeModelName("gemini-3.1-flash-lite"));
            Assert.AreEqual("gemini-3.1-pro-preview", GeminiProvider.NormalizeModelName("gemini-3.1-pro-preview"));
            Assert.AreEqual("gemini-2.5-flash", GeminiProvider.NormalizeModelName("gemini-2.5-flash"));
        }

        [TestMethod]
        public void DetachedSubtitleWindow_STA_ShouldInitializeWithoutCrash()
        {
            var t = new System.Threading.Thread(() =>
            {
                var window = new UI.DetachedSubtitleWindow();
                Assert.IsNotNull(window);
                window.SetFontSize(18);
                window.SetTopmost(true);
                window.SetTargetWindow("Game Demo Window");
                window.UpdateDialogue("Hello World", "Xin chào thế giới", "Artoria", null);
                window.SetLocked(false);
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join(3000);
            Assert.IsFalse(t.IsAlive, "STA thread khởi tạo DetachedSubtitleWindow phải hoàn tất trong 3s");
        }

        private class FailingTranslationProvider : ITranslationProvider
        {
            public Task<string> TranslateAsync(string text) => Task.FromResult(string.Empty);
            public Task<string> TranslateAsync(string text, TranslationContext? context) => Task.FromResult(string.Empty);
        }
    }
}
