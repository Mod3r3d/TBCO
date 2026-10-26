using System;
using System.Threading;

namespace TranslateBot.Diagnostics
{
    /// <summary>
    /// Lưu trữ các chỉ số hiệu năng theo thời gian thực (Section 42 & Stage 4 của TBCO plan, P1-10).
    /// Áp dụng Interlocked cho các counter và lock đồng bộ cho các giá trị moving average để đảm bảo thread-safety.
    /// </summary>
    public class PerformanceMetrics
    {
        private long _totalFramesChecked;
        private long _ocrSkippedCount;
        private long _ocrExecutedCount;
        private long _totalTranslationRequests;
        private long _rateLimit429Count;
        private long _cacheHitCount;
        private long _cacheMissCount;
        private long _droppedJobsCount;

        private readonly object _calcLock = new();

        // Moving averages (ms)
        public double AvgCaptureLatencyMs { get; set; }
        public double AvgDiffLatencyMs { get; set; }
        public double AvgOcrLatencyMs { get; set; }
        public double AvgProviderLatencyMs { get; set; }
        public double LoopFps { get; set; }

        public double LastChangeRatio { get; set; }
        public bool IsTransitionBurstActive { get; set; }

        // Translation and queue metrics
        public long TotalTranslationRequests => Interlocked.Read(ref _totalTranslationRequests);
        public long RateLimit429Count => Interlocked.Read(ref _rateLimit429Count);
        public long KeyRotationsCount { get; set; }
        public long CacheHitCount => Interlocked.Read(ref _cacheHitCount);
        public long CacheMissCount => Interlocked.Read(ref _cacheMissCount);
        public int ActiveConcurrency { get; set; } = 3;
        public int QueueDepth { get; set; }
        public long DroppedJobsCount => Interlocked.Read(ref _droppedJobsCount);

        public double CacheHitRate
        {
            get
            {
                long hits = CacheHitCount;
                long total = hits + CacheMissCount;
                return total > 0 ? ((double)hits / total) * 100.0 : 0.0;
            }
        }

        public long TotalFramesChecked => Interlocked.Read(ref _totalFramesChecked);
        public long OcrSkippedCount => Interlocked.Read(ref _ocrSkippedCount);
        public long OcrExecutedCount => Interlocked.Read(ref _ocrExecutedCount);

        public double OcrSkipPercentage
        {
            get
            {
                long total = TotalFramesChecked;
                return total > 0 ? ((double)OcrSkippedCount / total) * 100.0 : 0.0;
            }
        }

        public void RecordFrameChecked(bool ocrSkipped, double captureMs, double diffMs)
        {
            Interlocked.Increment(ref _totalFramesChecked);
            if (ocrSkipped)
            {
                Interlocked.Increment(ref _ocrSkippedCount);
            }
            else
            {
                Interlocked.Increment(ref _ocrExecutedCount);
            }

            lock (_calcLock)
            {
                // Exponential moving average (alpha = 0.15)
                AvgCaptureLatencyMs = AvgCaptureLatencyMs == 0 ? captureMs : (AvgCaptureLatencyMs * 0.85 + captureMs * 0.15);
                AvgDiffLatencyMs = AvgDiffLatencyMs == 0 ? diffMs : (AvgDiffLatencyMs * 0.85 + diffMs * 0.15);
            }
        }

        public void RecordOcrExecuted(double ocrMs)
        {
            lock (_calcLock)
            {
                AvgOcrLatencyMs = AvgOcrLatencyMs == 0 ? ocrMs : (AvgOcrLatencyMs * 0.85 + ocrMs * 0.15);
            }
        }

        public void RecordTranslationRequest(double latencyMs, bool isSuccess, bool isRateLimit = false)
        {
            Interlocked.Increment(ref _totalTranslationRequests);
            if (isRateLimit)
            {
                Interlocked.Increment(ref _rateLimit429Count);
            }

            if (isSuccess || latencyMs > 0)
            {
                lock (_calcLock)
                {
                    AvgProviderLatencyMs = AvgProviderLatencyMs == 0 
                        ? latencyMs 
                        : (AvgProviderLatencyMs * 0.85 + latencyMs * 0.15);
                }
            }
        }

        public void RecordCacheHit() => Interlocked.Increment(ref _cacheHitCount);
        public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMissCount);
        public void RecordJobDropped() => Interlocked.Increment(ref _droppedJobsCount);

        public void Reset()
        {
            Interlocked.Exchange(ref _totalFramesChecked, 0);
            Interlocked.Exchange(ref _ocrSkippedCount, 0);
            Interlocked.Exchange(ref _ocrExecutedCount, 0);
            Interlocked.Exchange(ref _totalTranslationRequests, 0);
            Interlocked.Exchange(ref _rateLimit429Count, 0);
            Interlocked.Exchange(ref _cacheHitCount, 0);
            Interlocked.Exchange(ref _cacheMissCount, 0);
            Interlocked.Exchange(ref _droppedJobsCount, 0);

            lock (_calcLock)
            {
                AvgCaptureLatencyMs = 0;
                AvgDiffLatencyMs = 0;
                AvgOcrLatencyMs = 0;
                AvgProviderLatencyMs = 0;
                LoopFps = 0;
                LastChangeRatio = 0;
                IsTransitionBurstActive = false;
                KeyRotationsCount = 0;
                ActiveConcurrency = 3;
                QueueDepth = 0;
            }
        }

        public PerformanceSnapshot CreateSnapshot()
        {
            lock (_calcLock)
            {
                return new PerformanceSnapshot(
                    LoopFps,
                    OcrSkipPercentage,
                    AvgCaptureLatencyMs,
                    AvgDiffLatencyMs,
                    AvgOcrLatencyMs,
                    AvgProviderLatencyMs,
                    CacheHitRate,
                    RateLimit429Count,
                    DroppedJobsCount,
                    ActiveConcurrency,
                    QueueDepth,
                    TotalTranslationRequests);
            }
        }

        public override string ToString()
        {
            return $"FPS: {LoopFps:F1} | Skip OCR: {OcrSkipPercentage:F0}% | Cap: {AvgCaptureLatencyMs:F1}ms | Diff: {AvgDiffLatencyMs:F2}ms | OCR: {AvgOcrLatencyMs:F0}ms | Trans: {AvgProviderLatencyMs:F0}ms | Cache: {CacheHitRate:F0}% | 429: {RateLimit429Count}";
        }
    }

    public readonly record struct PerformanceSnapshot(
        double LoopFps,
        double OcrSkipPercentage,
        double AvgCaptureLatencyMs,
        double AvgDiffLatencyMs,
        double AvgOcrLatencyMs,
        double AvgProviderLatencyMs,
        double CacheHitRate,
        long RateLimit429Count,
        long DroppedJobsCount,
        int ActiveConcurrency,
        int QueueDepth,
        long TotalRequests);
}
