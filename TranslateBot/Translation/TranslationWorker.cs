using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TranslateBot.Diagnostics;
using TranslateBot.Dialogue;
using TranslateBot.Infrastructure;

namespace TranslateBot.Translation
{
    public class TranslationWorker : IDisposable
    {
        public const int DefaultQueueCapacity = 32;

        private readonly ITranslationProvider _provider;
        private readonly int _queueCapacity;
        private Channel<DialogueJob> _jobQueue;
        private readonly ReorderBuffer _reorderBuffer = new();
        private CancellationTokenSource? _cts;

        // Stage 5: Translation Memory (exact + fuzzy LRU cache)
        private readonly TranslationMemory _translationMemory = new(1000);

        // Phase 1 (P0): Generation/Session ID quản lý theo phiên
        private long _currentGenerationId = 0;
        private int _enqueuedJobCount = 0;

        // Phase 15 & 19: Performance Metrics & Concurrency Control
        public PerformanceMetrics? Metrics { get; set; }

        private const int MaxWorkerCount = 3;
        private int _currentConcurrency = 3;
        private int _consecutiveSuccesses = 0;
        private readonly object _concurrencyLock = new();
        private readonly DynamicConcurrencyLimiter _concurrencyLimiter = new(3);

        // Phase 20: Request Coalescing & Stale Prefetch Tracking
        private DialogueJob? _lastPrefetchJob;

        // Quản lý các job confirmed CHƯA xử lý xong để có thể huỷ khi người chơi tua nhanh
        private readonly List<DialogueJob> _pendingConfirmedJobs = new();
        private readonly object _pendingLock = new();

        // Callback để gửi kết quả về cho giao diện (WPF) hiển thị theo đúng thứ tự (qua ReorderBuffer)
        public Action<DialogueJob, string>? OnTranslationCompleted
        {
            get => _reorderBuffer.OnOrderedResult;
            set => _reorderBuffer.OnOrderedResult = value;
        }

        public TranslationMemory Memory => _translationMemory;
        public int CurrentConcurrency => _currentConcurrency;
        public string ActiveProfileId { get; set; } = "global";
        public long CurrentGenerationId => _currentGenerationId;
        public int QueueCapacity => _queueCapacity;

        public TranslationWorker(ITranslationProvider provider, int queueCapacity = DefaultQueueCapacity)
        {
            _provider = provider;
            _queueCapacity = queueCapacity > 0 ? queueCapacity : DefaultQueueCapacity;
            _jobQueue = CreateQueue(_queueCapacity);
        }

        private static Channel<DialogueJob> CreateQueue(int capacity)
        {
            return Channel.CreateBounded<DialogueJob>(new BoundedChannelOptions(capacity)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        private Task[] _workerTasks = Array.Empty<Task>();

        public void Start()
        {
            _jobQueue = CreateQueue(_queueCapacity);
            _reorderBuffer.Reset(_currentGenerationId);
            _cts = new CancellationTokenSource();
            _concurrencyLimiter.MaxConcurrency = _currentConcurrency;
            if (Metrics != null) Metrics.ActiveConcurrency = _currentConcurrency;

            _workerTasks = new Task[MaxWorkerCount];
            for (int i = 0; i < MaxWorkerCount; i++)
            {
                _workerTasks[i] = Task.Run(() => ProcessQueueAsync(_cts.Token));
            }
        }

        public void Stop()
        {
            _jobQueue.Writer.TryComplete();
        }

        public async Task StopAndWaitAsync(TimeSpan gracePeriod)
        {
            Stop();
            if (_workerTasks.Length == 0) return;

            var allWorkers = Task.WhenAll(_workerTasks);
            var completed = await Task.WhenAny(allWorkers, Task.Delay(gracePeriod));
            if (completed != allWorkers)
            {
                _cts?.Cancel();
            }
        }

        public void AdvanceGeneration()
        {
            long nextGen = Interlocked.Increment(ref _currentGenerationId);
            _reorderBuffer.AdvanceGeneration(nextGen);

            lock (_pendingLock)
            {
                foreach (var job in _pendingConfirmedJobs)
                {
                    job.IsCancelled = true;
                }
                _pendingConfirmedJobs.Clear();
            }

            if (_lastPrefetchJob != null)
            {
                _lastPrefetchJob.IsCancelled = true;
                _lastPrefetchJob = null;
            }

            Interlocked.Exchange(ref _enqueuedJobCount, 0);
            if (Metrics != null) Metrics.QueueDepth = 0;
            AppLogger.Info($"[GENERATION_ADVANCED] The he moi #{nextGen} duoc khoi tao.");
        }

        public void EnqueueJob(DialogueJob job)
        {
            if (job.IsTranslated) return;

            job.GenerationId = _currentGenerationId;

            // Phase 20: Request Coalescing & Hủy Stale Prefetch
            if (job.IsPrefetch)
            {
                job.Priority = -1;
                _lastPrefetchJob = job;
            }
            else
            {
                job.Priority = 1;
                // Nếu có job prefetch đang chờ, câu thoại confirmed chính thức này sẽ thay thế nó
                if (_lastPrefetchJob != null && !_lastPrefetchJob.IsTranslated && !_lastPrefetchJob.IsCancelled)
                {
                    _lastPrefetchJob.IsCancelled = true;
                    Metrics?.RecordJobDropped();
                    AppLogger.Info($"[COALESCE] Huy job prefetch #{_lastPrefetchJob.SequenceId} do da co cau chinh thuc #{job.SequenceId}");
                    _lastPrefetchJob = null;
                }

                // Quản lý hàng đợi confirmed: chỉ hủy bớt câu cũ nếu hàng đợi dồn ứ quá lớn (SKIP/tua nhanh >= 4 câu)
                lock (_pendingLock)
                {
                    if (_pendingConfirmedJobs.Count >= 4)
                    {
                        var toDrop = _pendingConfirmedJobs.Take(_pendingConfirmedJobs.Count - 2).ToList();
                        foreach (var pending in toDrop)
                        {
                            if (pending.SequenceId < job.SequenceId && !pending.IsTranslated && !pending.IsCancelled)
                            {
                                pending.IsCancelled = true;
                                Metrics?.RecordJobDropped();
                                AppLogger.Info($"[COALESCE_FAST_FORWARD] Bo qua cau cu #{pending.SequenceId} do nguoi choi tua nhanh hoi thoai.");
                            }
                        }
                    }
                    _pendingConfirmedJobs.RemoveAll(j => j.IsTranslated || j.IsCancelled);
                    _pendingConfirmedJobs.Add(job);
                }
            }

            // Stage 5 & Giai đoạn 4: Check Translation Memory có xét Game Profile trước khi gửi API
            string activeProfile = job.Context?.GameName ?? ActiveProfileId;
            string? cached = _translationMemory.TryGetTranslation(job.NormalizedText, activeProfile);
            if (cached != null)
            {
                Metrics?.RecordCacheHit();
                job.IsTranslated = true;
                job.State = DialogueState.Translated;
                job.IsCacheHit = true;
                job.LatencyMs = 0;
                job.ProviderName = "MemoryCache";
                AppLogger.Info($"[TRANSLATION_CACHED] [{activeProfile}] #{job.SequenceId}");
                _reorderBuffer.Submit(job, cached);
                return;
            }

            Metrics?.RecordCacheMiss();
            job.State = DialogueState.Queued;

            // Phase 1 (P0): Bounded Queue Policy (Latest-Relevant)
            bool written = _jobQueue.Writer.TryWrite(job);
            if (!written)
            {
                // Hàng đợi đầy
                if (job.IsPrefetch)
                {
                    // Nếu là prefetch, loại bỏ ngay lập tức để không làm nghẽn hàng đợi chính
                    job.IsCancelled = true;
                    Metrics?.RecordJobDropped();
                    AppLogger.Info($"[QUEUE_FULL_DROP_PREFETCH] Hang doi day, bo qua prefetch #{job.SequenceId}.");
                    return;
                }

                // Nếu là câu thoại chính thức (Confirmed):
                // Dọn dẹp bất kỳ stale prefetch nào còn sót lại
                if (_lastPrefetchJob != null && !_lastPrefetchJob.IsTranslated)
                {
                    _lastPrefetchJob.IsCancelled = true;
                    _lastPrefetchJob = null;
                }

                // Thử ghi lại lần 2
                written = _jobQueue.Writer.TryWrite(job);
                if (!written)
                {
                    // Hàng đợi vẫn đầy: Áp dụng Latest-Relevant policy bằng Task.Run không chặn thread gọi
                    Metrics?.RecordJobDropped();
                    AppLogger.Warn($"[QUEUE_SATURATED] Hang doi day ({_queueCapacity}), ap dung backpressure cho #{job.SequenceId}.");
                    Task.Run(async () =>
                    {
                        try
                        {
                            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await _jobQueue.Writer.WriteAsync(job, timeoutCts.Token);
                            Interlocked.Increment(ref _enqueuedJobCount);
                            if (Metrics != null) Metrics.QueueDepth = _enqueuedJobCount;
                        }
                        catch (Exception)
                        {
                            job.IsCancelled = true;
                            AppLogger.Error($"[QUEUE_OVERFLOW_DROP] Khong the chen #{job.SequenceId} sau timeout, bo qua.");
                        }
                    });
                    return;
                }
            }

            int currentDepth = Interlocked.Increment(ref _enqueuedJobCount);
            if (Metrics != null) Metrics.QueueDepth = currentDepth;
            AppLogger.Info($"[TRANSLATION_QUEUED] #{job.SequenceId}{(job.IsPrefetch ? " (prefetch)" : "")} (Queue: {currentDepth}/{_queueCapacity})");
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            try
            {
                await foreach (var job in _jobQueue.Reader.ReadAllAsync(token))
                {
                    int remaining = Interlocked.Decrement(ref _enqueuedJobCount);
                    if (Metrics != null) Metrics.QueueDepth = Math.Max(0, remaining);

                    // Kiểm tra Generation ID và trạng thái Cancelled
                    if (job.GenerationId != _currentGenerationId)
                    {
                        AppLogger.Info($"[STALE_GENERATION_SKIP] Bo qua job #{job.SequenceId} (Gen {job.GenerationId} != {_currentGenerationId})");
                        continue;
                    }

                    if (job.IsCancelled)
                    {
                        AppLogger.Info($"[JOB_CANCELLED_SKIP] Bo qua xu ly job #{job.SequenceId}");
                        continue;
                    }

                    // Phase 19: Giới hạn Adaptive Concurrency bằng Limiter
                    await _concurrencyLimiter.WaitAsync(token);

                    var sw = Stopwatch.StartNew();
                    bool isSuccess = false;
                    bool isRateLimit = false;

                    try
                    {
                        // Kiểm tra lại sau khi chờ semaphore
                        if (job.GenerationId != _currentGenerationId || job.IsCancelled)
                        {
                            continue;
                        }

                        job.State = DialogueState.Translating;
                        string translatedText;
                        TranslationResult transResult;

                        var req = new TranslationRequest(
                            job.NormalizedText, 
                            job.Context, 
                            sequenceId: job.SequenceId, 
                            cancellationToken: token);

                        // Stage 5 & Phase 1/2: Long dialogue handling với TranslationResult có cấu trúc
                        if (LongDialogueHandler.IsLongDialogue(job.NormalizedText))
                        {
                            transResult = await TranslateLongDialogueAsync(job, token);
                            translatedText = transResult.Text;
                        }
                        else
                        {
                            transResult = await _provider.TranslateRequestAsync(req);
                            translatedText = transResult.Text;
                        }

                        sw.Stop();
                        isSuccess = transResult.IsSuccess && !string.IsNullOrWhiteSpace(translatedText);
                        job.IsTranslated = isSuccess;
                        job.State = DialogueState.Translated;
                        job.IsCacheHit = transResult.IsCacheHit;
                        job.LatencyMs = transResult.LatencyMs > 0 ? transResult.LatencyMs : sw.Elapsed.TotalMilliseconds;
                        job.ProviderName = transResult.ProviderName;

                        string finalResult = isSuccess 
                            ? translatedText 
                            : $"[Chưa dịch được — Kiểm tra mạng hoặc API Key]\n{job.RawText}";

                        if (isSuccess)
                        {
                            // Stage 3 & Giai đoạn 4: Phân loại cache entry, không để fallback hoặc partial failure làm bẩn canonical cache
                            bool isDegradedFallback = transResult.IsFallback 
                                || transResult.Status == TranslationStatus.PartialFailure 
                                || (transResult.ProviderName == "GoogleWebTranslate" &&
                                    (_provider as TranslationRouter)?.PrimaryProvider != "GoogleWeb" &&
                                    (_provider as TranslationRouter)?.PrimaryProvider != "GoogleWebTranslate");

                            string jobProfile = job.Context?.GameName ?? ActiveProfileId;
                            var memEntry = new TranslationMemoryEntry
                            {
                                NormalizedText = job.NormalizedText,
                                OriginalText = job.RawText,
                                Translation = translatedText,
                                ProfileId = jobProfile,
                                Speaker = job.Context?.SpeakerName,
                                Provider = transResult.ProviderName ?? "Unknown",
                                Model = transResult.ModelName,
                                EntryType = isDegradedFallback ? CacheEntryType.FallbackDegraded : CacheEntryType.ModelOutput,
                                IsFallback = isDegradedFallback,
                                IsUserApproved = false
                            };

                            if (!isDegradedFallback)
                            {
                                _translationMemory.Store(memEntry);
                            }
                            else
                            {
                                AppLogger.Warn($"[MEMORY_SKIP_FALLBACK] Khong luu ban dich fallback/partial tu {transResult.ProviderName} vao canonical cache cua #{job.SequenceId}");
                            }

                            AppLogger.Info($"[TRANSLATION_DONE] #{job.SequenceId} ({job.ProviderName})");
                            ReportSuccess();
                        }
                        else
                        {
                            AppLogger.Error($"[TRANSLATION_FAILED] #{job.SequenceId} -> fallback canh bao");
                            ReportFailure(false);
                        }

                        // Kiểm tra lại generation và cancellation trước khi đưa lên màn hình
                        if (job.GenerationId == _currentGenerationId && !job.IsPrefetch && !job.IsCancelled)
                        {
                            _reorderBuffer.Submit(job, finalResult);
                        }
                        else if (job.IsCancelled)
                        {
                            AppLogger.Info($"[COALESCE_STALE] Cau #{job.SequenceId} dich xong nhung da bi huy tu truoc - khong hien thi.");
                        }
                    }
                    catch (Exception ex)
                    {
                        isSuccess = false;
                        isRateLimit = ex.Message.Contains("429") || ex.Message.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0;
                        AppLogger.Error($"[TRANSLATION_ERROR] #{job.SequenceId}: {ex.Message}");
                        ReportFailure(isRateLimit);

                        if (job.GenerationId == _currentGenerationId && !job.IsPrefetch && !job.IsCancelled)
                        {
                            _reorderBuffer.Submit(job, $"[Lỗi kết nối API]\n{job.RawText}");
                        }
                    }
                    finally
                    {
                        sw.Stop();
                        Metrics?.RecordTranslationRequest(sw.Elapsed.TotalMilliseconds, isSuccess, isRateLimit);
                        _concurrencyLimiter.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Worker đã dừng an toàn
            }
        }

        // Adaptive Concurrency: Khi thành công liên tục, nâng dần worker
        public void ReportSuccess()
        {
            lock (_concurrencyLock)
            {
                _consecutiveSuccesses++;
                if (_consecutiveSuccesses >= 5 && _currentConcurrency < MaxWorkerCount)
                {
                    _currentConcurrency++;
                    _concurrencyLimiter.MaxConcurrency = _currentConcurrency;
                    _consecutiveSuccesses = 0;
                    if (Metrics != null) Metrics.ActiveConcurrency = _currentConcurrency;
                    AppLogger.Info($"[CONCURRENCY_SCALED_UP] Tang luong dich dong thoi len {_currentConcurrency}");
                }
            }
        }

        // Adaptive Concurrency: Khi gặp lỗi hoặc 429, hạ ngay về 1 worker để giảm tải
        public void ReportFailure(bool isRateLimit)
        {
            lock (_concurrencyLock)
            {
                _consecutiveSuccesses = 0;
                if (_currentConcurrency > 1)
                {
                    _currentConcurrency = 1;
                    _concurrencyLimiter.MaxConcurrency = _currentConcurrency;
                    if (Metrics != null) Metrics.ActiveConcurrency = _currentConcurrency;
                    AppLogger.Warn($"[CONCURRENCY_THROTTLED] Giam luong dich dong thoi ve 1 de tranh bao loi{(isRateLimit ? " (429 Rate Limit)" : "")}");
                }
            }
        }

        // Phase 1 (P0) & Phase 2: Dịch đoạn dài có cấu trúc — phân mảnh semantic, bảo toàn provider metadata, phát hiện partial failure
        private async Task<TranslationResult> TranslateLongDialogueAsync(DialogueJob job, CancellationToken token)
        {
            var chunks = LongDialogueHandler.SplitIntoChunks(job.NormalizedText);
            var translatedChunks = new List<string>();
            var sw = Stopwatch.StartNew();
            bool anyFailed = false;
            string lastProvider = "Unknown";
            string? lastModel = null;
            bool anyFallback = false;

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var chunkReq = new TranslationRequest(
                    chunk,
                    job.Context,
                    sequenceId: job.SequenceId,
                    cancellationToken: token);

                TranslationResult chunkRes;
                try
                {
                    chunkRes = await _provider.TranslateRequestAsync(chunkReq);
                }
                catch (Exception ex)
                {
                    chunkRes = TranslationResult.CreateFailure("LongDialogue", TranslationStatus.ProviderError, ex.Message);
                }

                if (chunkRes.IsSuccess && !string.IsNullOrWhiteSpace(chunkRes.Text))
                {
                    translatedChunks.Add(chunkRes.Text);
                    lastProvider = chunkRes.ProviderName;
                    lastModel = chunkRes.ModelName;
                    if (chunkRes.IsFallback) anyFallback = true;
                }
                else
                {
                    anyFailed = true;
                    translatedChunks.Add(chunk);
                    AppLogger.Warn($"[LONG_DIALOGUE_CHUNK_FAIL] #{job.SequenceId} chunk {i + 1}/{chunks.Count} dich that bai ({chunkRes.Status}).");
                }
            }

            sw.Stop();
            string merged = LongDialogueHandler.MergeTranslations(translatedChunks);

            if (anyFailed)
            {
                return TranslationResult.CreatePartialSuccess(
                    merged,
                    lastProvider,
                    lastModel,
                    "Mot hoac nhieu doan trong cau dai chua dich duoc day du",
                    latency: sw.Elapsed.TotalMilliseconds);
            }

            var successRes = TranslationResult.CreateSuccess(
                merged,
                lastProvider,
                lastModel,
                latency: sw.Elapsed.TotalMilliseconds);

            if (anyFallback)
            {
                successRes.IsFallback = true;
            }

            return successRes;
        }

        public void Dispose()
        {
            Stop();
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { }

            _concurrencyLimiter.Dispose();
            _translationMemory.Dispose();
        }
    }
}
