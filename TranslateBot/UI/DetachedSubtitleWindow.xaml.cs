using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TranslateBot.Capture;

namespace TranslateBot.UI
{
    /// <summary>
    /// Cửa sổ hiển thị văn bản dịch độc lập (Detached Floating Subtitle Window).
    /// Cho phép người dùng thu nhỏ app chính mà vẫn theo dõi phụ đề game mượt mà.
    /// </summary>
    public partial class DetachedSubtitleWindow : Window
    {
        public bool IsLocked { get; private set; } = false;
        private double _currentFontSize = 16.0;

        // Callbacks gửi sự kiện về cho MainWindow
        public Action? OnRequestSpeak { get; set; }
        public Action? OnRequestCopy { get; set; }
        public Action? OnRequestHistory { get; set; }
        public Action? OnRequestSelectWindow { get; set; }
        public Action? OnRequestDockBack { get; set; }
        public Action<double, double, double, double>? OnBoundsChanged { get; set; }
        public Action<double>? OnFontSizeChanged { get; set; }
        public Action<bool>? OnLockChanged { get; set; }
        public Action<bool>? OnTopmostChanged { get; set; }

        public DetachedSubtitleWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsLocked)
            {
                ApplyClickThrough(true);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            OnBoundsChanged?.Invoke(Left, Top, Width, Height);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Public API để cập nhật dữ liệu từ MainWindow
        // ═══════════════════════════════════════════════════════════════════

        public void UpdateDialogue(string original, string translated, string? speaker = null, string? previous = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(speaker))
                {
                    SpeakerBadge.Visibility = Visibility.Visible;
                    SpeakerText.Text = speaker;
                }
                else
                {
                    SpeakerBadge.Visibility = Visibility.Collapsed;
                }

                if (!string.IsNullOrWhiteSpace(previous))
                {
                    PreviousText.Visibility = Visibility.Visible;
                    PreviousText.Text = previous;
                }
                else
                {
                    PreviousText.Visibility = Visibility.Collapsed;
                }

                TranslatedText.Text = string.IsNullOrWhiteSpace(translated) ? "..." : translated;
                OriginalText.Text = string.IsNullOrWhiteSpace(original) ? "" : original;
            });
        }

        public void SetTargetWindow(string title)
        {
            Dispatcher.Invoke(() =>
            {
                TargetWindowTitleText.Text = string.IsNullOrWhiteSpace(title) ? "Toàn màn hình" : title;
            });
        }

        public void SetFontSize(double size)
        {
            if (size < 11) size = 11;
            if (size > 36) size = 36;
            _currentFontSize = size;
            TranslatedText.FontSize = _currentFontSize;
        }

        public void SetTopmost(bool topmost)
        {
            Topmost = topmost;
            PinToggleBtn.Content = topmost ? "Ghim" : "Bỏ ghim";
            PinToggleBtn.Foreground = topmost 
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
        }

        public void SetLocked(bool locked)
        {
            IsLocked = locked;
            ApplyClickThrough(locked);

            if (IsLocked)
            {
                LockToggleBtn.Content = "LOCK";
                LockToggleBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                MainContainer.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else
            {
                LockToggleBtn.Content = "F9";
                LockToggleBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                MainContainer.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
            }

            OnLockChanged?.Invoke(IsLocked);
        }

        public void ToggleLock()
        {
            SetLocked(!IsLocked);
        }

        private void ApplyClickThrough(bool clickThrough)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetClickThrough(hwnd, clickThrough);
                }
            }
            catch { }
        }

        public void ApplyRestoredBounds(double x, double y, double width, double height)
        {
            double screenW = SystemParameters.VirtualScreenWidth > 0 ? SystemParameters.VirtualScreenWidth : 1920;
            double screenH = SystemParameters.VirtualScreenHeight > 0 ? SystemParameters.VirtualScreenHeight : 1080;

            if (width >= 350 && width <= screenW) Width = width;
            if (height >= 100 && height <= screenH) Height = height;

            if (x < 0 || x > screenW - 100)
                x = Math.Max(20, (SystemParameters.PrimaryScreenWidth - Width) / 2);
            if (y < 0 || y > screenH - 50)
                y = Math.Max(20, SystemParameters.PrimaryScreenHeight - Height - 100);

            Left = x;
            Top = y;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Event Handlers
        // ═══════════════════════════════════════════════════════════════════

        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    DragMove();
                    OnBoundsChanged?.Invoke(Left, Top, Width, Height);
                }
                catch { }
            }
        }

        private void SelectWindow_Click(object sender, RoutedEventArgs e)
        {
            OnRequestSelectWindow?.Invoke();
        }

        private void Speak_Click(object sender, RoutedEventArgs e)
        {
            OnRequestSpeak?.Invoke();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            OnRequestCopy?.Invoke();
        }

        private void History_Click(object sender, RoutedEventArgs e)
        {
            OnRequestHistory?.Invoke();
        }

        private void FontInc_Click(object sender, RoutedEventArgs e)
        {
            SetFontSize(_currentFontSize + 1.5);
            OnFontSizeChanged?.Invoke(_currentFontSize);
        }

        private void FontDec_Click(object sender, RoutedEventArgs e)
        {
            SetFontSize(_currentFontSize - 1.5);
            OnFontSizeChanged?.Invoke(_currentFontSize);
        }

        private void PinToggle_Click(object sender, RoutedEventArgs e)
        {
            SetTopmost(!Topmost);
            OnTopmostChanged?.Invoke(Topmost);
        }

        private void LockToggle_Click(object sender, RoutedEventArgs e)
        {
            ToggleLock();
        }

        private void DockBack_Click(object sender, RoutedEventArgs e)
        {
            OnRequestDockBack?.Invoke();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            OnRequestDockBack?.Invoke();
        }
    }
}
