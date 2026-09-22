using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TranslateBot.Capture;

namespace TranslateBot.Diagnostics
{
    public record BenchmarkReport(
        double AvgCaptureLatencyMs,
        double MinCaptureLatencyMs,
        double MaxCaptureLatencyMs,
        double MaxCaptureFps,
        long LegacyAllocatedBytes,
        long ReusedAllocatedBytes,
        double MemoryReductionFactor,
        double LegacyDiffLatencyMs,
        double OptimizedDiffLatencyMs,
        double DiffSpeedupMultiplier,
        string FormattedSummary);

    /// <summary>
    /// Công cụ benchmark tự động cho Capture, Memory Allocation và Frame Difference (Stage 4).
    /// </summary>
    public static class CaptureBenchmark
    {
        public static async Task<BenchmarkReport> RunBenchmarkAsync(int roiWidth = 800, int roiHeight = 250, int iterations = 50)
        {
            return await Task.Run(() => RunBenchmark(roiWidth, roiHeight, iterations));
        }

        public static BenchmarkReport RunBenchmark(int roiWidth = 800, int roiHeight = 250, int iterations = 50)
        {
            if (roiWidth <= 0) roiWidth = 800;
            if (roiHeight <= 0) roiHeight = 250;
            if (iterations < 10) iterations = 10;

            // Đảm bảo tọa độ nằm trong màn hình chính
            int screenW = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
            int screenH = (int)System.Windows.SystemParameters.PrimaryScreenHeight;
            int capX = Math.Max(0, (screenW - roiWidth) / 2);
            int capY = Math.Max(0, (screenH - roiHeight) / 2);

            // ═══════════════════════════════════════════════════════════════
            // Test 1: Capture Latency & FPS (Buffer Reuse)
            // ═══════════════════════════════════════════════════════════════
            using var captureService = new CaptureService();
            
            // Warmup
            captureService.CaptureRegion(capX, capY, roiWidth, roiHeight, "bench_slot");

            double totalCapMs = 0;
            double minCapMs = double.MaxValue;
            double maxCapMs = double.MinValue;

            for (int i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                var frame = captureService.CaptureRegion(capX, capY, roiWidth, roiHeight, "bench_slot");
                sw.Stop();

                double ms = sw.Elapsed.TotalMilliseconds;
                totalCapMs += ms;
                if (ms < minCapMs) minCapMs = ms;
                if (ms > maxCapMs) maxCapMs = ms;
            }

            double avgCapMs = totalCapMs / iterations;
            double maxFps = avgCapMs > 0 ? 1000.0 / avgCapMs : 0;

            // ═══════════════════════════════════════════════════════════════
            // Test 2: Memory Allocation (Legacy vs Buffer Reuse)
            // ═══════════════════════════════════════════════════════════════
            const int memIterations = 40;

            // Legacy allocation test
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocBeforeLegacy = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < memIterations; i++)
            {
                using var bmp = new Bitmap(roiWidth, roiHeight, PixelFormat.Format32bppArgb);
                try
                {
                    using var g = Graphics.FromImage(bmp);
                    g.CopyFromScreen(capX, capY, 0, 0, new Size(roiWidth, roiHeight), CopyPixelOperation.SourceCopy);
                }
                catch { }
                var rect = new Rectangle(0, 0, roiWidth, roiHeight);
                var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
                byte[] legacyArr = new byte[bytes];
                Marshal.Copy(bmpData.Scan0, legacyArr, 0, bytes);
                bmp.UnlockBits(bmpData);
            }
            long legacyAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocBeforeLegacy;

            // Buffer reuse allocation test
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocBeforeReused = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < memIterations; i++)
            {
                captureService.CaptureRegion(capX, capY, roiWidth, roiHeight, "bench_slot");
            }
            long reusedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocBeforeReused;
            double memReduction = legacyAllocatedBytes > 0 
                ? (double)legacyAllocatedBytes / Math.Max(1, reusedAllocatedBytes) 
                : 1.0;

            // ═══════════════════════════════════════════════════════════════
            // Test 3: Frame Difference Speed (Legacy Full-pixel vs Optimized)
            // ═══════════════════════════════════════════════════════════════
            // Chuẩn bị 2 frame mẫu có 3% pixel khác nhau
            int frameBytes = roiWidth * roiHeight * 4;
            byte[] frameA = new byte[frameBytes];
            byte[] frameB = new byte[frameBytes];
            for (int i = 0; i < frameBytes; i++)
            {
                frameA[i] = (byte)(i % 250);
                // Tạo khác biệt ở 3% pixel
                frameB[i] = (i % 33 == 0) ? (byte)((frameA[i] + 50) % 255) : frameA[i];
            }

            const int diffIterations = 100;

            // Legacy full-pixel loop test
            var swLegacyDiff = Stopwatch.StartNew();
            for (int it = 0; it < diffIterations; it++)
            {
                int changedPixels = 0;
                for (int i = 0; i < frameBytes; i += 4)
                {
                    if (Math.Abs(frameB[i] - frameA[i]) > 15 ||
                        Math.Abs(frameB[i + 1] - frameA[i + 1]) > 15 ||
                        Math.Abs(frameB[i + 2] - frameA[i + 2]) > 15)
                    {
                        changedPixels++;
                    }
                }
            }
            swLegacyDiff.Stop();
            double avgLegacyDiffMs = swLegacyDiff.Elapsed.TotalMilliseconds / diffIterations;

            // Optimized downsampled grayscale test
            var detector = new FrameChangeDetector { SampleScale = 4 };
            detector.CheckAreaChange("bench", frameA, roiWidth, roiHeight); // warmup

            var swOptDiff = Stopwatch.StartNew();
            for (int it = 0; it < diffIterations; it++)
            {
                detector.CheckAreaChange("bench", frameB, roiWidth, roiHeight);
            }
            swOptDiff.Stop();
            double avgOptDiffMs = swOptDiff.Elapsed.TotalMilliseconds / diffIterations;

            double diffSpeedup = avgLegacyDiffMs > 0 && avgOptDiffMs > 0 
                ? avgLegacyDiffMs / avgOptDiffMs 
                : 1.0;

            // ═══════════════════════════════════════════════════════════════
            // Build Formatted Report
            // ═══════════════════════════════════════════════════════════════
            var sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║                TRANSLATE BOT — STAGE 4 BENCHMARK REPORT              ║");
            sb.AppendLine("╠══════════════════════════════════════════════════════════════════════╣");
            sb.AppendLine($"║ ROI Dimension: {roiWidth}x{roiHeight} px | Iterations: {iterations} frames");
            sb.AppendLine("╟──────────────────────────────────────────────────────────────────────╢");
            sb.AppendLine("║ 1. CAPTURE LATENCY & THROUGHPUT (Buffer Reuse):");
            sb.AppendLine($"║    • Average Latency : {avgCapMs:F2} ms");
            sb.AppendLine($"║    • Min / Max       : {minCapMs:F2} ms / {maxCapMs:F2} ms");
            sb.AppendLine($"║    • Max Capture FPS : ~{maxFps:F0} FPS");
            sb.AppendLine("╟──────────────────────────────────────────────────────────────────────╢");
            sb.AppendLine("║ 2. MEMORY ALLOCATION (LOH & Heap Churn):");
            sb.AppendLine($"║    • Legacy (no pool): {legacyAllocatedBytes / 1024.0 / 1024.0:F2} MB allocated ({memIterations} frames)");
            sb.AppendLine($"║    • Buffer Reuse    : {reusedAllocatedBytes / 1024.0:F2} KB allocated ({memIterations} frames)");
            sb.AppendLine($"║    • Memory Reduction: ~{memReduction:F0}x less heap allocation!");
            sb.AppendLine("╟──────────────────────────────────────────────────────────────────────╢");
            sb.AppendLine("║ 3. FRAME DIFFERENCE SPEEDUP (Downsampling & Grayscale):");
            sb.AppendLine($"║    • Legacy Diff loop: {avgLegacyDiffMs:F3} ms / check");
            sb.AppendLine($"║    • Optimized Diff  : {avgOptDiffMs:F3} ms / check");
            sb.AppendLine($"║    • Speedup Multiplier: {diffSpeedup:F1}x faster!");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════════════╝");

            return new BenchmarkReport(
                avgCapMs,
                minCapMs,
                maxCapMs,
                maxFps,
                legacyAllocatedBytes,
                reusedAllocatedBytes,
                memReduction,
                avgLegacyDiffMs,
                avgOptDiffMs,
                diffSpeedup,
                sb.ToString());
        }
    }
}
