using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TranslateBot.Capture;
using TranslateBot.Diagnostics;
using TranslateBot.Dialogue;
using TranslateBot.OCR;
using TranslateBot.OCR.Preprocessing;
using TranslateBot.Translation;
using TranslateBot.GameProfiles;

namespace TranslateBot.Infrastructure
{
    public class TranslationEngine
    {
        private readonly CaptureService _captureService;
        private readonly FrameChangeDetector _changeDetector;
        private readonly IOcrEngine _ocrEngine;
        private readonly DialogueTracker _dialogueTracker;
        private readonly TranslationWorker _translationWorker;

        // Stage 3B: Multi-area + exclusion
        private readonly OcrAreaManager _ocrAreaManager = new();
        private readonly ExclusionManager _exclusionManager = new();

        // Stage 4: Performance metrics & Adaptive burst
        private readonly PerformanceMetrics _metrics = new();
        private int _transitionBurstRemaining = 0;
        private int _loopCycleCount = 0;
        private DateTime _lastFpsCalculation = DateTime.Now;

        // Stage 5 & Milestone 2.2: Translation quality & Context Intelligence
        private readonly GlossaryManager _glossaryManager = new();
        private readonly CharacterProfileManager _characterProfileManager = new();
        private readonly SpeakerResolver _speakerResolver;
        private readonly FgoContextEngine _fgoContextEngine;
        private DialogueHistory? _dialogueHistory;
        private string? _currentSpeakerName;  // Detected from Speaker OCR area
        private string? _prefetchedText;       // Text đang được prefetch

        // Milestone 2.5: Game Profiles & Intelligence
        private readonly GameProfileManager _gameProfileManager = new();
        private GameProfile? _currentGameProfile;

        private bool _isRunning;
        private CancellationTokenSource? _cts;
        
        // ── Screen-absolute capture (Stage 1, backward compat) ─────────
        private int _regX, _regY, _regWidth, _regHeight;

        // ── Window-relative capture (Stage 3A) ─────────────────────────
        private IntPtr _targetWindowHandle = IntPtr.Zero;
        private bool _useWindowRelative;
        private int _relX, _relY, _relWidth, _relHeight;
        private string? _targetWindowTitle;
        private string? _targetProcessName;
        private DateTime _lastWindowSearchAt = DateTime.MinValue;
        private static readonly TimeSpan WindowSearchCooldown = TimeSpan.FromSeconds(3);

        public event Action<PerformanceMetrics>? OnMetricsUpdated;

        public TranslationEngine(
            CaptureService captureService,
            FrameChangeDetector changeDetector,
            IOcrEngine ocrEngine,
            DialogueTracker dialogueTracker,
            TranslationWorker translationWorker)
        {
            _captureService = captureService;
            _changeDetector = changeDetector;
            _ocrEngine = ocrEngine;
            _dialogueTracker = dialogueTracker;
            _translationWorker = translationWorker;
            _translationWorker.Metrics = _metrics;

            _speakerResolver = new SpeakerResolver(_characterProfileManager);
            _fgoContextEngine = new FgoContextEngine(_glossaryManager, _characterProfileManager, _speakerResolver);

            // Stage 4: Lắng nghe tín hiệu transition từ DialogueTracker để kích hoạt burst lấy mẫu cao tốc
            _dialogueTracker.OnTransitionDetected += () =>
            {
                _transitionBurstRemaining = 4;
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // Public Accessors
        // ═══════════════════════════════════════════════════════════════════

        public OcrAreaManager OcrAreaManager => _ocrAreaManager;
        public ExclusionManager ExclusionManager => _exclusionManager;
        public PerformanceMetrics Metrics => _metrics;
        public FrameChangeDetector ChangeDetector => _changeDetector;
        public bool IsWindowRelativeMode => _useWindowRelative;
        public IntPtr TargetWindowHandle => _targetWindowHandle;
        public GlossaryManager GlossaryManager => _glossaryManager;
        public CharacterProfileManager CharacterProfileManager => _characterProfileManager;
        public SpeakerResolver SpeakerResolver => _speakerResolver;
        public FgoContextEngine FgoContextEngine => _fgoContextEngine;
        public IOcrEngine OcrEngine => _ocrEngine;
        public DialogueTracker DialogueTracker => _dialogueTracker;
        public string? CurrentSpeakerName => _currentSpeakerName;
        public GameProfileManager GameProfileManager => _gameProfileManager;
        public GameProfile? CurrentGameProfile => _currentGameProfile;

        public void SetCurrentGameProfile(GameProfile? profile) => _currentGameProfile = profile;
        public void ApplyGameProfile(GameProfile profile) => _gameProfileManager.ApplyProfile(profile, this);
        public void SwitchScene(string sceneName) => _gameProfileManager.SwitchScene(sceneName, this);

        // Cho phép bật/tắt Prefetch để tiết kiệm Quota API (mặc định tắt vì Free Tier bị bóp 5 req/phút)
        public bool EnablePrefetch { get; set; } = false;

        // MainWindow gán DialogueHistory để engine có thể build context
        public void SetDialogueHistory(DialogueHistory history)
        {
            _dialogueHistory = history;
        }

        // Khởi tạo glossary + character profiles từ file
        public void LoadTranslationData()
        {
            _glossaryManager.Load();
            _characterProfileManager.Load();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Configuration Methods
        // ═══════════════════════════════════════════════════════════════════

        // Chế độ 1: Screen-absolute (giữ nguyên API cũ)
        public void SetCaptureRegion(int x, int y, int width, int height)
        {
            _regX = x; _regY = y; _regWidth = width; _regHeight = height;
            _useWindowRelative = false;
            _changeDetector.Invalidate("default");
            _dialogueTracker.Reset();
        }

        // Chế độ 2: Window-relative (Stage 3A)
        public void SetWindowTarget(IntPtr handle, string? windowTitle, string? processName)
        {
            _targetWindowHandle = handle;
            _targetWindowTitle = windowTitle;
            _targetProcessName = processName;
            _changeDetector.Invalidate();
            _dialogueTracker.Reset();
        }

        public void SetWindowRelativeRegion(int relX, int relY, int width, int height)
        {
            _relX = relX; _relY = relY; _relWidth = width; _relHeight = height;
            _useWindowRelative = true;
            _changeDetector.Invalidate("default");
            _dialogueTracker.Reset();
        }

        public void ResetTracker()
        {
            _dialogueTracker.Reset();
            _changeDetector.Invalidate();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ═══════════════════════════════════════════════════════════════════

        public void Start()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            _metrics.Reset();
            _loopCycleCount = 0;
            _lastFpsCalculation = DateTime.Now;

            _translationWorker.Start();
            _cts = new CancellationTokenSource();
            
            _ = Task.Run(() => MainLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _isRunning = false;
            _translationWorker.Stop();
            _cts?.Cancel();
        }

        public async Task StopAndWaitAsync(TimeSpan gracePeriod)
        {
            _isRunning = false;
            _cts?.Cancel();
            await _translationWorker.StopAndWaitAsync(gracePeriod);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Main Loop with Adaptive OCR (Section 12)
        // ═══════════════════════════════════════════════════════════════════

        private async Task MainLoopAsync(CancellationToken token)
        {
            while (_isRunning && !token.IsCancellationRequested)
            {
                bool anyChanged = false;

                try
                {
                    // Stage 3B: nếu có OcrArea đã thiết lập → dùng multi-area pipeline
                    var enabledAreas = _ocrAreaManager.GetEnabledDialogueAreas();

                    if (enabledAreas.Count > 0)
                    {
                        anyChanged = await ProcessMultiAreaAsync(enabledAreas);
                    }
                    else
                    {
                        // Fallback: single-region capture (backward compat)
                        anyChanged = await ProcessSingleRegionAsync();
                    }

                    // Cập nhật FPS đo lường
                    _loopCycleCount++;
                    var elapsedSinceFps = (DateTime.Now - _lastFpsCalculation).TotalSeconds;
                    if (elapsedSinceFps >= 1.0)
                    {
                        _metrics.LoopFps = _loopCycleCount / elapsedSinceFps;
                        _loopCycleCount = 0;
                        _lastFpsCalculation = DateTime.Now;
                        OnMetricsUpdated?.Invoke(_metrics);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[LOOP_ERROR] {ex.Message}");
                }

                // Adaptive sleep (Section 12)
                int sleepTime = GetAdaptiveSleepTime(_dialogueTracker.CurrentDialogue?.State, anyChanged);
                await Task.Delay(sleepTime, token);
            }
        }

        // Pipeline cũ (1 region duy nhất) — tối ưu frame difference + buffer reuse
        private async Task<bool> ProcessSingleRegionAsync()
        {
            int w = _useWindowRelative ? _relWidth : _regWidth;
            int h = _useWindowRelative ? _relHeight : _regHeight;

            if (w <= 0 || h <= 0) return false;

            var swCap = Stopwatch.StartNew();
            byte[]? frame = CaptureFrame();
            swCap.Stop();

            if (frame == null || frame.Length == 0) return false;

            var swDiff = Stopwatch.StartNew();
            var changeResult = _changeDetector.CheckAreaChange("default", frame, w, h);
            swDiff.Stop();

            _metrics.RecordFrameChecked(!changeResult.HasChanged, swCap.Elapsed.TotalMilliseconds, swDiff.Elapsed.TotalMilliseconds);

            // Nếu khung hình không đổi: không cần chạy OCR nặng lại,
            // nhưng VẪN PHẢI tick DialogueTracker để đếm đủ 300ms tĩnh và gửi đi dịch ngay!
            if (!changeResult.HasChanged)
            {
                _dialogueTracker.CheckStability();
                FlushPendingTranslations();
                return false;
            }

            _metrics.LastChangeRatio = changeResult.ChangedRatio;

            // Nếu thay đổi diện rộng (> 12% pixel thay đổi), kích hoạt burst 2 chu kỳ tiếp theo
            if (changeResult.ChangedRatio > 0.12)
            {
                _transitionBurstRemaining = Math.Max(_transitionBurstRemaining, 2);
            }

            // Stage 3B: áp dụng exclusion zones trước OCR
            _exclusionManager.ApplyExclusions(frame, w, h);

            // Milestone 2.3 & 2.5: Image Preprocessing (2x Upscale, Grayscale, Contrast boost) dựa theo Preset của GameProfile
            var defaultPreprocessing = PreprocessingOptions.FromPreset(_currentGameProfile?.PreprocessPreset ?? PreprocessPreset.FgoDialogue);
            var processed = ImagePreprocessor.Preprocess(frame, w, h, defaultPreprocessing);

            var swOcr = Stopwatch.StartNew();
            var ocrResult = await _ocrEngine.ExtractTextAsync(processed.Pixels, processed.Width, processed.Height);
            swOcr.Stop();
            _metrics.RecordOcrExecuted(swOcr.Elapsed.TotalMilliseconds);

            _dialogueTracker.ProcessOcrResult(ocrResult.Text);
            FlushPendingTranslations();
            return true;
        }

        // Pipeline mới (nhiều OCR area) — tối ưu frame difference riêng từng area + buffer reuse
        private async Task<bool> ProcessMultiAreaAsync(List<OcrArea> areas)
        {
            bool anyChanged = false;

            foreach (var area in areas)
            {
                var swCap = Stopwatch.StartNew();
                byte[]? frame = CaptureArea(area);
                swCap.Stop();

                if (frame == null || frame.Length == 0) continue;

                var swDiff = Stopwatch.StartNew();
                var changeResult = _changeDetector.CheckAreaChange(area.Id, frame, area.RegionWidth, area.RegionHeight);
                swDiff.Stop();

                _metrics.RecordFrameChecked(!changeResult.HasChanged, swCap.Elapsed.TotalMilliseconds, swDiff.Elapsed.TotalMilliseconds);

                // Nếu vùng này không đổi -> Bỏ qua OCR cho vùng này!
                if (!changeResult.HasChanged)
                {
                    continue;
                }

                anyChanged = true;
                _metrics.LastChangeRatio = changeResult.ChangedRatio;

                if (changeResult.ChangedRatio > 0.12)
                {
                    _transitionBurstRemaining = Math.Max(_transitionBurstRemaining, 2);
                }

                // Áp dụng exclusion zones
                _exclusionManager.ApplyExclusions(frame, area.RegionWidth, area.RegionHeight);

                // Milestone 2.3: Tiền xử lý ảnh theo Preset của từng OCR Area
                var preprocessing = area.GetEffectivePreprocessing();
                var processed = ImagePreprocessor.Preprocess(frame, area.RegionWidth, area.RegionHeight, preprocessing);

                // OCR
                var swOcr = Stopwatch.StartNew();
                var ocrResult = await _ocrEngine.ExtractTextAsync(processed.Pixels, processed.Width, processed.Height);
                swOcr.Stop();
                _metrics.RecordOcrExecuted(swOcr.Elapsed.TotalMilliseconds);

                string text = ocrResult.Text;
                if (string.IsNullOrWhiteSpace(text)) continue;

                // Xử lý theo loại area
                switch (area.Type)
                {
                    case OcrAreaType.Dialogue:
                    case OcrAreaType.Narration:
                        _dialogueTracker.ProcessOcrResult(text);
                        break;

                    case OcrAreaType.Speaker:
                        // Stage 5: Lưu speaker name → inject vào TranslationContext
                        string detectedSpeaker = text.Trim();
                        if (!string.Equals(_currentSpeakerName, detectedSpeaker, StringComparison.Ordinal))
                        {
                            _currentSpeakerName = detectedSpeaker;
                            AppLogger.Info($"[SPEAKER_CHANGED] \"{detectedSpeaker}\"");
                        }
                        break;

                    case OcrAreaType.Custom:
                        AppLogger.Info($"[CUSTOM_OCR] [{area.Name}] \"{text.Trim()}\"");
                        break;
                }
            }

            if (!anyChanged)
            {
                _dialogueTracker.CheckStability();
            }

            FlushPendingTranslations();
            return anyChanged;
        }

        // Capture 1 OcrArea cụ thể (tự chọn window-relative hoặc screen-absolute) với slot riêng
        private byte[]? CaptureArea(OcrArea area)
        {
            if (_useWindowRelative)
            {
                EnsureWindowHandle();
                if (_targetWindowHandle == IntPtr.Zero) return null;
                return _captureService.CaptureWindowRegion(
                    _targetWindowHandle, area.RegionX, area.RegionY, area.RegionWidth, area.RegionHeight, area.Id);
            }
            else
            {
                return _captureService.CaptureRegion(
                    area.RegionX, area.RegionY, area.RegionWidth, area.RegionHeight, area.Id);
            }
        }

        private void FlushPendingTranslations()
        {
            var pendingJobs = _dialogueTracker.GetPendingTranslations();
            foreach (var job in pendingJobs)
            {
                // Milestone 2.2: Multi-Source Speaker Resolution & FGO Context Engine
                var resolution = _speakerResolver.Resolve(job.RawText, _currentSpeakerName, _dialogueHistory, _fgoContextEngine.SceneContext);

                if (resolution.HasSpeaker)
                {
                    // Chuẩn hóa loại bỏ tiền tố người nói để LLM chỉ dịch phần thoại và tăng cache hit
                    job.RawText = resolution.CleanedDialogue;
                    job.NormalizedText = TextSimilarity.Normalize(resolution.CleanedDialogue);
                }

                // Stage 5 & Milestone 2.2: Build TranslationContext với Speaker và Profile
                job.Context = _fgoContextEngine.BuildContext(
                    job.RawText,
                    areaOcrSpeaker: _currentSpeakerName,
                    history: _dialogueHistory,
                    explicitSpeaker: resolution.Speaker,
                    explicitProfile: resolution.Profile);

                _translationWorker.EnqueueJob(job);
            }

            // Stage 5: Prefetch — nếu dialogue đang Stable (gần Confirmed),
            // gửi prefetch request để dịch trước, giảm latency khi player bấm Next.
            TryPrefetch();
        }

        // Build context từ FgoContextEngine (backward compatibility)
        private TranslationContext BuildTranslationContext(string? speakerName = null, CharacterProfile? profile = null)
        {
            return _fgoContextEngine.BuildContext(
                "",
                areaOcrSpeaker: _currentSpeakerName,
                history: _dialogueHistory,
                explicitSpeaker: speakerName,
                explicitProfile: profile);
        }

        // Prefetch: dịch trước câu đang Stable (chưa Confirmed)
        private void TryPrefetch()
        {
            if (!EnablePrefetch) return;

            var current = _dialogueTracker.CurrentDialogue;
            if (current == null || current.State != DialogueState.Stable) return;
            if (current.IsTranslated || current.IsPrefetch) return;

            // Đã prefetch text này rồi?
            if (_prefetchedText == current.NormalizedText) return;

            // Check Translation Memory — nếu đã có cache thì không cần prefetch
            if (_translationWorker.Memory.TryGetTranslation(current.NormalizedText) != null) return;

            // Gửi prefetch request
            _prefetchedText = current.NormalizedText;
            var prefetchJob = new DialogueJob(current.SequenceId, current.RawText, current.NormalizedText)
            {
                IsPrefetch = true,
                Context = _fgoContextEngine.BuildContext(
                    current.RawText,
                    areaOcrSpeaker: _currentSpeakerName,
                    history: _dialogueHistory)
            };
            AppLogger.Info($"[PREFETCH_QUEUED] #{current.SequenceId}");
            _translationWorker.EnqueueJob(prefetchJob);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Capture Helpers (single-region fallback)
        // ═══════════════════════════════════════════════════════════════════

        private byte[]? CaptureFrame()
        {
            if (_useWindowRelative)
            {
                return CaptureWindowRelativeFrame();
            }
            else
            {
                if (_regWidth <= 0 || _regHeight <= 0) return null;
                return _captureService.CaptureRegion(_regX, _regY, _regWidth, _regHeight, "default");
            }
        }

        private byte[]? CaptureWindowRelativeFrame()
        {
            if (_relWidth <= 0 || _relHeight <= 0) return null;

            EnsureWindowHandle();
            if (_targetWindowHandle == IntPtr.Zero) return null;

            return _captureService.CaptureWindowRegion(
                _targetWindowHandle, _relX, _relY, _relWidth, _relHeight, "default");
        }

        private void EnsureWindowHandle()
        {
            if (_targetWindowHandle != IntPtr.Zero && NativeMethods.IsWindow(_targetWindowHandle))
                return;

            TryReacquireWindow();
        }

        private void TryReacquireWindow()
        {
            if (DateTime.Now - _lastWindowSearchAt < WindowSearchCooldown)
                return;

            _lastWindowSearchAt = DateTime.Now;

            var found = WindowEnumerator.FindByTitleAndProcess(_targetWindowTitle ?? "", _targetProcessName ?? "");
            if (found != null)
            {
                _targetWindowHandle = found.Handle;
                AppLogger.Info($"[WINDOW_REACQUIRED] Tìm lại cửa sổ: \"{found.Title}\" (PID {found.ProcessId})");
            }
            else
            {
                AppLogger.Info($"[WINDOW_LOST] Không tìm thấy cửa sổ: \"{_targetWindowTitle}\" [{_targetProcessName}]");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Adaptive Sleep Controller (Section 12 của plan)
        // ═══════════════════════════════════════════════════════════════════

        private int GetAdaptiveSleepTime(DialogueState? state, bool anyAreaChanged)
        {
            // 1. Transition burst mode: Lấy mẫu siêu tốc khi phát hiện thoại mới/chuyển cảnh
            if (_transitionBurstRemaining > 0)
            {
                _transitionBurstRemaining--;
                _metrics.IsTransitionBurstActive = true;
                return 40; // 40ms siêu tốc (như translate-bot)
            }

            _metrics.IsTransitionBurstActive = false;

            // 2. Không có thoại và màn hình đứng im hoàn toàn -> Idle nhẹ nhàng tiết kiệm CPU
            if (state == null && !anyAreaChanged)
            {
                return 250; // 250ms (thay vì 750ms quá trễ)
            }

            // 3. Không có thoại nhưng vừa có thay đổi trên màn hình
            if (state == null && anyAreaChanged)
            {
                return 80;
            }

            // 4. Trạng thái thoại bình thường
            return state switch
            {
                DialogueState.Partial or DialogueState.Extending => 60, // Thoại đang chạy (60ms)
                DialogueState.Stable => 50,                             // Thoại đã dừng, kiểm tra mốc 300ms mỗi 50ms để dịch tức thì!
                DialogueState.Confirmed => 120,                         // Đã gửi dịch, chuẩn bị câu kế tiếp
                _ => 250
            };
        }
    }
}