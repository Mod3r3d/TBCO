using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TranslateBot.UI
{
    // Thanh điều khiển nổi (Section 19): AlwaysOnTop, nhỏ, draggable, nửa trong suốt.
    // Người chơi có thể kéo sang góc nào cũng được, toolbar không che thoại game.
    // Tất cả action đều delegate về MainWindow qua callback.
    public partial class FloatingToolbar : Window
    {
        private bool _isBotRunning;

        // Callbacks — MainWindow gán các hành động tương ứng
        public Action? OnToggleBot { get; set; }
        public Action? OnSelectRegion { get; set; }
        public Action? OnSnapshot { get; set; }
        public Action? OnSelectWindow { get; set; }
        public Action? OnShowMainWindow { get; set; }
        public Action? OnToggleOverlay { get; set; }
        public Action? OnToggleLock { get; set; }
        public Action? OnToggleTts { get; set; }
        public Action? OnOpenHistory { get; set; }

        // MainWindow gọi để đồng bộ trạng thái bot
        public Action<double, double>? OnPositionChanged { get; set; }

        public FloatingToolbar()
        {
            InitializeComponent();
            Opacity = 0.65; // Nửa trong suốt khi không hover
        }

        // ═══════════════════════════════════════════════════════════════════
        // State Sync — MainWindow gọi khi bot start/stop hoặc overlay thay đổi
        // ═══════════════════════════════════════════════════════════════════

        public void SetBotRunning(bool running)
        {
            _isBotRunning = running;
            ToggleBtn.Content = running ? "||" : "▶";
            StatusDot.Fill = new SolidColorBrush(
                running ? (Color)ColorConverter.ConvertFromString("#10B981")  // xanh lá
                        : (Color)ColorConverter.ConvertFromString("#6B7280")); // xám
        }

        public void SetOverlayActive(bool active)
        {
            OverlayToggleBtn.Foreground = active
                ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) // Xanh dương sáng
                : new SolidColorBrush(Color.FromRgb(209, 213, 219)); // Xám
        }

        public void SetOverlayLocked(bool locked)
        {
            LockToggleBtn.Content = locked ? "LOCK" : "F9";
            LockToggleBtn.Foreground = locked
                ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) // Xanh lá đã khóa
                : new SolidColorBrush(Color.FromRgb(209, 213, 219));
        }

        public void SetTtsActive(bool active)
        {
            TtsToggleBtn.Foreground = active
                ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) // Vàng cam khi bật
                : new SolidColorBrush(Color.FromRgb(209, 213, 219)); // Xám khi tắt
        }

        // ═══════════════════════════════════════════════════════════════════
        // Button Handlers — delegate tất cả về MainWindow
        // ═══════════════════════════════════════════════════════════════════

        private void ToggleBtn_Click(object sender, RoutedEventArgs e) => OnToggleBot?.Invoke();
        private void RegionBtn_Click(object sender, RoutedEventArgs e) => OnSelectRegion?.Invoke();
        private void SnapshotBtn_Click(object sender, RoutedEventArgs e) => OnSnapshot?.Invoke();
        private void WindowBtn_Click(object sender, RoutedEventArgs e) => OnSelectWindow?.Invoke();
        private void OverlayToggleBtn_Click(object sender, RoutedEventArgs e) => OnToggleOverlay?.Invoke();
        private void LockToggleBtn_Click(object sender, RoutedEventArgs e) => OnToggleLock?.Invoke();
        private void TtsToggleBtn_Click(object sender, RoutedEventArgs e) => OnToggleTts?.Invoke();
        private void HistoryBtn_Click(object sender, RoutedEventArgs e) => OnOpenHistory?.Invoke();
        private void MainWindowBtn_Click(object sender, RoutedEventArgs e) => OnShowMainWindow?.Invoke();

        // ═══════════════════════════════════════════════════════════════════
        // Dragging + Opacity animation
        // ═══════════════════════════════════════════════════════════════════

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    DragMove();
                    OnPositionChanged?.Invoke(Left, Top);
                }
                catch { }
            }
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateOpacity(1.0);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateOpacity(0.65);
        }

        private void AnimateOpacity(double target)
        {
            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, animation);
        }

        // Đặt vị trí từ config đã lưu
        public void RestorePosition(double x, double y)
        {
            double screenW = SystemParameters.VirtualScreenWidth > 0 ? SystemParameters.VirtualScreenWidth : 1920;
            double screenH = SystemParameters.VirtualScreenHeight > 0 ? SystemParameters.VirtualScreenHeight : 1080;

            if (x <= 0 || x > screenW - 100)
                x = Math.Max(20, (SystemParameters.PrimaryScreenWidth - Width) / 2);
            if (y <= 0 || y > screenH - 50)
                y = 40;

            Left = x;
            Top = y;
        }
    }
}
