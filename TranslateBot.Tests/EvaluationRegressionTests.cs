using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Database;
using TranslateBot.Infrastructure;
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
    }
}
