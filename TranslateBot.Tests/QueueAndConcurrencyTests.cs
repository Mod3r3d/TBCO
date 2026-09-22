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
    }
}
