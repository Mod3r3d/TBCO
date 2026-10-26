using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Diagnostics;
using TranslateBot.Dialogue;
using TranslateBot.Translation;

namespace TranslateBot.Tests
{
    [TestClass]
    public class QueueAndConcurrencyTests
    {
        private class MockSuccessProvider : ITranslationProvider
        {
            public Task<string> TranslateAsync(string text)
            {
                return Task.FromResult($"[Dịch: {text}]");
            }

            public Task<string> TranslateAsync(string text, TranslationContext? context)
            {
                return TranslateAsync(text);
            }
        }

        [TestMethod]
        public void AdaptiveConcurrency_ThrottlesDownOnFailure()
        {
            var provider = new MockSuccessProvider();
            var worker = new TranslationWorker(provider);
            var metrics = new PerformanceMetrics();
            worker.Metrics = metrics;

            Assert.AreEqual(3, worker.CurrentConcurrency);

            worker.ReportFailure(isRateLimit: true);

            Assert.AreEqual(1, worker.CurrentConcurrency);
            Assert.AreEqual(1, metrics.ActiveConcurrency);
        }

        [TestMethod]
        public void AdaptiveConcurrency_ScalesUpOnSuccesses()
        {
            var provider = new MockSuccessProvider();
            var worker = new TranslationWorker(provider);
            var metrics = new PerformanceMetrics();
            worker.Metrics = metrics;

            worker.ReportFailure(isRateLimit: false);
            Assert.AreEqual(1, worker.CurrentConcurrency);

            // Cần 5 lần thành công để tăng 1 bậc
            for (int i = 0; i < 5; i++)
            {
                worker.ReportSuccess();
            }
            Assert.AreEqual(2, worker.CurrentConcurrency);
            Assert.AreEqual(2, metrics.ActiveConcurrency);

            // Thêm 5 lần thành công nữa để đạt tối đa 3
            for (int i = 0; i < 5; i++)
            {
                worker.ReportSuccess();
            }
            Assert.AreEqual(3, worker.CurrentConcurrency);
            Assert.AreEqual(3, metrics.ActiveConcurrency);
        }

        [TestMethod]
        public void RequestCoalescing_CancelsStalePrefetch()
        {
            var provider = new MockSuccessProvider();
            var worker = new TranslationWorker(provider);
            var metrics = new PerformanceMetrics();
            worker.Metrics = metrics;

            var prefetchJob = new DialogueJob(1, "Master, look out", "master look out")
            {
                IsPrefetch = true
            };

            worker.EnqueueJob(prefetchJob);
            Assert.IsFalse(prefetchJob.IsCancelled, "Job prefetch ban đầu không được bị hủy");

            // Câu thoại chính thức dài hơn xuất hiện
            var confirmedJob = new DialogueJob(2, "Master, look out for enemies ahead!", "master look out for enemies ahead")
            {
                IsPrefetch = false
            };

            worker.EnqueueJob(confirmedJob);

            Assert.IsTrue(prefetchJob.IsCancelled, "Job prefetch phải bị hủy tự động (Coalescing)");
            Assert.AreEqual(1, metrics.DroppedJobsCount, "Chỉ số DroppedJobsCount phải tăng lên 1");
        }

        [TestMethod]
        public async Task TranslationMemory_TracksCacheHitAndMissInMetrics()
        {
            var provider = new MockSuccessProvider();
            var worker = new TranslationWorker(provider);
            var metrics = new PerformanceMetrics();
            worker.Metrics = metrics;
            worker.Memory.Clear();
            worker.Start();

            var job1 = new DialogueJob(1, "Hello Chaldea", "hello chaldea");
            worker.EnqueueJob(job1);

            // Chờ worker xử lý xong
            await Task.Delay(100);

            Assert.AreEqual(1, metrics.CacheMissCount, "Lần đầu tiên phải là Cache Miss");

            // Đưa cùng câu thoại vào lần 2
            var job2 = new DialogueJob(2, "Hello Chaldea", "hello chaldea");
            worker.EnqueueJob(job2);

            Assert.AreEqual(1, metrics.CacheHitCount, "Lần thứ hai phải là Cache Hit");
            Assert.AreEqual(50.0, metrics.CacheHitRate, 0.01, "Tỷ lệ Cache Hit phải là 50%");

            await worker.StopAndWaitAsync(TimeSpan.FromMilliseconds(200));
        }

        [TestMethod]
        public void ReorderBuffer_LatestWins_DropsLateJobsAndPreventsDeadlock()
        {
            var buffer = new ReorderBuffer();
            var dispatched = new System.Collections.Generic.List<int>();

            buffer.OnOrderedResult = (job, text) =>
            {
                dispatched.Add(job.SequenceId);
            };

            var job1 = new DialogueJob(1, "Text 1", "text 1");
            var job2 = new DialogueJob(2, "Text 2", "text 2");
            var job3 = new DialogueJob(3, "Text 3", "text 3");

            // Câu 1 đến -> hiển thị
            buffer.Submit(job1, "Trans 1");
            Assert.AreEqual(1, buffer.LatestDispatchedSequence);
            Assert.AreEqual(1, dispatched.Count);
            Assert.AreEqual(1, dispatched[0]);

            // Câu 3 về đích trước câu 2 (mạng trễ ở câu 2)
            buffer.Submit(job3, "Trans 3");
            Assert.AreEqual(3, buffer.LatestDispatchedSequence);
            Assert.AreEqual(2, dispatched.Count);
            Assert.AreEqual(3, dispatched[1]);

            // Giờ câu 2 mới về đích muộn -> phải bị drop để không giật lùi phụ đề!
            buffer.Submit(job2, "Trans 2");
            Assert.AreEqual(3, buffer.LatestDispatchedSequence);
            Assert.AreEqual(2, dispatched.Count, "Câu #2 không được hiển thị");
            Assert.AreEqual(1, buffer.DroppedLateJobCount, "DroppedLateJobCount phải là 1");

            // Reset
            buffer.Reset();
            Assert.AreEqual(-1, buffer.LatestDispatchedSequence);
            Assert.AreEqual(0, buffer.DroppedLateJobCount);
        }

        [TestMethod]
        public void FastForwardCoalescing_CancelsOldPendingJobsWhenSpammed()
        {
            var provider = new MockSuccessProvider();
            var worker = new TranslationWorker(provider);
            var metrics = new PerformanceMetrics();
            worker.Metrics = metrics;

            // Đưa liên tiếp 5 confirmed jobs mô phỏng người dùng giữ nút tua nhanh/SKIP
            var j1 = new DialogueJob(1, "Line 1", "line 1");
            var j2 = new DialogueJob(2, "Line 2", "line 2");
            var j3 = new DialogueJob(3, "Line 3", "line 3");
            var j4 = new DialogueJob(4, "Line 4", "line 4");
            var j5 = new DialogueJob(5, "Line 5", "line 5");

            worker.EnqueueJob(j1);
            worker.EnqueueJob(j2);
            worker.EnqueueJob(j3);
            worker.EnqueueJob(j4);
            worker.EnqueueJob(j5);

            // Các câu cũ (1, 2, 3) phải được đánh dấu huỷ để không làm tắc nghẽn queue
            Assert.IsTrue(j1.IsCancelled || j2.IsCancelled || j3.IsCancelled, "Các câu cũ bị tua qua phải bị huỷ");
            Assert.IsTrue(metrics.DroppedJobsCount > 0, "Chỉ số DroppedJobsCount phải được ghi nhận");
        }

        [TestMethod]
        public void PerformanceMetrics_CreateSnapshot_ProducesAccurateImmutableCopy()
        {
            var metrics = new PerformanceMetrics();
            metrics.RecordFrameChecked(ocrSkipped: true, captureMs: 5.2, diffMs: 1.1);
            metrics.RecordFrameChecked(ocrSkipped: false, captureMs: 4.8, diffMs: 1.3);
            metrics.RecordOcrExecuted(ocrMs: 24.5);
            metrics.RecordTranslationRequest(latencyMs: 120.0, isSuccess: true, isRateLimit: false);
            metrics.RecordTranslationRequest(latencyMs: 0.0, isSuccess: false, isRateLimit: true);
            metrics.RecordCacheHit();
            metrics.RecordCacheMiss();
            metrics.RecordJobDropped();
            metrics.QueueDepth = 4;
            metrics.LoopFps = 30.5;

            var snapshot = metrics.CreateSnapshot();

            // Kiểm tra giá trị snapshot
            Assert.AreEqual(30.5, snapshot.LoopFps, 0.01);
            Assert.AreEqual(50.0, snapshot.OcrSkipPercentage, 0.01);
            Assert.AreEqual(1, snapshot.RateLimit429Count);
            Assert.AreEqual(1, snapshot.DroppedJobsCount);
            Assert.AreEqual(50.0, snapshot.CacheHitRate, 0.01);
            Assert.AreEqual(4, snapshot.QueueDepth);
            Assert.AreEqual(2, snapshot.TotalRequests);

            // Mutate gốc: Reset()
            metrics.Reset();

            // Snapshot bất biến không đổi
            Assert.AreEqual(30.5, snapshot.LoopFps, 0.01);
            Assert.AreEqual(1, snapshot.RateLimit429Count);
            Assert.AreEqual(1, snapshot.DroppedJobsCount);
        }
    }
}
