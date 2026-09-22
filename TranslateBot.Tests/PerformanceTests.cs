using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Capture;
using TranslateBot.Diagnostics;

namespace TranslateBot.Tests
{
    [TestClass]
    public class PerformanceTests
    {
        // ═══════════════════════════════════════════════════════════════════
        // 1. FrameChangeDetector Tests
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void FrameChangeDetector_InitialFrame_ShouldBeChanged()
        {
            var detector = new FrameChangeDetector { SampleScale = 2 };
            int width = 100, height = 50;
            byte[] frame = new byte[width * height * 4];

            var result = detector.CheckAreaChange("test", frame, width, height);

            Assert.IsTrue(result.HasChanged, "Frame đầu tiên luôn luôn được coi là changed");
            Assert.AreEqual(1.0, result.ChangedRatio, 0.001);
        }

        [TestMethod]
        public void FrameChangeDetector_IdenticalFrame_ShouldNotBeChanged()
        {
            var detector = new FrameChangeDetector { SampleScale = 2 };
            int width = 100, height = 50;
            byte[] frame = new byte[width * height * 4];
            for (int i = 0; i < frame.Length; i++) frame[i] = (byte)(i % 256);

            // Frame 1
            detector.CheckAreaChange("test", frame, width, height);

            // Frame 2 (identical)
            var result = detector.CheckAreaChange("test", frame, width, height);

            Assert.IsFalse(result.HasChanged, "Khung hình giống hệt nhau không được báo thay đổi");
            Assert.AreEqual(0, result.ChangedPixels);
            Assert.AreEqual(0.0, result.ChangedRatio, 0.001);
        }

        [TestMethod]
        public void FrameChangeDetector_SignificantChange_ShouldDetectChange()
        {
            var detector = new FrameChangeDetector 
            { 
                SampleScale = 2,
                PixelDiffThreshold = 15,
                ChangedRatioThreshold = 0.015,
                MinimumChangedArea = 5
            };
            int width = 100, height = 50;
            byte[] frameA = new byte[width * height * 4];
            byte[] frameB = new byte[width * height * 4];

            for (int i = 0; i < frameA.Length; i++)
            {
                frameA[i] = 100;
                // Thay đổi 10% số pixel sang màu trắng (255)
                frameB[i] = (i % 10 == 0) ? (byte)255 : (byte)100;
            }

            detector.CheckAreaChange("test", frameA, width, height);
            var result = detector.CheckAreaChange("test", frameB, width, height);

            Assert.IsTrue(result.HasChanged, "Thay đổi 10% pixel phải được phát hiện");
            Assert.IsTrue(result.ChangedRatio > 0.05, $"Tỷ lệ thay đổi thực tế: {result.ChangedRatio}");
        }

        [TestMethod]
        public void FrameChangeDetector_TinyNoise_ShouldBeIgnored()
        {
            var detector = new FrameChangeDetector 
            { 
                SampleScale = 2,
                PixelDiffThreshold = 20,
                ChangedRatioThreshold = 0.02, // 2%
                MinimumChangedArea = 10
            };
            int width = 100, height = 50;
            byte[] frameA = new byte[width * height * 4];
            byte[] frameB = new byte[width * height * 4];

            for (int i = 0; i < frameA.Length; i++)
            {
                frameA[i] = 100;
                frameB[i] = 100;
            }

            // Chỉ thay đổi 2 pixel duy nhất (dưới ngưỡng 10 pixel tối thiểu)
            frameB[0] = 255;
            frameB[4] = 255;

            detector.CheckAreaChange("test", frameA, width, height);
            var result = detector.CheckAreaChange("test", frameB, width, height);

            Assert.IsFalse(result.HasChanged, "Nhiễu 2 pixel nhỏ lẻ phải bị bỏ qua");
        }

        [TestMethod]
        public void FrameChangeDetector_MultiArea_ShouldBeIsolated()
        {
            var detector = new FrameChangeDetector { SampleScale = 2 };
            int width = 100, height = 50;
            byte[] frameA = new byte[width * height * 4];
            byte[] frameB = new byte[width * height * 4];

            for (int i = 0; i < frameA.Length; i++)
            {
                frameA[i] = 50;
                frameB[i] = (byte)(i % 5 == 0 ? 250 : 50);
            }

            // Đăng ký frame cho cả area1 và area2
            detector.CheckAreaChange("area1", frameA, width, height);
            detector.CheckAreaChange("area2", frameA, width, height);

            // Chỉ thay đổi ở area1
            var resArea1 = detector.CheckAreaChange("area1", frameB, width, height);
            var resArea2 = detector.CheckAreaChange("area2", frameA, width, height);

            Assert.IsTrue(resArea1.HasChanged, "Area1 phải báo changed");
            Assert.IsFalse(resArea2.HasChanged, "Area2 không đổi phải báo false dù Area1 đổi");
        }

        [TestMethod]
        public void FrameChangeDetector_Invalidate_ShouldForceNextChange()
        {
            var detector = new FrameChangeDetector { SampleScale = 2 };
            int width = 100, height = 50;
            byte[] frame = new byte[width * height * 4];

            detector.CheckAreaChange("test", frame, width, height);
            Assert.IsFalse(detector.CheckAreaChange("test", frame, width, height).HasChanged);

            // Invalidate
            detector.Invalidate("test");

            var result = detector.CheckAreaChange("test", frame, width, height);
            Assert.IsTrue(result.HasChanged, "Sau Invalidate(), frame tiếp theo phải báo changed");
        }

        // ═══════════════════════════════════════════════════════════════════
        // 2. CaptureBufferPool Tests
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void CaptureBufferPool_ReusesSameBuffer_WhenDimensionMatches()
        {
            using var pool = new CaptureBufferPool();
            int w = 80, h = 40;

            var buf1 = pool.Capture("slot1", 0, 0, w, h);
            var buf2 = pool.Capture("slot1", 0, 0, w, h);

            Assert.IsNotNull(buf1);
            Assert.IsNotNull(buf2);
            Assert.AreEqual(w * h * 4, buf1.Length);
            Assert.AreSame(buf1, buf2, "Buffer phải được tái sử dụng cùng tham chiếu byte[]");
        }

        [TestMethod]
        public void CaptureBufferPool_Reallocates_WhenDimensionChanges()
        {
            using var pool = new CaptureBufferPool();

            var buf1 = pool.Capture("slot1", 0, 0, 50, 50);
            var buf2 = pool.Capture("slot1", 0, 0, 100, 60);

            Assert.AreEqual(50 * 50 * 4, buf1.Length);
            Assert.AreEqual(100 * 60 * 4, buf2.Length);
            Assert.AreNotSame(buf1, buf2, "Kích thước đổi thì phải cấp phát buffer mới đúng size");
        }

        [TestMethod]
        public void CaptureBufferPool_DifferentSlots_AreIsolated()
        {
            using var pool = new CaptureBufferPool();

            var bufA = pool.Capture("slotA", 0, 0, 60, 40);
            var bufB = pool.Capture("slotB", 0, 0, 60, 40);

            Assert.AreNotSame(bufA, bufB, "Hai slot khác nhau phải có 2 buffer riêng biệt");
        }

        // ═══════════════════════════════════════════════════════════════════
        // 3. PerformanceMetrics Tests
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void PerformanceMetrics_CalculatesSkipRatioCorrectly()
        {
            var metrics = new PerformanceMetrics();

            // 8 frames skipped, 2 frames executed -> 80% skip rate
            for (int i = 0; i < 8; i++)
            {
                metrics.RecordFrameChecked(ocrSkipped: true, captureMs: 2.0, diffMs: 0.2);
            }
            for (int i = 0; i < 2; i++)
            {
                metrics.RecordFrameChecked(ocrSkipped: false, captureMs: 2.5, diffMs: 0.3);
                metrics.RecordOcrExecuted(ocrMs: 35.0);
            }

            Assert.AreEqual(10, metrics.TotalFramesChecked);
            Assert.AreEqual(8, metrics.OcrSkippedCount);
            Assert.AreEqual(2, metrics.OcrExecutedCount);
            Assert.AreEqual(80.0, metrics.OcrSkipPercentage, 0.01);
            Assert.IsTrue(metrics.AvgCaptureLatencyMs > 0);
            Assert.IsTrue(metrics.AvgDiffLatencyMs > 0);
            Assert.IsTrue(metrics.AvgOcrLatencyMs > 0);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 4. CaptureBenchmark Execution Test
        // ═══════════════════════════════════════════════════════════════════

        [TestMethod]
        public void CaptureBenchmark_RunsSuccessfully()
        {
            var report = CaptureBenchmark.RunBenchmark(roiWidth: 200, roiHeight: 100, iterations: 15);

            Assert.IsNotNull(report);
            Assert.IsTrue(report.AvgCaptureLatencyMs >= 0);
            Assert.IsTrue(report.MaxCaptureFps > 0);
            Assert.IsTrue(report.MemoryReductionFactor >= 1.0);
            Assert.IsTrue(report.DiffSpeedupMultiplier > 0);
            Assert.IsFalse(string.IsNullOrWhiteSpace(report.FormattedSummary));
        }
    }
}
