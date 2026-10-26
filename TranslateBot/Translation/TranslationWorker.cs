using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly ITranslationProvider _provider;
        private Channel<DialogueJob> _jobQueue;
        private readonly ReorderBuffer _reorderBuffer = new();
        private CancellationTokenSource? _cts;

        // Stage 5: Translation Memory (exact + fuzzy LRU cache)
        private readonly TranslationMemory _translationMemory = new(1000);

        // Phase 15 & 19: Performance Metrics & Concurrency Control
        public PerformanceMetrics? Metrics { get; set; }

        private const int MaxWorkerCount = 3;
        private int _currentConcurrency = 3;
        private int _consecutiveSuccesses = 0;
        private readonly object _concurrencyLock = new();
        private readonly DynamicConcurrencyLimiter _concurrencyLimiter = new(3);

        // Phase 20: Request Coalescing & Stale Prefetch Tracking
        private DialogueJob? _lastPrefetchJob;

        // Mở rộng Coalescing cho CÂU THOẠI CHÍNH THỨC (không chỉ prefetch): nếu câu #12 (VD:
        // câu dài, đang dịch chậm) còn chưa xong mà đã có câu #14 mới hơn được xác nhận, việc
        // dịch xong #12 rồi hiển thị chỉ để bị #13/#14 đè lên gần như ngay lập tức là vô nghĩa
        // - đây chính là nguyên nhân "phần sub nhảy khá nhanh" khi câu dài làm dồn ứ hàng đợi.
        // Giữ 1 danh sách nhỏ các job confirmed CHƯA xử lý xong để có thể huỷ khi bị vượt mặt.
        private readonly List<DialogueJob> _pendingConfirmedJobs = new();
        private readonly object _pendingLock = new();

        // Callback để gửi kết quả về cho giao diện (WPF) hiển thị.
        // Gọi theo ĐÚNG THỨ TỰ SequenceId (qua ReorderBuffer).
        public Action<DialogueJob, string>? OnTranslationCompleted
        {
            get => _reorderBuffer.OnOrderedResult;
            set => _reorderBuffer.OnOrderedResult = value;
        }

        public TranslationMemory Memory => _translationMemory;
        public int CurrentConcurrency => _currentConcurrency;
        public string ActiveProfileId { get; set; } = "global";

        public TranslationWorker(ITranslationProvider provider)
        {
            _provider = provider;
            _jobQueue = CreateQueue();
        }

        private static Channel<DialogueJob> CreateQueue()
        {
            return Channel.CreateUnbounded<DialogueJob>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });
        }

        private Task[] _workerTasks = Array.Empty<Task>();

        public void Start()
        {
            _jobQueue = CreateQueue();
            _reorderBuffer.Reset();
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

        public void EnqueueJob(DialogueJob job)
        {
            if (job.IsTranslated) return;

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
                    AppLogger.Info($"[COALESCE] Hủy job prefetch #{_lastPrefetchJob.SequenceId} do đã có câu chính thức #{job.SequenceId}");
                    _lastPrefetchJob = null;
                }

                // Quản lý hàng đợi confirmed: chỉ hủy bớt câu cũ nếu hàng đợi dồn ứ quá lớn
                // (người chơi giữ nút SKIP hoặc tua nhanh hội thoại >= 4 câu)
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
                                AppLogger.Info($"[COALESCE_FAST_FORWARD] Bỏ qua câu cũ #{pending.SequenceId} do người chơi tua nhanh hội thoại.");
                            }
                        }
                    }
                    // Chỉ giữ lại các job thật sự còn đang chờ, tránh danh sách phình vô hạn
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
            AppLogger.Info($"[TRANSLATION_QUEUED] #{job.SequenceId}{(job.IsPrefetch ? " (prefetch)" : "")}");

            _jobQueue.Writer.TryWrite(job);
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            try
            {
                await foreach (var job in _jobQueue.Reader.ReadAllAsync(token))
                {
                    // Kiểm tra xem job có bị hủy bởi Coalescing không
                    if (job.IsCancelled)
                    {
                        AppLogger.Info($"[JOB_CANCELLED_SKIP] Bỏ qua xử lý job #{job.SequenceId}");
                        continue;
                    }

                    // Phase 19: Giới hạn Adaptive Concurrency bằng Semaphore
                    await _concurrencyLimiter.WaitAsync(token);

                    var sw = Stopwatch.StartNew();
                    bool isSuccess = false;
                    bool isRateLimit = false;

                    try
                    {
                        job.State = DialogueState.Translating;
                        string translatedText;
                        TranslationResult transResult;

                        var req = new TranslationRequest(
                            job.NormalizedText, 
                            job.Context, 
                            sequenceId: job.SequenceId, 
                            cancellationToken: token);

                        // Stage 5: Long dialogue handling (Section 28)
                        if (LongDialogueHandler.IsLongDialogue(job.NormalizedText))
                        {
                            translatedText = await TranslateLongDialogueAsync(job);
                            transResult = TranslationResult.CreateSuccess(
                                translatedText, 
                                (_provider as TranslationRouter)?.CurrentProviderName ?? "Gemini", 
                                latency: sw.Elapsed.TotalMilliseconds);
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
                            // Stage 3 & Giai đoạn 4: Phân loại cache entry, không để fallback thô thành canonical
                            bool isDegradedFallback = transResult.IsFallback || (transResult.ProviderName == "GoogleWebTranslate" &&
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
                                AppLogger.Warn($"[MEMORY_SKIP_FALLBACK] Không lưu bản dịch fallback từ {transResult.ProviderName} vào canonical cache của #{job.SequenceId}");
                            }

                            AppLogger.Info($"[TRANSLATION_DONE] #{job.SequenceId} ({job.ProviderName})");
                            ReportSuccess();
                        }
                        else
                        {
                            AppLogger.Error($"[TRANSLATION_FAILED] #{job.SequenceId} -> fallback cảnh báo");
                            ReportFailure(false);
                        }

                        // Prefetch: lưu cache nhưng KHÔNG hiển thị ra UI (chờ Confirmed thật sự)
                        // Cũng bỏ qua hiển thị nếu job đã bị coalesce/huỷ TRONG LÚC đang dịch dở
                        // (đã có câu mới hơn xuất hiện) - dữ liệu dịch vẫn được lưu cache phía
                        // trên (không lãng phí), chỉ là không đưa ra màn hình nữa vì đã lỗi thời.
                        if (!job.IsPrefetch && !job.IsCancelled)
                        {
                            _reorderBuffer.Submit(job, finalResult);
                        }
                        else if (job.IsCancelled)
                        {
                            AppLogger.Info($"[COALESCE_STALE] Câu #{job.SequenceId} dịch xong nhưng đã bị huỷ từ trước (lỗi thời) - không hiển thị.");
                        }
                    }
                    catch (Exception ex)
                    {
                        isSuccess = false;
                        isRateLimit = ex.Message.Contains("429") || ex.Message.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0;
                        AppLogger.Error($"[TRANSLATION_ERROR] #{job.SequenceId}: {ex.Message}");
                        ReportFailure(isRateLimit);

                        if (!job.IsPrefetch && !job.IsCancelled)
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
                // Worker đã bị dừng an toàn
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
                    AppLogger.Info($"[CONCURRENCY_SCALED_UP] Tăng luồng dịch đồng thời lên {_currentConcurrency}");
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
                    AppLogger.Warn($"[CONCURRENCY_THROTTLED] Giảm luồng dịch đồng thời về 1 để tránh bão lỗi{(isRateLimit ? " (429 Rate Limit)" : "")}");
                }
            }
        }

        // Section 28: Dịch đoạn dài — chia semantic chunks, dịch từng chunk, merge
        private async Task<string> TranslateLongDialogueAsync(DialogueJob job)
        {
            var chunks = LongDialogueHandler.SplitIntoChunks(job.NormalizedText);
            var translatedChunks = new List<string>();

            foreach (var chunk in chunks)
            {
                string translated = await _provider.TranslateAsync(chunk, job.Context);
                translatedChunks.Add(string.IsNullOrEmpty(translated) ? chunk : translated);
            }

            return LongDialogueHandler.MergeTranslations(translatedChunks);
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
