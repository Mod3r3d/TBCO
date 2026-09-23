using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TranslateBot.Capture;

namespace TranslateBot.Overlay
{
    public partial class OverlayWindow : Window
    {
        public OverlayStyle CurrentStyle { get; private set; } = OverlayStyle.CreatePreset(OverlayTheme.FgoChaldea, OverlayMode.Overlay);
        public bool IsLocked { get; private set; } = false;
        public IntPtr TargetGameWindowHandle { get; set; } = IntPtr.Zero;

        public Action? OnRequestOpenHistory { get; set; }
        public Action<OverlayStyle, bool, Rect>? OnOverlayConfigChanged { get; set; }

        private string _lastOriginal = "";
        private string _lastTranslated = "Vùng phụ đề game sẵn sàng...";
        private string? _lastSpeaker = null;
        private int _lastSequenceId = 0;
        private double _lastLatencyMs = 0;
        private bool _lastCacheHit = false;

        public OverlayWindow()
        {
            InitializeComponent();
            ApplyStyle(CurrentStyle);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Áp dụng trạng thái khóa ban đầu sau khi có Handle
            if (IsLocked)
            {
                ApplyClickThrough(true);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Styling & Themes
        // ═══════════════════════════════════════════════════════════════════

        public void ApplyStyle(OverlayStyle style)
        {
            CurrentStyle = style ?? throw new ArgumentNullException(nameof(style));

            // Card container
            OverlayCard.Background = style.BackgroundBrush;
            OverlayCard.BorderBrush = style.BorderBrush;
            OverlayCard.BorderThickness = style.BorderThickness;
            OverlayCard.CornerRadius = style.CornerRadius;
            OverlayCard.Padding = style.Padding;
            OverlayCard.Opacity = style.CardOpacity;

            // Card DropShadow
            CardShadow.Color = style.ShadowColor;
            CardShadow.Opacity = style.ShadowOpacity;
            CardShadow.BlurRadius = style.ShadowBlurRadius;

            // Translated text
            TranslatedLabel.Foreground = style.TextBrush;
            TranslatedLabel.FontSize = style.FontSize;
            TranslatedLabel.FontFamily = new FontFamily(style.FontFamily);
            TranslatedLabel.FontWeight = style.FontWeight;

            // Text glow / outline shadow
            if (style.HasTextShadow)
            {
                TextGlowEffect.Color = style.ShadowColor;
                TextGlowEffect.Opacity = style.ShadowOpacity;
                TextGlowEffect.BlurRadius = style.ShadowBlurRadius;
            }
            else
            {
                TextGlowEffect.Opacity = 0;
            }

            // Original text
            OriginalLabel.Foreground = style.SecondaryTextBrush;
            OriginalLabel.FontSize = style.OriginalFontSize;
            OriginalLabel.FontFamily = new FontFamily(style.FontFamily);

            // Speaker badge
            SpeakerBadge.Background = style.SpeakerBadgeBrush;
            SpeakerLabel.Foreground = style.SpeakerTextBrush;

            // Debug text
            DebugMetricsLabel.Foreground = style.DebugTextBrush;

            // UI Badges
            ModeBadge.Text = $"HUD: {style.Mode}";
            ThemeBadge.Text = $"Theme: {style.Theme}";
            ThemeBadge.Foreground = style.SpeakerBadgeBrush;

            // Refresh dialogue display with updated style
            RenderCurrentDialogue();

            NotifyStateChanged();
        }

        public void CycleTheme()
        {
            var nextTheme = CurrentStyle.Theme switch
            {
                OverlayTheme.FgoChaldea => OverlayTheme.Dark,
                OverlayTheme.Dark => OverlayTheme.Light,
                OverlayTheme.Light => OverlayTheme.HighContrast,
                OverlayTheme.HighContrast => OverlayTheme.FgoChaldea,
                _ => OverlayTheme.FgoChaldea
            };

            var newStyle = OverlayStyle.CreatePreset(nextTheme, CurrentStyle.Mode);
            newStyle.DisplayMode = CurrentStyle.DisplayMode;
            newStyle.Position = CurrentStyle.Position;
            newStyle.FontSize = CurrentStyle.FontSize;
            ApplyStyle(newStyle);
        }

        public void CycleMode()
        {
            var nextMode = CurrentStyle.Mode == OverlayMode.Overlay ? OverlayMode.Layer : OverlayMode.Overlay;
            var newStyle = OverlayStyle.CreatePreset(CurrentStyle.Theme, nextMode);
            newStyle.DisplayMode = CurrentStyle.DisplayMode;
            newStyle.Position = CurrentStyle.Position;
            newStyle.FontSize = CurrentStyle.FontSize;
            ApplyStyle(newStyle);
        }

        public void CycleDisplayMode()
        {
            CurrentStyle.DisplayMode = CurrentStyle.DisplayMode switch
            {
                DialogueDisplayMode.Both => DialogueDisplayMode.TranslationOnly,
                DialogueDisplayMode.TranslationOnly => DialogueDisplayMode.Debug,
                DialogueDisplayMode.Debug => DialogueDisplayMode.Both,
                _ => DialogueDisplayMode.Both
            };

            DisplayModeBtn.Content = CurrentStyle.DisplayMode switch
            {
                DialogueDisplayMode.Both => "Song ngữ",
                DialogueDisplayMode.TranslationOnly => "Chỉ dịch",
                DialogueDisplayMode.Debug => "Debug",
                _ => "Hiển thị"
            };

            RenderCurrentDialogue();
            NotifyStateChanged();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Win32 Click-Through & Locking (Section 20)
        // ═══════════════════════════════════════════════════════════════════

        public void SetLocked(bool locked)
        {
            IsLocked = locked;
            ApplyClickThrough(locked);

            if (IsLocked)
            {
                MiniControlBar.Visibility = Visibility.Collapsed;
                LockStatusBadge.Text = "[Đã khóa - F9]";
                LockStatusBadge.Foreground = Brushes.LightGreen;
                LockBtn.Content = "Mở [F9]";
                ResizeMode = ResizeMode.NoResize;
            }
            else
            {
                MiniControlBar.Visibility = Visibility.Visible;
                LockStatusBadge.Text = "[Tự do]";
                LockStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                LockBtn.Content = "Khóa [F9]";
                ResizeMode = ResizeMode.CanResizeWithGrip;
            }

            NotifyStateChanged();
        }

        public void ToggleLock() => SetLocked(!IsLocked);

        private void ApplyClickThrough(bool clickThrough)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetClickThrough(hwnd, clickThrough);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Snapping / Positioning (Top, Bottom, Custom)
        // ═══════════════════════════════════════════════════════════════════

        public void SnapTo(OverlayPosition position)
        {
            CurrentStyle.Position = position;

            if (position == OverlayPosition.Custom)
            {
                NotifyStateChanged();
                return;
            }

            NativeMethods.RECT targetRect = new();
            bool hasTargetWindow = TargetGameWindowHandle != IntPtr.Zero 
                && NativeMethods.GetWindowRect(TargetGameWindowHandle, out targetRect);

            double targetX = hasTargetWindow ? targetRect.Left : 0;
            double targetY = hasTargetWindow ? targetRect.Top : 0;
            double targetW = hasTargetWindow ? targetRect.Width : SystemParameters.WorkArea.Width;
            double targetH = hasTargetWindow ? targetRect.Height : SystemParameters.WorkArea.Height;

            // Đặt chiều rộng tương đối với game window, linh hoạt từ màn hình 14 inch tới 16+ inch
            Width = Math.Clamp(targetW * 0.85, 340, 1100);
            Left = targetX + (targetW - Width) / 2;

            if (position == OverlayPosition.Bottom)
            {
                Top = targetY + targetH - Height - (hasTargetWindow ? 30 : 80);
            }
            else if (position == OverlayPosition.Top)
            {
                Top = targetY + (hasTargetWindow ? 40 : 50);
            }

            NotifyStateChanged();
        }

        public void SnapNextPosition()
        {
            var next = CurrentStyle.Position switch
            {
                OverlayPosition.Bottom => OverlayPosition.Top,
                OverlayPosition.Top => OverlayPosition.Custom,
                OverlayPosition.Custom => OverlayPosition.Bottom,
                _ => OverlayPosition.Bottom
            };
            SnapTo(next);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Dialogue Updating & Rendering
        // ═══════════════════════════════════════════════════════════════════

        public void UpdateDialogue(string original, string translated, string? speaker = null, 
            int sequenceId = 0, double latencyMs = 0, bool isCacheHit = false)
        {
            _lastOriginal = original ?? "";
            _lastTranslated = translated ?? "";
            _lastSpeaker = speaker;
            _lastSequenceId = sequenceId;
            _lastLatencyMs = latencyMs;
            _lastCacheHit = isCacheHit;

            Dispatcher.Invoke(RenderCurrentDialogue);
        }

        private void RenderCurrentDialogue()
        {
            TranslatedLabel.Text = string.IsNullOrWhiteSpace(_lastTranslated) ? "..." : _lastTranslated;
            OriginalLabel.Text = _lastOriginal;

            // Speaker badge
            if (!string.IsNullOrWhiteSpace(_lastSpeaker))
            {
                SpeakerBadge.Visibility = Visibility.Visible;
                SpeakerLabel.Text = _lastSpeaker;
            }
            else
            {
                SpeakerBadge.Visibility = Visibility.Collapsed;
            }

            // Display Mode: Song ngữ, Dịch thuần, hay Debug
            switch (CurrentStyle.DisplayMode)
            {
                case DialogueDisplayMode.TranslationOnly:
                    OriginalLabel.Visibility = Visibility.Collapsed;
                    DebugFooter.Visibility = Visibility.Collapsed;
                    break;

                case DialogueDisplayMode.Debug:
                    OriginalLabel.Visibility = string.IsNullOrWhiteSpace(_lastOriginal) ? Visibility.Collapsed : Visibility.Visible;
                    DebugFooter.Visibility = Visibility.Visible;
                    string cacheTag = _lastCacheHit ? "HIT (0ms)" : "MISS";
                    DebugMetricsLabel.Text = $"#{_lastSequenceId:D4} | Latency: {_lastLatencyMs:F0}ms | TM: {cacheTag} | Mode: {CurrentStyle.Mode}";
                    break;

                case DialogueDisplayMode.Both:
                case DialogueDisplayMode.OriginalAbove:
                default:
                    OriginalLabel.Visibility = string.IsNullOrWhiteSpace(_lastOriginal) ? Visibility.Collapsed : Visibility.Visible;
                    DebugFooter.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Interaction Handlers
        // ═══════════════════════════════════════════════════════════════════

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsLocked && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!IsLocked)
            {
                MiniControlBar.Visibility = Visibility.Visible;
            }
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!IsLocked)
            {
                // Giữ mini bar nếu chuột ở trong hoặc có thể ẩn bớt
                // MiniControlBar.Visibility = Visibility.Visible;
            }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (!IsLocked)
            {
                CurrentStyle.Position = OverlayPosition.Custom;
                NotifyStateChanged();
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLocked)
            {
                NotifyStateChanged();
            }
        }

        private void LockBtn_Click(object sender, RoutedEventArgs e) => ToggleLock();
        private void ModeCycleBtn_Click(object sender, RoutedEventArgs e) => CycleMode();
        private void ThemeCycleBtn_Click(object sender, RoutedEventArgs e) => CycleTheme();
        private void DisplayModeBtn_Click(object sender, RoutedEventArgs e) => CycleDisplayMode();
        private void SnapBtn_Click(object sender, RoutedEventArgs e) => SnapNextPosition();
        private void HistoryBtn_Click(object sender, RoutedEventArgs e) => OnRequestOpenHistory?.Invoke();
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();

        private void NotifyStateChanged()
        {
            var rect = new Rect(Left, Top, Width, Height);
            OnOverlayConfigChanged?.Invoke(CurrentStyle, IsLocked, rect);
        }
    }
}
