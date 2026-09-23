using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DotNetEnv;
using TranslateBot.Capture;
using TranslateBot.Dialogue;
using TranslateBot.Diagnostics;
using TranslateBot.Hotkeys;
using TranslateBot.Infrastructure;
using TranslateBot.OCR;
using TranslateBot.Translation;

namespace TranslateBot
{
    public partial class MainWindow : Window
    {
        private TranslationEngine? _engine;
        private SnapshotService? _snapshotService;
        private bool _isBotRunning = false;
        private AppConfig _appConfig = new AppConfig();
        private readonly DialogueHistory _dialogueHistory = new();
        private readonly HotkeyManager _hotkeyManager = new();
        private ApiKeyPool _keyPool = new();
        private TranslationRouter? _translationRouter;
        private Translation.Providers.DeepLProvider? _deepLProvider;

        // Stage 3A: Cửa sổ game đã chọn
        private WindowInfo? _selectedWindow;

        // Stage 3B: Floating toolbar
        private UI.FloatingToolbar? _floatingToolbar;

        // Stage 3B: Shared services (để OcrAreaEditor + SnapshotService truy cập)
        private CaptureService? _captureService;
        private IOcrEngine? _ocrEngine;
        private ITranslationProvider? _translationProvider;

        // Stage 6: Overlay & UX
        private Overlay.OverlayWindow? _overlayWindow;
        private UI.HistoryWindow? _historyWindow;
        private UI.DetachedSubtitleWindow? _detachedSubtitleWindow;
        private Overlay.OverlayStyle _currentOverlayStyle = Overlay.OverlayStyle.CreatePreset(Overlay.OverlayTheme.FgoChaldea, Overlay.OverlayMode.Overlay);
        private bool _isOverlayLocked = false;
        private bool _isSyncingUiControls = true;

        // Stage 7: Advanced Features (TTS & Session Manager)
        private readonly Session.SessionManager _sessionManager = new();
        private readonly TTS.TtsService _ttsService = new();
        private bool _isTtsEnabled = false;

        public MainWindow()
        {
            _isSyncingUiControls = true;
            _appConfig = ConfigManager.Load();

            InitializeComponent();
            RestoreSettingsUiFromConfig();
            _isSyncingUiControls = false;

            ApplyTheme();
            RestoreCaptureFromConfig();
            RestoreOcrAreasFromConfig();
            RestoreOverlayFromConfig();
            InitializeBot();
        }

        private void InitializeBot()
        {
            try
            {
                var creds = ConfigManager.LoadCredentials(_appConfig);
                _keyPool = new ApiKeyPool(creds);

                // Fallback nếu có file .env hoặc legacy key mà pool chưa có
                string envKey = "";
                try
                {
                    Env.Load();
                    envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
                }
                catch { }

                if (!string.IsNullOrEmpty(envKey) && envKey != "dán_api_key_của_bạn_vào_đây" && !_keyPool.GetAllCredentials().Any())
                {
                    _keyPool.AddOrUpdateCredential(new ApiCredential
                    {
                        Id = "gemini-primary",
                        Provider = "Gemini",
                        Secret = envKey,
                        Status = ApiKeyStatus.Active
                    });
                }

                string activeProvider = _appConfig.TranslationProvider ?? "Gemini";
                string deepLKey = ConfigManager.GetEffectiveDeepLApiKey(_appConfig);
                bool hasGemini = _keyPool.GetAllCredentials().Any(c => c.IsAvailable);
                bool hasDeepL = !string.IsNullOrWhiteSpace(deepLKey);

                if (activeProvider.Equals("Gemini", StringComparison.OrdinalIgnoreCase) && !hasGemini)
                {
                    StatusText.Text = "! Chưa có Gemini API Key — Bấm [Cài đặt API] để nhập";
                    StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    return;
                }
                else if (activeProvider.Equals("DeepL", StringComparison.OrdinalIgnoreCase) && !hasDeepL)
                {
                    StatusText.Text = "! Chưa có DeepL Auth Key — Vui lòng nhập ở Tab Bộ máy AI & Ngôn ngữ";
                    StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    return;
                }

                _captureService?.Dispose();
                _captureService = new CaptureService();
                var changeDetector = new FrameChangeDetector();
                _ocrEngine = CreateOcrEngine(_appConfig.OcrEngineType);
                var dialogueTracker = new DialogueTracker();

                var gemini = new GeminiProvider();
                if (!string.IsNullOrEmpty(_appConfig.AiModel)) gemini.ModelName = _appConfig.AiModel;
                if (!string.IsNullOrEmpty(_appConfig.TargetLanguage)) gemini.TargetLanguage = _appConfig.TargetLanguage;
                if (!string.IsNullOrEmpty(_appConfig.SourceLanguage)) gemini.SourceLanguage = _appConfig.SourceLanguage;

                _deepLProvider = new Translation.Providers.DeepLProvider(deepLKey)
                {
                    SourceLanguage = _appConfig.SourceLanguage,
                    TargetLanguage = _appConfig.TargetLanguage
                };

                var fallback = new GoogleWebTranslateProvider
                {
                    SourceLanguage = _appConfig.SourceLanguage,
                    TargetLanguage = _appConfig.TargetLanguage
                };

                _translationRouter = new TranslationRouter(
                    _keyPool, 
                    gemini, 
                    fallback, 
                    deepLProvider: _deepLProvider)
                {
                    PrimaryProvider = activeProvider
                };
                _translationProvider = _translationRouter;
                var translationWorker = new TranslationWorker(_translationProvider);

                translationWorker.OnTranslationCompleted = (job, result) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        job.State = DialogueState.Displayed;
                        string? speaker = job.Context?.SpeakerName;
                        _dialogueHistory.Add(job.SequenceId, job.RawText, result, speaker, 0);
                        _overlayWindow?.UpdateDialogue(job.RawText, result, speaker, job.SequenceId, 0, false);
                        _detachedSubtitleWindow?.UpdateDialogue(job.RawText, result, speaker, _dialogueHistory.Previous?.TranslatedText);

                        // Stage 7: Tự động ghi session log và đọc TTS nếu bật
                        _sessionManager.AddRecord(job.SequenceId, job.RawText, result, speaker, _translationRouter?.CurrentProviderName ?? activeProvider, 0, false);
                        if (_isTtsEnabled)
                        {
                            _ttsService.SpeakAsync(result, cancelPrevious: true);
                        }
                    });
                };

                _dialogueHistory.OnNewEntry += entry =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_dialogueHistory.Previous != null && PreviousTranslatedLabel != null)
                        {
                            PreviousTranslatedLabel.Text = _dialogueHistory.Previous.TranslatedText;
                            PreviousTranslatedLabel.Visibility = Visibility.Visible;
                        }

                        if (!string.IsNullOrWhiteSpace(entry.Speaker) && MainSpeakerBadge != null && MainSpeakerLabel != null)
                        {
                            MainSpeakerBadge.Visibility = Visibility.Visible;
                            MainSpeakerLabel.Text = entry.Speaker;
                        }
                        else if (MainSpeakerBadge != null)
                        {
                            MainSpeakerBadge.Visibility = Visibility.Collapsed;
                        }

                        if (OriginalTextLabel != null) OriginalTextLabel.Text = entry.OriginalText;
                        if (TranslatedTextLabel != null) TranslatedTextLabel.Text = entry.TranslatedText;

                        if (_detachedSubtitleWindow != null && _detachedSubtitleWindow.IsLoaded)
                        {
                            _detachedSubtitleWindow.UpdateDialogue(
                                entry.OriginalText, 
                                entry.TranslatedText, 
                                entry.Speaker, 
                                _dialogueHistory.Previous?.TranslatedText);
                        }

                        if (_currentOverlayStyle.DisplayMode == Overlay.DialogueDisplayMode.Debug && MainDebugText != null)
                        {
                            MainDebugText.Visibility = Visibility.Visible;
                            MainDebugText.Text = $"#{entry.SequenceId:D4} | Thời gian: {entry.FormattedTime}";
                        }
                        else if (MainDebugText != null)
                        {
                            MainDebugText.Visibility = Visibility.Collapsed;
                        }
                    });
                };

                _engine = new TranslationEngine(_captureService, changeDetector, _ocrEngine, dialogueTracker, translationWorker);
                _engine.OnMetricsUpdated += metrics =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_isBotRunning && PerformanceMetricsText != null)
                        {
                            PerformanceMetricsText.Text = $"{metrics.LoopFps:F0} FPS | Skip: {metrics.OcrSkipPercentage:F0}% | Cap: {metrics.AvgCaptureLatencyMs:F1}ms | Diff: {metrics.AvgDiffLatencyMs:F2}ms | OCR: {metrics.AvgOcrLatencyMs:F0}ms";
                        }
                    });
                };
                _snapshotService = new SnapshotService(_captureService, _ocrEngine, _translationProvider);

                // Stage 5: Truyền DialogueHistory cho engine (để build context)
                _engine.SetDialogueHistory(_dialogueHistory);
                _engine.LoadTranslationData();  // Load glossary.json + characters.json

                StatusText.Text = "Sẵn sàng";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Centralized Global Hotkeys (Phase 1 — HotkeyManager)
        // ═══════════════════════════════════════════════════════════════════

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            _hotkeyManager.Initialize(hwnd);
            _hotkeyManager.OnCommandTriggered += HandleHotkeyCommand;
        }

        private void HandleHotkeyCommand(HotkeyCommand cmd)
        {
            Dispatcher.Invoke(() =>
            {
                switch (cmd)
                {
                    case HotkeyCommand.Snapshot:
                    case HotkeyCommand.CaptureOnce:
                        DoSnapshot();
                        break;
                    case HotkeyCommand.LockOverlay:
                    case HotkeyCommand.ToggleClickThrough:
                        ToggleOverlayLock();
                        break;
                    case HotkeyCommand.ToggleCapture:
                        ToggleBotBtn_Click(this, new RoutedEventArgs());
                        break;
                    case HotkeyCommand.SelectRegion:
                        DoSelectRegion();
                        break;
                    case HotkeyCommand.SelectWindow:
                        DoSelectWindow();
                        break;
                    case HotkeyCommand.ToggleOverlay:
                        ToggleOverlay();
                        break;
                    case HotkeyCommand.DetachSubtitle:
                        ToggleDetachedSubtitle();
                        break;
                    case HotkeyCommand.Retranslate:
                        DoRetranslate();
                        break;
                    case HotkeyCommand.OpenHotkeyHelp:
                        OpenHotkeyCheatsheet();
                        break;
                    case HotkeyCommand.ToggleTTS:
                        ToggleTts();
                        break;
                    case HotkeyCommand.OpenApiKeyManager:
                        ApiKeyBtn_Click(this, new RoutedEventArgs());
                        break;
                    case HotkeyCommand.OpenHistory:
                        OpenHistoryWindow();
                        break;
                    case HotkeyCommand.FinalizeDialogue:
                        _engine?.ResetTracker();
                        break;
                    case HotkeyCommand.EmergencyStop:
                        if (_isBotRunning)
                        {
                            ToggleBotBtn_Click(this, new RoutedEventArgs());
                        }
                        break;
                }
            });
        }

        private async void DoRetranslate()
        {
            if (_translationProvider == null) return;

            string textToTranslate = "";
            TranslationContext? context = null;

            if (_dialogueHistory.Current != null)
            {
                textToTranslate = _dialogueHistory.Current.OriginalText;
            }
            else if (!string.IsNullOrWhiteSpace(OriginalTextLabel.Text) && OriginalTextLabel.Text != "Chưa có dữ liệu...")
            {
                textToTranslate = OriginalTextLabel.Text;
            }

            if (string.IsNullOrWhiteSpace(textToTranslate))
            {
                DoSnapshot();
                return;
            }

            StatusText.Text = "Đang dịch lại...";
            try
            {
                string result = await _translationProvider.TranslateAsync(textToTranslate, context);
                if (!string.IsNullOrEmpty(result))
                {
                    TranslatedTextLabel.Text = result;
                    _overlayWindow?.UpdateDialogue(textToTranslate, result, _dialogueHistory.Current?.Speaker, 0, 0, false);
                    _detachedSubtitleWindow?.UpdateDialogue(textToTranslate, result, _dialogueHistory.Current?.Speaker, _dialogueHistory.Previous?.TranslatedText);
                    StatusText.Text = "Đã dịch lại xong";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[RETRANSLATE_ERROR] {ex.Message}");
                StatusText.Text = "Dịch lại thất bại";
            }
        }

        private void OpenHotkeyCheatsheet()
        {
            var win = new UI.HotkeyCheatsheetWindow
            {
                Owner = this
            };
            win.ShowDialog();
        }

        private void HotkeyCheatsheetBtn_Click(object sender, RoutedEventArgs e) => OpenHotkeyCheatsheet();
        private void HotkeyHintText_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenHotkeyCheatsheet();

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // F1: Mở bảng tra cứu phím tắt
            if (e.Key == System.Windows.Input.Key.F1)
            {
                OpenHotkeyCheatsheet();
                e.Handled = true;
                return;
            }

            // F6: Bật / Tắt dịch tự động
            if (e.Key == System.Windows.Input.Key.F6)
            {
                ToggleBotBtn_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            // F7: Chụp 1 khung hình
            if (e.Key == System.Windows.Input.Key.F7)
            {
                DoSnapshot();
                e.Handled = true;
                return;
            }

            // F8 hoặc Ctrl+F8
            if (e.Key == System.Windows.Input.Key.F8)
            {
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
                {
                    DoSelectRegion();
                }
                else
                {
                    DoSnapshot();
                }
                e.Handled = true;
                return;
            }

            // F9: Khóa / Mở khóa xuyên chuột
            if (e.Key == System.Windows.Input.Key.F9)
            {
                ToggleOverlayLock();
                e.Handled = true;
                return;
            }

            // F10: Ẩn / Hiện Overlay HUD
            if (e.Key == System.Windows.Input.Key.F10)
            {
                ToggleOverlay();
                e.Handled = true;
                return;
            }

            // F11: Tách / Gắn lại cửa sổ phụ đề nổi
            if (e.Key == System.Windows.Input.Key.F11)
            {
                ToggleDetachedSubtitle();
                e.Handled = true;
                return;
            }

            // Ctrl+T: Dịch lại
            if (e.Key == System.Windows.Input.Key.T && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                DoRetranslate();
                e.Handled = true;
                return;
            }

            // Ctrl+Y: TTS
            if (e.Key == System.Windows.Input.Key.Y && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                ToggleTts();
                e.Handled = true;
                return;
            }

            // Ctrl+J: Lịch sử
            if (e.Key == System.Windows.Input.Key.J && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                OpenHistoryWindow();
                e.Handled = true;
                return;
            }

            // Ctrl+K: API Key
            if (e.Key == System.Windows.Input.Key.K && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                ApiKeyBtn_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Window Selection (Stage 3A)
        // ═══════════════════════════════════════════════════════════════════

        private void SelectWindowBtn_Click(object sender, RoutedEventArgs e)
            => DoSelectWindow();

        private void DoSelectWindow()
        {
            var selector = new UI.WindowSelector();
            selector.Owner = this;
            selector.OnWindowSelected = windowInfo =>
            {
                _selectedWindow = windowInfo;
                _engine?.SetWindowTarget(windowInfo.Handle, windowInfo.Title, windowInfo.ProcessName);

                _appConfig.TargetWindowTitle = windowInfo.Title;
                _appConfig.TargetProcessName = windowInfo.ProcessName;
                ConfigManager.Save(_appConfig);
                UpdateWindowInfoDisplay();
            };
            selector.ShowDialog();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Region Selection (hỗ trợ cả 2 chế độ)
        // ═══════════════════════════════════════════════════════════════════

        private void SelectRegionBtn_Click(object sender, RoutedEventArgs e)
            => DoSelectRegion();

        private void DoSelectRegion()
        {
            OpenRegionSelector((x, y, w, h) =>
            {
                if (_selectedWindow != null)
                {
                    _engine?.SetWindowRelativeRegion(x, y, w, h);
                    _appConfig.UseWindowRelativeCapture = true;
                    _appConfig.RelativeCaptureX = x;
                    _appConfig.RelativeCaptureY = y;
                    _appConfig.RelativeCaptureWidth = w;
                    _appConfig.RelativeCaptureHeight = h;
                }
                else
                {
                    _engine?.SetCaptureRegion(x, y, w, h);
                    _appConfig.UseWindowRelativeCapture = false;
                    _appConfig.CaptureX = x;
                    _appConfig.CaptureY = y;
                    _appConfig.CaptureWidth = w;
                    _appConfig.CaptureHeight = h;
                }
                ConfigManager.Save(_appConfig);
                UpdateWindowInfoDisplay();
            });
        }

        // Mở RegionSelector với chế độ phù hợp. Trả về true nếu đã mở.
        private bool OpenRegionSelector(Action<int, int, int, int> onRegionSelected)
        {
            var selector = new UI.RegionSelector();

            if (_selectedWindow != null)
            {
                selector.WindowHandle = _selectedWindow.Handle;
                selector.OnWindowRelativeRegionSelected = onRegionSelected;
            }
            else
            {
                selector.OnRegionSelected = onRegionSelected;
            }

            selector.ShowDialog();
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Stage 3B: OCR Area Editor
        // ═══════════════════════════════════════════════════════════════════

        private void OcrAreaBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null) return;

            var editor = new UI.OcrAreaEditor(_engine.OcrAreaManager, _engine.ExclusionManager);
            editor.Owner = this;

            // OcrAreaEditor cần mở RegionSelector → delegate qua callback
            editor.RequestRegionSelection = (callback) =>
            {
                return OpenRegionSelector(callback);
            };

            editor.OnAreasChanged = () =>
            {
                _appConfig.OcrAreas = _engine.OcrAreaManager.ToList();
                _appConfig.ExclusionAreas = _engine.ExclusionManager.ToList();
                ConfigManager.Save(_appConfig);
                UpdateWindowInfoDisplay();
            };

            editor.ShowDialog();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Stage 3B: Snapshot (F8)
        // ═══════════════════════════════════════════════════════════════════

        private void SnapshotBtn_Click(object sender, RoutedEventArgs e) => DoSnapshot();

        private async void DoSnapshot()
        {
            if (_snapshotService == null) return;

            StatusText.Text = "Đang chụp...";

            SnapshotResult result;

            if (_appConfig.UseWindowRelativeCapture && _engine != null)
            {
                var handle = _engine.TargetWindowHandle;
                if (handle == IntPtr.Zero)
                {
                    // Thử tìm lại cửa sổ
                    var found = WindowEnumerator.FindByTitleAndProcess(
                        _appConfig.TargetWindowTitle ?? "", _appConfig.TargetProcessName ?? "");
                    if (found != null) handle = found.Handle;
                }

                int x = _appConfig.RelativeCaptureX, y = _appConfig.RelativeCaptureY;
                int w = _appConfig.RelativeCaptureWidth, h = _appConfig.RelativeCaptureHeight;

                // Nếu có OCR areas → dùng area Dialogue đầu tiên
                var areas = _engine.OcrAreaManager.GetEnabledDialogueAreas();
                if (areas.Count > 0)
                {
                    x = areas[0].RegionX; y = areas[0].RegionY;
                    w = areas[0].RegionWidth; h = areas[0].RegionHeight;
                }

                result = await _snapshotService.CaptureAndTranslateAsync(handle, x, y, w, h);
            }
            else
            {
                int x = _appConfig.CaptureX, y = _appConfig.CaptureY;
                int w = _appConfig.CaptureWidth, h = _appConfig.CaptureHeight;

                if (w <= 0 || h <= 0)
                {
                    StatusText.Text = "Chưa chọn vùng quét";
                    return;
                }

                result = await _snapshotService.CaptureAndTranslateAsync(x, y, w, h);
            }

            if (result.Success)
            {
                OriginalTextLabel.Text = result.OriginalText;
                TranslatedTextLabel.Text = result.TranslatedText;
                _detachedSubtitleWindow?.UpdateDialogue(result.OriginalText, result.TranslatedText, null, null);
                StatusText.Text = "Snapshot hoàn tất";
            }
            else
            {
                StatusText.Text = "Snapshot thất bại";
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Stage 3B: Floating Toolbar
        // ═══════════════════════════════════════════════════════════════════

        private void ToolbarToggleBtn_Click(object sender, RoutedEventArgs e)
            => ToggleFloatingToolbar();

        private void ToggleFloatingToolbar()
        {
            try
            {
                if (_floatingToolbar == null || !_floatingToolbar.IsLoaded)
                {
                    _floatingToolbar = new UI.FloatingToolbar();
                    _floatingToolbar.Closed += (s, e) => _floatingToolbar = null;
                    _floatingToolbar.OnToggleBot = () => Dispatcher.Invoke(() =>
                        ToggleBotBtn_Click(this, new RoutedEventArgs()));
                    _floatingToolbar.OnSelectRegion = () => Dispatcher.Invoke(DoSelectRegion);
                    _floatingToolbar.OnSnapshot = () => Dispatcher.Invoke(DoSnapshot);
                    _floatingToolbar.OnSelectWindow = () => Dispatcher.Invoke(DoSelectWindow);
                    _floatingToolbar.OnShowMainWindow = () => Dispatcher.Invoke(() =>
                    {
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                    });
                    _floatingToolbar.OnToggleOverlay = () => Dispatcher.Invoke(ToggleOverlay);
                    _floatingToolbar.OnToggleLock = () => Dispatcher.Invoke(ToggleOverlayLock);
                    _floatingToolbar.OnToggleTts = () => Dispatcher.Invoke(ToggleTts);
                    _floatingToolbar.OnOpenHistory = () => Dispatcher.Invoke(OpenHistoryWindow);
                    _floatingToolbar.OnPositionChanged = (x, y) =>
                    {
                        _appConfig.ToolbarX = x;
                        _appConfig.ToolbarY = y;
                        ConfigManager.Save(_appConfig);
                    };

                    _floatingToolbar.RestorePosition(_appConfig.ToolbarX, _appConfig.ToolbarY);
                    _floatingToolbar.SetBotRunning(_isBotRunning);
                    _floatingToolbar.SetOverlayActive(_overlayWindow != null && _overlayWindow.IsVisible);
                    _floatingToolbar.SetOverlayLocked(_isOverlayLocked);
                    _floatingToolbar.SetTtsActive(_isTtsEnabled);
                    _floatingToolbar.Show();
                    _appConfig.ShowToolbar = true;
                    ConfigManager.Save(_appConfig);
                }
                else
                {
                    _floatingToolbar.Close();
                    _floatingToolbar = null;
                    _appConfig.ShowToolbar = false;
                    ConfigManager.Save(_appConfig);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FloatingToolbar] Lỗi bật/tắt thanh nổi: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Detached Floating Subtitle Window (Cửa sổ phụ đề nổi độc lập)
        // ═══════════════════════════════════════════════════════════════════

        private void DetachSubtitleBtn_Click(object sender, RoutedEventArgs e)
            => ToggleDetachedSubtitle();

        public void ToggleDetachedSubtitle()
        {
            try
            {
                if (_detachedSubtitleWindow == null || !_detachedSubtitleWindow.IsLoaded)
                {
                    _detachedSubtitleWindow = new UI.DetachedSubtitleWindow();
                    _detachedSubtitleWindow.Closed += (s, e) =>
                    {
                        _detachedSubtitleWindow = null;
                        if (DetachSubtitleBtn != null)
                        {
                            DetachSubtitleBtn.Content = "Tách cửa sổ";
                            DetachSubtitleBtn.ToolTip = "Tách vùng hiển thị thành cửa sổ nổi độc lập";
                        }
                        if (DetachedBadge != null) DetachedBadge.Visibility = Visibility.Collapsed;
                        _appConfig.IsSubtitleDetached = false;
                        ConfigManager.Save(_appConfig);
                    };

                    _detachedSubtitleWindow.OnRequestSpeak = () => Dispatcher.Invoke(() => SpeakCurrentBtn_Click(this, new RoutedEventArgs()));
                    _detachedSubtitleWindow.OnRequestCopy = () => Dispatcher.Invoke(() => CopyTextBtn_Click(this, new RoutedEventArgs()));
                    _detachedSubtitleWindow.OnRequestHistory = () => Dispatcher.Invoke(OpenHistoryWindow);
                    _detachedSubtitleWindow.OnRequestSelectWindow = () => Dispatcher.Invoke(DoSelectWindow);
                    _detachedSubtitleWindow.OnRequestDockBack = () => Dispatcher.Invoke(ToggleDetachedSubtitle);

                    _detachedSubtitleWindow.OnBoundsChanged = (x, y, w, h) =>
                    {
                        _appConfig.SubtitleWindowX = x;
                        _appConfig.SubtitleWindowY = y;
                        _appConfig.SubtitleWindowWidth = w;
                        _appConfig.SubtitleWindowHeight = h;
                        ConfigManager.Save(_appConfig);
                    };

                    _detachedSubtitleWindow.OnFontSizeChanged = (size) =>
                    {
                        _appConfig.SubtitleFontSize = size;
                        ConfigManager.Save(_appConfig);
                    };

                    _detachedSubtitleWindow.OnTopmostChanged = (topmost) =>
                    {
                        _appConfig.SubtitleTopmost = topmost;
                        ConfigManager.Save(_appConfig);
                    };

                    _detachedSubtitleWindow.OnLockChanged = (locked) =>
                    {
                        _isOverlayLocked = locked;
                        _appConfig.OverlayLocked = locked;
                        ConfigManager.Save(_appConfig);
                        UpdateLockButtonsDisplay();
                        _overlayWindow?.SetLocked(_isOverlayLocked);
                        _floatingToolbar?.SetOverlayLocked(_isOverlayLocked);
                    };

                    _detachedSubtitleWindow.ApplyRestoredBounds(
                        _appConfig.SubtitleWindowX,
                        _appConfig.SubtitleWindowY,
                        _appConfig.SubtitleWindowWidth,
                        _appConfig.SubtitleWindowHeight);

                    _detachedSubtitleWindow.SetFontSize(_appConfig.SubtitleFontSize > 0 ? _appConfig.SubtitleFontSize : 16);
                    _detachedSubtitleWindow.SetTopmost(_appConfig.SubtitleTopmost);
                    _detachedSubtitleWindow.SetLocked(_isOverlayLocked);
                    _detachedSubtitleWindow.SetTargetWindow(_selectedWindow?.Title ?? _appConfig.TargetWindowTitle ?? "Toàn màn hình");

                    string currentOriginal = OriginalTextLabel != null ? OriginalTextLabel.Text : "";
                    string currentTrans = TranslatedTextLabel != null ? TranslatedTextLabel.Text : "";
                    string? currentSpeaker = (MainSpeakerBadge != null && MainSpeakerBadge.Visibility == Visibility.Visible && MainSpeakerLabel != null) 
                        ? MainSpeakerLabel.Text : null;
                    string? prevTrans = (PreviousTranslatedLabel != null && PreviousTranslatedLabel.Visibility == Visibility.Visible) 
                        ? PreviousTranslatedLabel.Text : null;

                    _detachedSubtitleWindow.UpdateDialogue(currentOriginal, currentTrans, currentSpeaker, prevTrans);
                    _detachedSubtitleWindow.Show();

                    if (DetachSubtitleBtn != null)
                    {
                        DetachSubtitleBtn.Content = "Gắn lại";
                        DetachSubtitleBtn.ToolTip = "Gắn lại vùng hiển thị vào ứng dụng chính";
                    }
                    if (DetachedBadge != null) DetachedBadge.Visibility = Visibility.Visible;

                    _appConfig.IsSubtitleDetached = true;
                    ConfigManager.Save(_appConfig);
                }
                else
                {
                    _detachedSubtitleWindow.Close();
                    _detachedSubtitleWindow = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DetachedSubtitleWindow] Lỗi bật/tắt cửa sổ phụ đề: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Stage 6: Overlay, Themes & History Management (Section 20, 21, 36)
        // ═══════════════════════════════════════════════════════════════════

        private void OverlayToggleBtn_Click(object sender, RoutedEventArgs e) => ToggleOverlay();
        private void LockOverlayBtn_Click(object sender, RoutedEventArgs e) => ToggleOverlayLock();
        private void HistoryBtn_Click(object sender, RoutedEventArgs e) => OpenHistoryWindow();

        public void ToggleOverlay()
        {
            if (_overlayWindow == null)
            {
                _overlayWindow = new Overlay.OverlayWindow();
                _overlayWindow.TargetGameWindowHandle = _selectedWindow?.Handle ?? IntPtr.Zero;

                _overlayWindow.ApplyStyle(_currentOverlayStyle);

                if (_appConfig.OverlayWidth > 0 && _appConfig.OverlayHeight > 0)
                {
                    _overlayWindow.Width = _appConfig.OverlayWidth;
                    _overlayWindow.Height = _appConfig.OverlayHeight;
                }

                if (_currentOverlayStyle.Position == Overlay.OverlayPosition.Custom && _appConfig.OverlayX > 0 && _appConfig.OverlayY > 0)
                {
                    _overlayWindow.Left = _appConfig.OverlayX;
                    _overlayWindow.Top = _appConfig.OverlayY;
                }
                else
                {
                    _overlayWindow.SnapTo(_currentOverlayStyle.Position);
                }

                _overlayWindow.SetLocked(_isOverlayLocked);
                UpdateLockButtonsDisplay();

                _overlayWindow.OnRequestOpenHistory = OpenHistoryWindow;
                _overlayWindow.OnOverlayConfigChanged = (style, locked, rect) =>
                {
                    _currentOverlayStyle = style;
                    _isOverlayLocked = locked;
                    _appConfig.OverlayMode = style.Mode.ToString();
                    _appConfig.OverlayTheme = style.Theme.ToString();
                    _appConfig.DisplayMode = style.DisplayMode.ToString();
                    _appConfig.OverlayPosition = style.Position.ToString();
                    _appConfig.OverlayLocked = locked;
                    _appConfig.OverlayX = rect.X;
                    _appConfig.OverlayY = rect.Y;
                    _appConfig.OverlayWidth = rect.Width;
                    _appConfig.OverlayHeight = rect.Height;
                    _appConfig.OverlayFontSize = style.FontSize;
                    ConfigManager.Save(_appConfig);

                    SyncUiControlsWithStyle();
                    UpdateLockButtonsDisplay();
                    _floatingToolbar?.SetOverlayLocked(_isOverlayLocked);
                };

                _overlayWindow.Show();
                _appConfig.OverlayEnabled = true;
                ConfigManager.Save(_appConfig);
                if (OverlayToggleCheckBox != null) OverlayToggleCheckBox.IsChecked = true;
            }
            else if (_overlayWindow.IsVisible)
            {
                _overlayWindow.Hide();
                _appConfig.OverlayEnabled = false;
                ConfigManager.Save(_appConfig);
                if (OverlayToggleCheckBox != null) OverlayToggleCheckBox.IsChecked = false;
            }
            else
            {
                _overlayWindow.TargetGameWindowHandle = _selectedWindow?.Handle ?? IntPtr.Zero;
                _overlayWindow.Show();
                _appConfig.OverlayEnabled = true;
                ConfigManager.Save(_appConfig);
                if (OverlayToggleCheckBox != null) OverlayToggleCheckBox.IsChecked = true;
            }

            _floatingToolbar?.SetOverlayActive(_overlayWindow != null && _overlayWindow.IsVisible);
        }

        public void ToggleOverlayLock()
        {
            _isOverlayLocked = !_isOverlayLocked;
            _overlayWindow?.SetLocked(_isOverlayLocked);
            _detachedSubtitleWindow?.SetLocked(_isOverlayLocked);
            _appConfig.OverlayLocked = _isOverlayLocked;
            ConfigManager.Save(_appConfig);
            UpdateLockButtonsDisplay();
            _floatingToolbar?.SetOverlayLocked(_isOverlayLocked);
        }

        private void UpdateLockButtonsDisplay()
        {
            if (_isOverlayLocked)
            {
                LockOverlayBtn.Content = "Đã khóa [F9]";
                LockOverlayBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else
            {
                LockOverlayBtn.Content = "Khóa [F9]";
                LockOverlayBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4B5563"));
            }
        }

        public void OpenHistoryWindow()
        {
            if (_historyWindow == null || !_historyWindow.IsLoaded)
            {
                _historyWindow = new UI.HistoryWindow(_dialogueHistory);
                _historyWindow.Owner = this;
                _historyWindow.OnRequestSpeak = text => _ttsService.SpeakAsync(text, cancelPrevious: true);
                _historyWindow.Show();
            }
            else
            {
                _historyWindow.Activate();
                if (_historyWindow.WindowState == WindowState.Minimized)
                    _historyWindow.WindowState = WindowState.Normal;
            }
        }

        private void OverlayModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiControls || _appConfig == null || OverlayModeCombo?.SelectedItem is not ComboBoxItem item) return;
            string tag = item.Tag?.ToString() ?? "Overlay";
            if (Enum.TryParse<Overlay.OverlayMode>(tag, out var mode))
            {
                var newStyle = Overlay.OverlayStyle.CreatePreset(_currentOverlayStyle.Theme, mode);
                newStyle.DisplayMode = _currentOverlayStyle.DisplayMode;
                newStyle.Position = _currentOverlayStyle.Position;
                newStyle.FontSize = _currentOverlayStyle.FontSize;
                _currentOverlayStyle = newStyle;

                _overlayWindow?.ApplyStyle(_currentOverlayStyle);
                _appConfig.OverlayMode = mode.ToString();
                ConfigManager.Save(_appConfig);
            }
        }

        private void DisplayModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiControls || _appConfig == null || DisplayModeCombo?.SelectedItem is not ComboBoxItem item) return;
            string tag = item.Tag?.ToString() ?? "Both";
            if (Enum.TryParse<Overlay.DialogueDisplayMode>(tag, out var displayMode))
            {
                _currentOverlayStyle.DisplayMode = displayMode;
                _overlayWindow?.ApplyStyle(_currentOverlayStyle);
                _appConfig.DisplayMode = displayMode.ToString();
                ConfigManager.Save(_appConfig);

                // Update card chính trên MainWindow
                if (OriginalTextLabel != null && MainDebugText != null)
                {
                    if (displayMode == Overlay.DialogueDisplayMode.TranslationOnly)
                    {
                        OriginalTextLabel.Visibility = Visibility.Collapsed;
                        MainDebugText.Visibility = Visibility.Collapsed;
                    }
                    else if (displayMode == Overlay.DialogueDisplayMode.Debug)
                    {
                        OriginalTextLabel.Visibility = Visibility.Visible;
                        MainDebugText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        OriginalTextLabel.Visibility = Visibility.Visible;
                        MainDebugText.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiControls || _appConfig == null || ThemeCombo?.SelectedItem is not ComboBoxItem item) return;
            string themeName = item.Tag?.ToString() ?? "AutoHostDark";
            _appConfig.MainTheme = themeName;
            ConfigManager.Save(_appConfig);
            ApplyTheme(themeName);
        }

        private void SyncUiControlsWithStyle()
        {
            _isSyncingUiControls = true;
            try
            {
                // Mode
                foreach (ComboBoxItem item in OverlayModeCombo.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), _currentOverlayStyle.Mode.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        OverlayModeCombo.SelectedItem = item;
                        break;
                    }
                }

                // Display Mode
                foreach (ComboBoxItem item in DisplayModeCombo.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), _currentOverlayStyle.DisplayMode.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        DisplayModeCombo.SelectedItem = item;
                        break;
                    }
                }

                // Theme
                string curTheme = _appConfig.MainTheme ?? "AutoHostDark";
                foreach (ComboBoxItem item in ThemeCombo.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), curTheme, StringComparison.OrdinalIgnoreCase))
                    {
                        ThemeCombo.SelectedItem = item;
                        break;
                    }
                }

                // Scan Interval
                string curInterval = _appConfig.ScanIntervalMs > 0 ? _appConfig.ScanIntervalMs.ToString() : "300";
                foreach (ComboBoxItem item in ScanIntervalCombo.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), curInterval, StringComparison.OrdinalIgnoreCase))
                    {
                        ScanIntervalCombo.SelectedItem = item;
                        break;
                    }
                }

                // AI Model
                string curModel = _appConfig.AiModel ?? "gemini-flash-latest";
                foreach (ComboBoxItem item in AiModelCombo.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), curModel, StringComparison.OrdinalIgnoreCase))
                    {
                        AiModelCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _isSyncingUiControls = false;
            }
        }

        private void RestoreOverlayFromConfig()
        {
            var theme = Enum.TryParse<Overlay.OverlayTheme>(_appConfig.OverlayTheme, out var t) ? t : Overlay.OverlayTheme.Dark;
            var mode = Enum.TryParse<Overlay.OverlayMode>(_appConfig.OverlayMode, out var m) ? m : Overlay.OverlayMode.Overlay;
            var display = Enum.TryParse<Overlay.DialogueDisplayMode>(_appConfig.DisplayMode, out var d) ? d : Overlay.DialogueDisplayMode.Both;
            var position = Enum.TryParse<Overlay.OverlayPosition>(_appConfig.OverlayPosition, out var pos) ? pos : Overlay.OverlayPosition.Bottom;

            _currentOverlayStyle = Overlay.OverlayStyle.CreatePreset(theme, mode);
            _currentOverlayStyle.DisplayMode = display;
            _currentOverlayStyle.Position = position;
            _currentOverlayStyle.FontSize = _appConfig.OverlayFontSize > 0 ? _appConfig.OverlayFontSize : 20;
            _isOverlayLocked = _appConfig.OverlayLocked;

            _dialogueHistory.SetCapacity(_appConfig.MaxHistoryEntries > 0 ? _appConfig.MaxHistoryEntries : 100);

            _isTtsEnabled = _appConfig.TtsEnabled;
            _ttsService.Mode = _isTtsEnabled ? TTS.TtsMode.AutoReadTranslated : TTS.TtsMode.Off;
            _ttsService.Rate = _appConfig.TtsRate;
            _ttsService.Volume = _appConfig.TtsVolume > 0 ? _appConfig.TtsVolume : 85;
            if (!string.IsNullOrEmpty(_appConfig.TtsVoice))
            {
                _ttsService.CurrentVoice = _appConfig.TtsVoice;
            }

            // Sync CheckBoxes
            AlwaysOnTopCheckBox.IsChecked = _appConfig.AlwaysOnTop;
            this.Topmost = _appConfig.AlwaysOnTop;
            AutoCopyClipboardCheckBox.IsChecked = _appConfig.AutoCopyToClipboard;
            OverlayToggleCheckBox.IsChecked = _appConfig.OverlayEnabled;
            TtsToggleCheckBox.IsChecked = _appConfig.TtsEnabled;
            SessionLogCheckBox.IsChecked = _appConfig.AutoSaveSession;

            SyncUiControlsWithStyle();
            UpdateLockButtonsDisplay();
            ApplyTheme(_appConfig.MainTheme);

            if (_appConfig.OverlayEnabled)
            {
                ToggleOverlay();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // AutoHost Checkboxes & UI Event Handlers
        // ═══════════════════════════════════════════════════════════════════

        private void AlwaysOnTopCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool isTop = AlwaysOnTopCheckBox.IsChecked == true;
            this.Topmost = isTop;
            _appConfig.AlwaysOnTop = isTop;
            ConfigManager.Save(_appConfig);
        }

        private void AutoCopyClipboardCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _appConfig.AutoCopyToClipboard = AutoCopyClipboardCheckBox.IsChecked == true;
            ConfigManager.Save(_appConfig);
        }

        private void OverlayToggleCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool enable = OverlayToggleCheckBox.IsChecked == true;
            if (enable != (_overlayWindow != null && _overlayWindow.IsVisible))
            {
                ToggleOverlay();
            }
        }

        private void TtsToggleCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _isTtsEnabled = TtsToggleCheckBox.IsChecked == true;
            _ttsService.Mode = _isTtsEnabled ? TTS.TtsMode.AutoReadTranslated : TTS.TtsMode.Off;
            _appConfig.TtsEnabled = _isTtsEnabled;
            ConfigManager.Save(_appConfig);
            _floatingToolbar?.SetTtsActive(_isTtsEnabled);
        }

        private void SessionLogCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _appConfig.AutoSaveSession = SessionLogCheckBox.IsChecked == true;
            _sessionManager.AutoSave = _appConfig.AutoSaveSession;
            ConfigManager.Save(_appConfig);
        }

        private void ScanIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiControls || ScanIntervalCombo?.SelectedItem is not ComboBoxItem item) return;
            if (int.TryParse(item.Tag?.ToString(), out int ms))
            {
                _appConfig.ScanIntervalMs = ms;
                ConfigManager.Save(_appConfig);
            }
        }

        private void AiModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiControls || AiModelCombo?.SelectedItem is not ComboBoxItem item) return;
            string model = item.Tag?.ToString() ?? "gemini-flash-latest";
            _appConfig.AiModel = model;
            if (_translationProvider is GeminiProvider gp)
            {
                gp.ModelName = model;
            }
            ConfigManager.Save(_appConfig);
        }

        private void SourceLangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiControls || SourceLangCombo?.SelectedItem is not ComboBoxItem item) return;
            string lang = item.Tag?.ToString() ?? "auto";
            _appConfig.SourceLanguage = lang;
            if (_translationProvider is GeminiProvider gp)
            {
                gp.SourceLanguage = lang;
            }
            ConfigManager.Save(_appConfig);
        }

        private void TargetLangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiControls || TargetLangCombo?.SelectedItem is not ComboBoxItem item) return;
            string lang = item.Tag?.ToString() ?? "vi";
            _appConfig.TargetLanguage = lang;
            if (_translationProvider is GeminiProvider gp)
            {
                gp.TargetLanguage = lang;
            }
            ConfigManager.Save(_appConfig);
        }

        private void OcrEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiControls || OcrEngineCombo?.SelectedItem is not ComboBoxItem item) return;
            string engineType = item.Tag?.ToString() ?? "WindowsOcr";
            _appConfig.OcrEngineType = engineType;
            ConfigManager.Save(_appConfig);

            // Thông báo: cần khởi động lại bot để áp dụng engine mới
            StatusText.Text = "⚙ Đã đổi engine OCR. Nhấn DỪNG rồi BẮT ĐẦU lại để áp dụng.";
            StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
        }

        // Factory method: tạo OCR engine theo loại đã chọn, fallback về Windows OCR nếu lỗi
        private IOcrEngine CreateOcrEngine(string engineType)
        {
            if (string.Equals(engineType, "OneOcr", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var oneOcr = new OneOcrEngine();
                    AppLogger.Info("[OCR_ENGINE] Đang sử dụng: OneOCR (thử nghiệm), tự động fallback về Windows OCR nếu lỗi khởi tạo.");
                    // Bọc bằng OcrEngineWithFallback: OneOCR khởi tạo DLL native theo kiểu
                    // lazy + async nên try/catch ở đây KHÔNG bắt được lỗi thật (xảy ra sau,
                    // lúc gọi OCR lần đầu) - wrapper tự phát hiện và chuyển engine khi cần.
                    return new OcrEngineWithFallback(oneOcr, new WindowsOcrEngine("en-US"));
                }
                catch (Exception ex)
                {
                    AppLogger.Info($"[OCR_ENGINE_FALLBACK] OneOCR không khởi tạo được: {ex.Message}");
                    AppLogger.Info("[OCR_ENGINE_FALLBACK] Fallback về Windows OCR.");
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = $"OneOCR lỗi, đã chuyển về Windows OCR: {ex.Message}";
                        StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    });
                }
            }

            AppLogger.Info("[OCR_ENGINE] Đang sử dụng: Windows OCR");
            return new WindowsOcrEngine("en-US");
        }

        private void RestoreSettingsUiFromConfig()
        {
            _isSyncingUiControls = true;
            try
            {
                // Đồng bộ Nhà cung cấp dịch (Translation Provider)
                string currentProvider = _appConfig.TranslationProvider ?? "Gemini";
                if (TranslationProviderCombo != null)
                {
                    foreach (ComboBoxItem item in TranslationProviderCombo.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), currentProvider, StringComparison.OrdinalIgnoreCase))
                        {
                            TranslationProviderCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                if (DeepLKeyTextBox != null)
                {
                    DeepLKeyTextBox.Text = ConfigManager.GetEffectiveDeepLApiKey(_appConfig);
                }

                UpdateProviderPanelsVisibility(currentProvider);

                // Đồng bộ Mô hình AI
                if (!string.IsNullOrEmpty(_appConfig.AiModel) && AiModelCombo != null)
                {
                    foreach (ComboBoxItem item in AiModelCombo.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), _appConfig.AiModel, StringComparison.OrdinalIgnoreCase))
                        {
                            AiModelCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Đồng bộ Ngôn ngữ đích
                if (!string.IsNullOrEmpty(_appConfig.TargetLanguage) && TargetLangCombo != null)
                {
                    foreach (ComboBoxItem item in TargetLangCombo.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), _appConfig.TargetLanguage, StringComparison.OrdinalIgnoreCase))
                        {
                            TargetLangCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Đồng bộ Ngôn ngữ nguồn
                if (!string.IsNullOrEmpty(_appConfig.SourceLanguage) && SourceLangCombo != null)
                {
                    foreach (ComboBoxItem item in SourceLangCombo.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), _appConfig.SourceLanguage, StringComparison.OrdinalIgnoreCase))
                        {
                            SourceLangCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Đồng bộ Chu kỳ quét
                if (_appConfig.ScanIntervalMs > 0 && ScanIntervalCombo != null)
                {
                    foreach (ComboBoxItem item in ScanIntervalCombo.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), _appConfig.ScanIntervalMs.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            ScanIntervalCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Đồng bộ OCR Engine
                if (!string.IsNullOrEmpty(_appConfig.OcrEngineType) && OcrEngineCombo != null)
                {
                    foreach (ComboBoxItem item in OcrEngineCombo.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), _appConfig.OcrEngineType, StringComparison.OrdinalIgnoreCase))
                        {
                            OcrEngineCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _isSyncingUiControls = false;
            }
        }

        private void TranslationProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiControls) return;

            if (TranslationProviderCombo.SelectedItem is ComboBoxItem item)
            {
                string provider = item.Tag?.ToString() ?? "Gemini";
                _appConfig.TranslationProvider = provider;
                ConfigManager.Save(_appConfig);

                if (_translationRouter != null)
                {
                    _translationRouter.PrimaryProvider = provider;
                }

                UpdateProviderPanelsVisibility(provider);
                InitializeBot();
            }
        }

        private void UpdateProviderPanelsVisibility(string provider)
        {
            if (GeminiModelRow == null || GeminiApiKeyRow == null || 
                DeepLSettingsRow == null || GoogleWebNoteBorder == null) return;

            if (provider.Equals("DeepL", StringComparison.OrdinalIgnoreCase))
            {
                GeminiModelRow.Visibility = Visibility.Collapsed;
                GeminiApiKeyRow.Visibility = Visibility.Collapsed;
                DeepLSettingsRow.Visibility = Visibility.Visible;
                GoogleWebNoteBorder.Visibility = Visibility.Collapsed;
            }
            else if (provider.Equals("GoogleWeb", StringComparison.OrdinalIgnoreCase))
            {
                GeminiModelRow.Visibility = Visibility.Collapsed;
                GeminiApiKeyRow.Visibility = Visibility.Collapsed;
                DeepLSettingsRow.Visibility = Visibility.Collapsed;
                GoogleWebNoteBorder.Visibility = Visibility.Visible;
            }
            else // Gemini
            {
                GeminiModelRow.Visibility = Visibility.Visible;
                GeminiApiKeyRow.Visibility = Visibility.Visible;
                DeepLSettingsRow.Visibility = Visibility.Collapsed;
                GoogleWebNoteBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void SaveDeepLKeyBtn_Click(object sender, RoutedEventArgs e)
        {
            string key = DeepLKeyTextBox?.Text?.Trim() ?? string.Empty;
            ConfigManager.SetEffectiveDeepLApiKey(_appConfig, key);
            if (_deepLProvider != null)
            {
                _deepLProvider.ApiKey = key;
            }
            InitializeBot();

            string planType = key.EndsWith(":fx", StringComparison.OrdinalIgnoreCase) ? "Free Plan (:fx)" : "Pro Plan";
            MessageBox.Show($"Đã lưu DeepL Auth Key ({planType}) thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CopyTextBtn_Click(object sender, RoutedEventArgs e)
        {
            string text = TranslatedTextLabel.Text;
            if (!string.IsNullOrWhiteSpace(text) && text != "Vùng dịch thuật sẽ hiển thị ở đây.")
            {
                try
                {
                    Clipboard.SetText(text);
                    HotkeyHintText.Text = "● Đã sao chép câu dịch vào Clipboard!";
                }
                catch { }
            }
        }

        private void SpeakCurrentBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TranslatedTextLabel.Text) &&
                TranslatedTextLabel.Text != "Vùng dịch thuật sẽ hiển thị ở đây.")
            {
                _ttsService.SpeakAsync(TranslatedTextLabel.Text, cancelPrevious: true);
            }
        }

        public void ToggleTts()
        {
            _isTtsEnabled = !_isTtsEnabled;
            _ttsService.Mode = _isTtsEnabled ? TTS.TtsMode.AutoReadTranslated : TTS.TtsMode.Off;
            _appConfig.TtsEnabled = _isTtsEnabled;
            ConfigManager.Save(_appConfig);
            if (TtsToggleCheckBox != null) TtsToggleCheckBox.IsChecked = _isTtsEnabled;
            _floatingToolbar?.SetTtsActive(_isTtsEnabled);
        }

        private void ApiKeyBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new UI.ApiKeyManagerWindow(_keyPool, _appConfig)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                InitializeBot();
                MessageBox.Show("Đã cập nhật cấu hình API Key Pool thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Bot Lifecycle (AutoHost Style: HOẠT ĐỘNG / ĐÃ TẠM DỪNG)
        // ═══════════════════════════════════════════════════════════════════

        private void ToggleBotBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_engine == null)
            {
                ApiKeyBtn_Click(sender, e);
                if (_engine == null) return;
            }

            _isBotRunning = !_isBotRunning;

            if (_isBotRunning)
            {
                _sessionManager.StartSession();
                _engine.Start();
                ToggleBotBtn.Content = "TẠM DỪNG HOOK";
                ToggleBotBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E04747"));
                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                StatusText.Text = "HOẠT ĐỘNG";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else
            {
                _engine.Stop();
                _sessionManager.EndSession();
                ToggleBotBtn.Content = "BẮT ĐẦU DỊCH";
                ToggleBotBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                StatusText.Text = "ĐÃ TẠM DỪNG";
                StatusText.Foreground = Resources["BrushTextSecondary"] as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
                PerformanceMetricsText.Text = "";
            }

            _floatingToolbar?.SetBotRunning(_isBotRunning);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Theme Engine Toàn Cục (AutoHost Dark, Chaldea, High Contrast, Light)
        // ═══════════════════════════════════════════════════════════════════

        private void ApplyTheme(string? themeName = null)
        {
            themeName ??= _appConfig.MainTheme ?? "AutoHostDark";

            SolidColorBrush bgBrush;
            SolidColorBrush cardBgBrush;
            SolidColorBrush cardBorderBrush;
            SolidColorBrush headerBrush;
            SolidColorBrush textPrimaryBrush;
            SolidColorBrush textSecondaryBrush;
            SolidColorBrush textMutedBrush;
            Overlay.OverlayTheme overlayTheme;

            switch (themeName.ToLowerInvariant())
            {
                case "fgochaldea":
                    // FGO Chaldea (Chaldea Navy + Gold Accent)
                    bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B132B"));
                    cardBgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111D3B"));
                    cardBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A4374"));
                    headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                    textPrimaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7ED"));
                    textSecondaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    textMutedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

                    Resources["BrushKeyBadgeBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2E56"));
                    Resources["BrushKeyBadgeBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    Resources["BrushKeyBadgeFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                    Resources["BrushTabFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    Resources["BrushTabHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B2A4A"));
                    Resources["BrushTabHoverFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                    Resources["BrushTabSelectedBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2E56"));
                    Resources["BrushTabSelectedFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                    Resources["BrushTabSelectedBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                    Resources["BrushButtonBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#172545"));
                    Resources["BrushButtonBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A4374"));
                    Resources["BrushButtonFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7ED"));
                    Resources["BrushInputBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#172545"));
                    Resources["BrushInputBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A4374"));
                    Resources["BrushInputFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7ED"));
                    Resources["BrushCheckBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#172545"));
                    overlayTheme = Overlay.OverlayTheme.FgoChaldea;
                    break;

                case "highcontrast":
                    // High Contrast (Pure Black + Vivid Amber)
                    bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000000"));
                    cardBgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A0A"));
                    cardBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                    textPrimaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                    textSecondaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE047"));
                    textMutedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

                    Resources["BrushKeyBadgeBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A"));
                    Resources["BrushKeyBadgeBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    Resources["BrushKeyBadgeFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE047"));
                    Resources["BrushTabFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE047"));
                    Resources["BrushTabHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A"));
                    Resources["BrushTabHoverFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                    Resources["BrushTabSelectedBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#262626"));
                    Resources["BrushTabSelectedFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                    Resources["BrushTabSelectedBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    Resources["BrushButtonBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141414"));
                    Resources["BrushButtonBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    Resources["BrushButtonFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                    Resources["BrushInputBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141414"));
                    Resources["BrushInputBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    Resources["BrushInputFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                    Resources["BrushCheckBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000000"));
                    overlayTheme = Overlay.OverlayTheme.HighContrast;
                    break;

                case "light":
                    // Clean Light Studio
                    bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
                    cardBgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                    cardBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                    headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
                    textPrimaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    textSecondaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
                    textMutedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

                    Resources["BrushKeyBadgeBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0F2FE"));
                    Resources["BrushKeyBadgeBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BAE6FD"));
                    Resources["BrushKeyBadgeFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
                    Resources["BrushTabFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                    Resources["BrushTabHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
                    Resources["BrushTabHoverFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    Resources["BrushTabSelectedBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0F2FE"));
                    Resources["BrushTabSelectedFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
                    Resources["BrushTabSelectedBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
                    Resources["BrushButtonBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                    Resources["BrushButtonBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                    Resources["BrushButtonFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    Resources["BrushInputBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                    Resources["BrushInputBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                    Resources["BrushInputFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    Resources["BrushCheckBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
                    overlayTheme = Overlay.OverlayTheme.Light;
                    break;

                case "autohostdark":
                default:
                    // Studio Dark (Deep Obsidian + Electric Cyan Accent)
                    bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B0F19"));
                    cardBgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"));
                    cardBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
                    headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
                    textPrimaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                    textSecondaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    textMutedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

                    Resources["BrushKeyBadgeBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F2442"));
                    Resources["BrushKeyBadgeBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                    Resources["BrushKeyBadgeFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA"));
                    Resources["BrushTabFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    Resources["BrushTabHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#151D2C"));
                    Resources["BrushTabHoverFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                    Resources["BrushTabSelectedBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                    Resources["BrushTabSelectedFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
                    Resources["BrushTabSelectedBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
                    Resources["BrushButtonBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#161E2E"));
                    Resources["BrushButtonBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                    Resources["BrushButtonFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
                    Resources["BrushInputBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#161E2E"));
                    Resources["BrushInputBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                    Resources["BrushInputFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
                    Resources["BrushCheckBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#161F30"));
                    overlayTheme = Overlay.OverlayTheme.Dark;
                    break;
            }

            // Áp dụng màu chung lên Resources và cửa sổ chính
            Resources["BrushAppBg"] = bgBrush;
            Resources["BrushCardBg"] = cardBgBrush;
            Resources["BrushCardBorder"] = cardBorderBrush;
            Resources["BrushTextPrimary"] = textPrimaryBrush;
            Resources["BrushTextSecondary"] = textSecondaryBrush;
            Resources["BrushTextMuted"] = textMutedBrush;

            AppWindow.Background = bgBrush;
            AppWindow.Foreground = textPrimaryBrush;

            if (Card1 != null) { Card1.Background = cardBgBrush; Card1.BorderBrush = cardBorderBrush; }
            if (Card2 != null) { Card2.Background = cardBgBrush; Card2.BorderBrush = cardBorderBrush; }
            if (Card3 != null) { Card3.Background = cardBgBrush; Card3.BorderBrush = cardBorderBrush; }
            if (TranslationCard != null) { TranslationCard.Background = cardBgBrush; TranslationCard.BorderBrush = cardBorderBrush; }

            if (BrandTitleText != null) BrandTitleText.Foreground = headerBrush;
            if (!_isBotRunning && StatusText != null) StatusText.Foreground = textSecondaryBrush;
            if (TranslatedTextLabel != null) TranslatedTextLabel.Foreground = textPrimaryBrush;
            if (OriginalTextLabel != null) OriginalTextLabel.Foreground = textSecondaryBrush;
            if (WindowInfoText != null) WindowInfoText.Foreground = textSecondaryBrush;
            if (HotkeyHintText != null) HotkeyHintText.Foreground = textMutedBrush;

            // Đồng bộ theme cho OverlayWindow
            var newStyle = Overlay.OverlayStyle.CreatePreset(overlayTheme, _currentOverlayStyle.Mode);
            newStyle.DisplayMode = _currentOverlayStyle.DisplayMode;
            newStyle.Position = _currentOverlayStyle.Position;
            newStyle.FontSize = _currentOverlayStyle.FontSize;
            _currentOverlayStyle = newStyle;
            _overlayWindow?.ApplyStyle(_currentOverlayStyle);
            _appConfig.OverlayTheme = overlayTheme.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Helpers
        // ═══════════════════════════════════════════════════════════════════

        private void RestoreCaptureFromConfig()
        {
            if (_engine == null) return;

            if (_appConfig.UseWindowRelativeCapture && !string.IsNullOrEmpty(_appConfig.TargetWindowTitle))
            {
                var found = WindowEnumerator.FindByTitleAndProcess(
                    _appConfig.TargetWindowTitle ?? "", _appConfig.TargetProcessName ?? "");

                if (found != null)
                {
                    _selectedWindow = found;
                    _engine.SetWindowTarget(found.Handle, found.Title, found.ProcessName);
                }
                else
                {
                    _engine.SetWindowTarget(IntPtr.Zero, _appConfig.TargetWindowTitle, _appConfig.TargetProcessName);
                }

                if (_appConfig.RelativeCaptureWidth > 0 && _appConfig.RelativeCaptureHeight > 0)
                {
                    _engine.SetWindowRelativeRegion(
                        _appConfig.RelativeCaptureX, _appConfig.RelativeCaptureY,
                        _appConfig.RelativeCaptureWidth, _appConfig.RelativeCaptureHeight);
                }
            }
            else if (_appConfig.CaptureWidth > 0 && _appConfig.CaptureHeight > 0)
            {
                _engine.SetCaptureRegion(_appConfig.CaptureX, _appConfig.CaptureY,
                    _appConfig.CaptureWidth, _appConfig.CaptureHeight);
            }
            else
            {
                _engine.SetCaptureRegion(0, 0, 0, 0);
            }

            UpdateWindowInfoDisplay();
        }

        private void RestoreOcrAreasFromConfig()
        {
            if (_engine == null) return;

            _engine.OcrAreaManager.LoadFrom(_appConfig.OcrAreas);
            _engine.ExclusionManager.LoadFrom(_appConfig.ExclusionAreas);

            // Floating toolbar
            if (_appConfig.ShowToolbar)
            {
                ToggleFloatingToolbar();
            }
        }

        private void UpdateWindowInfoDisplay()
        {
            var areaCount = _engine?.OcrAreaManager.GetEnabledAreas().Count ?? 0;
            var exCount = _engine?.ExclusionManager.Exclusions.Count ?? 0;
            string areaInfo = areaCount > 0 ? $"  ·  {areaCount} vùng" : "";
            string exInfo = exCount > 0 ? $"  ·  {exCount} loại trừ" : "";

            if (_appConfig.UseWindowRelativeCapture)
            {
                string windowName = _selectedWindow?.Title ?? _appConfig.TargetWindowTitle ?? "(chưa tìm thấy)";
                if (windowName.Length > 35) windowName = windowName[..32] + "...";

                if (_appConfig.RelativeCaptureWidth > 0 || areaCount > 0)
                {
                    WindowInfoText.Text = $"{windowName}{areaInfo}{exInfo}";
                }
                else
                {
                    WindowInfoText.Text = $"{windowName}  ·  Chưa chọn vùng quét";
                }
                StatusText.Text = "Sẵn sàng";
                _detachedSubtitleWindow?.SetTargetWindow(windowName);
            }
            else if (_appConfig.CaptureWidth > 0 || areaCount > 0)
            {
                WindowInfoText.Text = $"{_appConfig.CaptureWidth}×{_appConfig.CaptureHeight} (Toàn màn hình){areaInfo}{exInfo}";
                StatusText.Text = "Sẵn sàng";
                _detachedSubtitleWindow?.SetTargetWindow("Toàn màn hình");
            }
            else
            {
                WindowInfoText.Text = "";
                StatusText.Text = "Chưa chọn vùng quét";
                _detachedSubtitleWindow?.SetTargetWindow("Toàn màn hình");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Shutdown (Instant & Clean)
        // ═══════════════════════════════════════════════════════════════════

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 1. Ẩn cửa sổ ngay tức thì để người dùng thấy app tắt không bị đơ
            this.Hide();

            // 2. Gỡ hotkey toàn cục
            try
            {
                _hotkeyManager.Dispose();
            }
            catch { }

            // 3. Đóng ngay các cửa sổ con
            try
            {
                _floatingToolbar?.Close();
                _detachedSubtitleWindow?.Close();
                _overlayWindow?.Close();
                _historyWindow?.Close();
            }
            catch { }

            // 4. Dừng engine và giải phóng tài nguyên nhanh chóng
            try
            {
                _engine?.Stop();
                _captureService?.Dispose();
                _sessionManager.EndSession();
                _ttsService.Dispose();
            }
            catch { }

            base.OnClosing(e);
            Application.Current.Shutdown();
        }
    }
}