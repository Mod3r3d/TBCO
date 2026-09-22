using System;

namespace TranslateBot.Diagnostics
{
    /// <summary>
    /// Lưu trữ các chỉ số hiệu năng theo thời gian thực (Section 42 & Stage 4 của TBCO plan).
    /// </summary>
    public class PerformanceMetrics
    {
        private long _totalFramesChecked;
        private long _ocrSkippedCount;
        private long _ocrExecutedCount;

        // Moving averages (ms)
        public double AvgCaptureLatencyMs { get; set; }
        public double AvgDiffLatencyMs { get; set; }
        public double AvgOcrLatencyMs { get; set; }
        public double AvgProviderLatencyMs { get; set; }
        public double LoopFps { get; set; }

        public double LastChangeRatio { get; set; }
        public bool IsTransitionBurstActive { get; set; }

        // Translation and queue metrics
        public long TotalTranslationRequests { get; private set; }
        public long RateLimit429Count { get; private set; }
        public long KeyRotationsCount { get; set; }
        public long CacheHitCount { get; private set; }
        public long CacheMissCount { get; private set; }
        public int ActiveConcurrency { get; set; } = 3;
        public int QueueDepth { get; set; }
        public long DroppedJobsCount { get; private set; }

        public double CacheHitRate =>
            (CacheHitCount + CacheMissCount) > 0 
                ? ((double)CacheHitCount / (CacheHitCount + CacheMissCount)) * 100.0 
                : 0.0;

        public long TotalFramesChecked => _totalFramesChecked;
        public long OcrSkippedCount => _ocrSkippedCount;
        public long OcrExecutedCount => _ocrExecutedCount;

        public double OcrSkipPercentage => 
            _totalFramesChecked > 0 ? ((double)_ocrSkippedCount / _totalFramesChecked) * 100.0 : 0.0;

        public void RecordFrameChecked(bool ocrSkipped, double captureMs, double diffMs)
        {
            _totalFramesChecked++;
            if (ocrSkipped)
            {
                _ocrSkippedCount++;
            }
            else
            {
                _ocrExecutedCount++;
            }

            // Exponential moving average (alpha = 0.15)
            AvgCaptureLatencyMs = AvgCaptureLatencyMs == 0 ? captureMs : (AvgCaptureLatencyMs * 0.85 + captureMs * 0.15);
            AvgDiffLatencyMs = AvgDiffLatencyMs == 0 ? diffMs : (AvgDiffLatencyMs * 0.85 + diffMs * 0.15);
        }

        public void RecordOcrExecuted(double ocrMs)
        {
            AvgOcrLatencyMs = AvgOcrLatencyMs == 0 ? ocrMs : (AvgOcrLatencyMs * 0.85 + ocrMs * 0.15);
        }

        public void RecordTranslationRequest(double latencyMs, bool isSuccess, bool isRateLimit = false)
        {
            TotalTranslationRequests++;
            if (isRateLimit)
            {
                RateLimit429Count++;
            }

            if (isSuccess || latencyMs > 0)
            {
                AvgProviderLatencyMs = AvgProviderLatencyMs == 0 
                    ? latencyMs 
                    : (AvgProviderLatencyMs * 0.85 + latencyMs * 0.15);
            }
        }

        public void RecordCacheHit() => CacheHitCount++;
        public void RecordCacheMiss() => CacheMissCount++;
        public void RecordJobDropped() => DroppedJobsCount++;

        public void Reset()
        {
            _totalFramesChecked = 0;
            _ocrSkippedCount = 0;
            _ocrExecutedCount = 0;
            AvgCaptureLatencyMs = 0;
            AvgDiffLatencyMs = 0;
            AvgOcrLatencyMs = 0;
            AvgProviderLatencyMs = 0;
            LoopFps = 0;
            LastChangeRatio = 0;
            IsTransitionBurstActive = false;

            TotalTranslationRequests = 0;
            RateLimit429Count = 0;
            KeyRotationsCount = 0;
            CacheHitCount = 0;
            CacheMissCount = 0;
            ActiveConcurrency = 3;
            QueueDepth = 0;
            DroppedJobsCount = 0;
        }

        public override string ToString()
        {
            return $"FPS: {LoopFps:F1} | Skip OCR: {OcrSkipPercentage:F0}% | Cap: {AvgCaptureLatencyMs:F1}ms | Diff: {AvgDiffLatencyMs:F2}ms | OCR: {AvgOcrLatencyMs:F0}ms | Trans: {AvgProviderLatencyMs:F0}ms | Cache: {CacheHitRate:F0}% | 429: {RateLimit429Count}";
        }
    }
}
