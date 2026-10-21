using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Database;
using TranslateBot.Infrastructure;
using TranslateBot.Dialogue;
using TranslateBot.OCR;
using TranslateBot.Translation;

namespace TranslateBot.Tests
{
    [TestClass]
    public class EvaluationRegressionTests
    {
        [TestMethod]
        public void ExclusionManager_ApplyExclusions_WithOffset_ClipsCorrectly()
        {
            var manager = new ExclusionManager();
            int frameW = 50;
            int frameH = 50;
            int totalBytes = frameW * frameH * 4;
            byte[] frame = new byte[totalBytes];

            // Điền pixel toàn bộ là 255 (trắng)
            Array.Fill(frame, (byte)255);

            // Exclusion ở tọa độ tuyệt đối (50, 50, w: 20, h: 20)
            manager.Add(new ExclusionArea
            {
                Id = "ex1",
                Name = "TestEx",
                RegionX = 50,
                RegionY = 50,
                RegionWidth = 20,
                RegionHeight = 20,
                Enabled = true
            });

            // Frame được capture tại offset (40, 40)
            // Tọa độ cục bộ của exclusion trên frame: x = 50 - 40 = 10, y = 50 - 40 = 10
            // Phạm vi bị tô đen trên frame: X: [10, 30), Y: [10, 30)
            manager.ApplyExclusions(frame, frameW, frameH, offsetX: 40, offsetY: 40);

            // Kiểm tra pixel tại (15, 15) cục bộ phải bị zero-out (đen: 0)
            int insideIdx = (15 * frameW + 15) * 4;
            Assert.AreEqual(0, frame[insideIdx]);
            Assert.AreEqual(0, frame[insideIdx + 1]);
            Assert.AreEqual(0, frame[insideIdx + 2]);
            Assert.AreEqual(0, frame[insideIdx + 3]);

            // Kiểm tra pixel tại (5, 5) cục bộ nằm ngoài exclusion phải giữ nguyên 255
            int outsideIdx = (5 * frameW + 5) * 4;
            Assert.AreEqual(255, frame[outsideIdx]);
            Assert.AreEqual(255, frame[outsideIdx + 1]);
            Assert.AreEqual(255, frame[outsideIdx + 2]);
            Assert.AreEqual(255, frame[outsideIdx + 3]);
        }

        [TestMethod]
        public void ExclusionManager_ApplyExclusions_PartialIntersection_ClipsProperly()
        {
            var manager = new ExclusionManager();
            int frameW = 50;
            int frameH = 50;
            byte[] frame = new byte[frameW * frameH * 4];
            Array.Fill(frame, (byte)255);

            // ExclusionArea ở (0, 0, w: 50, h: 50)
            manager.Add(new ExclusionArea
            {
                Id = "ex_part",
                RegionX = 0,
                RegionY = 0,
                RegionWidth = 50,
                RegionHeight = 50,
                Enabled = true
            });

            // Frame capture ở (30, 30, w: 50, h: 50)
            // Local exclusion: startX = max(0, -30) = 0, endX = min(50, -30 + 50 = 20)
            // Giao nhau từ [0, 20) theo cả X và Y
            manager.ApplyExclusions(frame, frameW, frameH, offsetX: 30, offsetY: 30);

            int clippedInsideIdx = (10 * frameW + 10) * 4;
            Assert.AreEqual(0, frame[clippedInsideIdx], "Điểm giao nhau [10, 10] phải bị tô đen");

            int clippedOutsideIdx = (35 * frameW + 35) * 4;
            Assert.AreEqual(255, frame[clippedOutsideIdx], "Điểm ngoài giao [35, 35] phải giữ nguyên 255");
        }

        [TestMethod]
        public void ExclusionManager_ApplyExclusions_Disabled_DoesNotModifyFrame()
        {
            var manager = new ExclusionManager();
            byte[] frame = new byte[100 * 4];
            Array.Fill(frame, (byte)255);

            manager.Add(new ExclusionArea
            {
                Id = "disabled_ex",
                RegionX = 0,
                RegionY = 0,
                RegionWidth = 10,
                RegionHeight = 10,
                Enabled = false
            });

            manager.ApplyExclusions(frame, 10, 10, 0, 0);

            for (int i = 0; i < frame.Length; i++)
            {
                Assert.AreEqual(255, frame[i], "Exclusion tắt không được ảnh hưởng tới frame");
            }
        }

        [TestMethod]
        public async Task DynamicConcurrencyLimiter_AcquireAndDynamicScale()
        {
            using var limiter = new DynamicConcurrencyLimiter(1);
            Assert.AreEqual(1, limiter.MaxConcurrency);
            Assert.AreEqual(0, limiter.ActiveCount);

            // 1st acquire succeeds immediately
            await limiter.WaitAsync();
            Assert.AreEqual(1, limiter.ActiveCount);

            // 2nd acquire must wait
            var waitTask = limiter.WaitAsync();
            Assert.IsFalse(waitTask.IsCompleted);
            Assert.AreEqual(1, limiter.WaiterCount);

            // Tăng giới hạn đồng thời từ 1 lên 2
            limiter.MaxConcurrency = 2;

            // waitTask sẽ được unblock ngay lập tức
            await Task.WhenAny(waitTask, Task.Delay(500));
            Assert.IsTrue(waitTask.IsCompletedSuccessfully);
            Assert.AreEqual(2, limiter.ActiveCount);

            limiter.Release();
            limiter.Release();
            Assert.AreEqual(0, limiter.ActiveCount);
        }

        [TestMethod]
        public async Task DynamicConcurrencyLimiter_Cancellation_HandlesGracefully()
        {
            using var limiter = new DynamicConcurrencyLimiter(1);
            await limiter.WaitAsync();

            using var cts = new CancellationTokenSource();
            var waitTask = limiter.WaitAsync(cts.Token);

            Assert.IsFalse(waitTask.IsCompleted);
            cts.Cancel();

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await waitTask);
            limiter.Release();
            Assert.AreEqual(0, limiter.ActiveCount);
        }

        [TestMethod]
        public void OcrLanguageMapping_MapsCorrectlyToWindowsTags()
        {
            Assert.AreEqual("ja-JP", MainWindow.MapSourceLanguageToOcrTag("ja"));
            Assert.AreEqual("ja-JP", MainWindow.MapSourceLanguageToOcrTag("ja-JP"));
            Assert.AreEqual("en-US", MainWindow.MapSourceLanguageToOcrTag("en"));
            Assert.AreEqual("en-US", MainWindow.MapSourceLanguageToOcrTag("en-US"));
            Assert.AreEqual("zh-Hans-CN", MainWindow.MapSourceLanguageToOcrTag("zh"));
            Assert.AreEqual("zh-Hans-CN", MainWindow.MapSourceLanguageToOcrTag("zh-CN"));
            Assert.AreEqual("ja-JP", MainWindow.MapSourceLanguageToOcrTag("auto"));
            Assert.AreEqual("ja-JP", MainWindow.MapSourceLanguageToOcrTag(null));
            Assert.AreEqual("ja-JP", MainWindow.MapSourceLanguageToOcrTag(""));
            Assert.AreEqual("ja-JP", MainWindow.MapSourceLanguageToOcrTag("unknown"));
            Assert.AreEqual("fr-FR", MainWindow.MapSourceLanguageToOcrTag("fr-FR"));
        }

        [TestMethod]
        public void ProviderHealthTracker_CircuitBreaker_HalfOpenRecovery()
        {
            var tracker = new ProviderHealthTracker();
            string provider = "TestProvider";

            Assert.IsTrue(tracker.IsAvailable(provider));

            // Báo lỗi 3 lần liên tiếp -> Unhealthy (Circuit Open)
            tracker.ReportFailure(provider, "HTTP 500");
            tracker.ReportFailure(provider, "HTTP 500");
            tracker.ReportFailure(provider, "HTTP 500");

            var metrics = tracker.GetOrCreate(provider);
            Assert.AreEqual(ProviderStatus.Unhealthy, metrics.Status);
            Assert.IsFalse(tracker.IsAvailable(provider), "Ngay sau 3 lỗi liên tiếp, circuit breaker phải mở (ngắt mạch)");

            // Giả lập 31 giây trôi qua
            metrics.LastFailureAt = DateTime.Now.AddSeconds(-31);

            Assert.IsTrue(tracker.IsAvailable(provider), "Sau 30 giây, circuit breaker phải chuyển sang half-open và cho phép thử lại");
        }

        [TestMethod]
        public void AppPaths_CustomAppDataDir_ResolvesPathsInsideCustomDir()
        {
            string originalDir = AppPaths.AppDataDir;
            string tempDir = Path.Combine(Path.GetTempPath(), "TBCO_Test_" + Guid.NewGuid().ToString("N"));

            try
            {
                AppPaths.AppDataDir = tempDir;
                Assert.AreEqual(tempDir, AppPaths.AppDataDir);

                string configPath = AppPaths.GetConfigPath();
                Assert.AreEqual(Path.Combine(tempDir, "config.json"), configPath);

                string dbPath = AppPaths.GetDatabasePath();
                Assert.AreEqual(Path.Combine(tempDir, "tbco_memory.db"), dbPath);

                string sessionsDir = AppPaths.GetSessionsDirectory();
                Assert.AreEqual(Path.Combine(tempDir, "sessions"), sessionsDir);
                Assert.IsTrue(Directory.Exists(sessionsDir));
            }
            finally
            {
                AppPaths.AppDataDir = originalDir;
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [TestMethod]
        public void SqliteMemoryStore_PruneOldEntries_CapsMaxRecords()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), "test_prune_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using var store = new SqliteMemoryStore(tempDb);
                for (int i = 0; i < 50; i++)
                {
                    store.Store($"norm_{i}", $"Text {i}", $"Bản dịch {i}", null, "Gemini", null);
                }

                Assert.AreEqual(50, store.GetCount());

                // Prune về tối đa 20 bản ghi
                store.PruneOldEntries(maxEntries: 20);

                Assert.AreEqual(20, store.GetCount(), "Số lượng bản ghi sau khi prune phải được giới hạn ở maxEntries");
            }
            finally
            {
                if (File.Exists(tempDb))
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }
        }

        [TestMethod]
        public void TextSimilarity_TryFindScrollOverlap_FgoScenario_StitchesCompleteSentence()
        {
            string line1 = "This is the story of a single future -what became of an island of Mystics that was reduced to the Land of the";
            string line2 = "island of Mystics that was reduced to the Land of the Fae.";

            bool success = TextSimilarity.TryFindScrollOverlap(line1, line2, out string merged);

            Assert.IsTrue(success, "Phải nhận diện được phần trùng lặp khi câu dài xuống dòng");
            Assert.IsTrue(merged.StartsWith("This is the story"), "Câu ghép phải giữ được đoạn mở đầu");
            Assert.IsTrue(merged.EndsWith("Land of the Fae."), "Câu ghép phải chứa được đuôi câu Fae.");
            Assert.IsFalse(merged.Contains("Land of the island"), "Không được để trùng lặp từ ngữ khi ghép");
        }

        [TestMethod]
        public void TextSimilarity_TryFindScrollOverlap_JapaneseScenario_StitchesCompleteSentence()
        {
            string line1 = "新生ノーチラス号の工事も、本日をもって";
            string line2 = "本日をもって終了する予定です。";

            bool success = TextSimilarity.TryFindScrollOverlap(line1, line2, out string merged);

            Assert.IsTrue(success);
            Assert.AreEqual("新生ノーチラス号の工事も、本日をもって終了する予定です。", merged);
        }

        [TestMethod]
        public void DialogueTracker_MultiLineScrolling_PreservesWholeSentence()
        {
            var tracker = new DialogueTracker();

            // Khung 1: Câu thoại dài bắt đầu hiện 2 dòng đầu
            tracker.ProcessOcrResult("This is the story of a single future -what became of an island of Mystics that was reduced to the Land of the");
            Assert.IsNotNull(tracker.CurrentDialogue);

            // Khung 2: Dòng 1 cuộn đi, dòng 3 xuất hiện
            tracker.ProcessOcrResult("island of Mystics that was reduced to the Land of the Fae.");

            // Kiểm tra tracker không bị tách câu mà ghép nối lại
            Assert.IsNotNull(tracker.CurrentDialogue);
            Assert.IsTrue(tracker.CurrentDialogue.NormalizedText.StartsWith("this is the story", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(tracker.CurrentDialogue.NormalizedText.EndsWith("land of the fae.", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(DialogueState.Extending, tracker.CurrentDialogue.State);
        }

        [TestMethod]
        public void TextSimilarity_TryMergeContinuation_NoOverlapIncompleteSentence_ShouldStitch()
        {
            string line1 = "The Holy Grail War was originally created by";
            string line2 = "the Three Founding Families to reach the Root.";

            bool success = TextSimilarity.TryMergeContinuation(line1, line2, out string merged);

            Assert.IsTrue(success);
            Assert.AreEqual("The Holy Grail War was originally created by the Three Founding Families to reach the Root.", merged);
        }

        [TestMethod]
        public void TextSimilarity_TryMergeContinuation_CompletedSentence_ShouldNotMerge()
        {
            string sentence1 = "The battle has ended.";
            string sentence2 = "We must prepare for the next mission.";

            bool success = TextSimilarity.TryMergeContinuation(sentence1, sentence2, out _);

            Assert.IsFalse(success, "Hai câu có dấu chấm hoàn chỉnh độc lập không được tự ý gộp.");
        }

        [TestMethod]
        public void OcrAreaManager_SelectRegion_SyncsDialogueAreaCorrectly()
        {
            var areaManager = new OCR.OcrAreaManager();
            Assert.AreEqual(0, areaManager.Areas.Count);

            // Giả lập người dùng quét vùng lần 1 qua Ctrl+F8
            int x1 = 100, y1 = 200, w1 = 800, h1 = 150;
            var existing = areaManager.Areas.FirstOrDefault(a => a.Type == OCR.OcrAreaType.Dialogue);
            if (existing != null)
            {
                existing.RegionX = x1;
            }
            else
            {
                areaManager.Add(new OCR.OcrArea
                {
                    Id = "dialogue-main",
                    Name = "Vùng Thoại (Chính)",
                    Type = OCR.OcrAreaType.Dialogue,
                    RegionX = x1,
                    RegionY = y1,
                    RegionWidth = w1,
                    RegionHeight = h1,
                    Enabled = true
                });
            }

            Assert.AreEqual(1, areaManager.Areas.Count);
            Assert.AreEqual(OCR.OcrAreaType.Dialogue, areaManager.Areas[0].Type);
            Assert.AreEqual(100, areaManager.Areas[0].RegionX);
            Assert.AreEqual(800, areaManager.Areas[0].RegionWidth);

            // Giả lập người dùng quét lại vùng lần 2 qua Ctrl+F8 (cập nhật tọa độ)
            int x2 = 120, y2 = 220, w2 = 850, h2 = 160;
            var dialogueArea = areaManager.Areas.FirstOrDefault(a => a.Type == OCR.OcrAreaType.Dialogue);
            Assert.IsNotNull(dialogueArea);
            dialogueArea.RegionX = x2;
            dialogueArea.RegionY = y2;
            dialogueArea.RegionWidth = w2;
            dialogueArea.RegionHeight = h2;

            Assert.AreEqual(1, areaManager.Areas.Count, "Không được nhân đôi area khi quét lại vùng thoại");
            Assert.AreEqual(120, areaManager.Areas[0].RegionX);
            Assert.AreEqual(850, areaManager.Areas[0].RegionWidth);
        }

        [TestMethod]
        public void AppConfig_ShowSpeakerBadge_DefaultsToFalse()
        {
            var config = new Infrastructure.AppConfig();
            Assert.IsFalse(config.ShowSpeakerBadge, "Mặc định không hiện nhãn tên người nói trên bảng dịch để tránh gây vướng");
        }
    }
}
