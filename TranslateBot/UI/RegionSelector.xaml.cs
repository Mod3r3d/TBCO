using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TranslateBot.UI
{
    public partial class RegionSelector : Window
    {
        private Point _startPoint;
        private bool _isDragging = false;
        
        // ── Callback screen-absolute (giữ nguyên API cũ) ──────────────
        // Trả về (x, y, width, height) — pixel vật lý, tọa độ màn hình
        public Action<int, int, int, int>? OnRegionSelected { get; set; }

        // ── Stage 3A: Callback window-relative ─────────────────────────
        // Trả về (relX, relY, width, height) — pixel vật lý, tương đối client area
        // Chỉ được gọi khi WindowHandle != IntPtr.Zero
        public Action<int, int, int, int>? OnWindowRelativeRegionSelected { get; set; }

        // Nếu set giá trị này, RegionSelector sẽ tính tọa độ tương đối so với
        // client area của cửa sổ thay vì tọa độ tuyệt đối màn hình.
        public IntPtr WindowHandle { get; set; } = IntPtr.Zero;

        public RegionSelector()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _startPoint = e.GetPosition(DrawCanvas);
                SelectionBox.Visibility = Visibility.Visible;
                Canvas.SetLeft(SelectionBox, _startPoint.X);
                Canvas.SetTop(SelectionBox, _startPoint.Y);
                SelectionBox.Width = 0;
                SelectionBox.Height = 0;
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPoint = e.GetPosition(DrawCanvas);
                
                double x = Math.Min(currentPoint.X, _startPoint.X);
                double y = Math.Min(currentPoint.Y, _startPoint.Y);
                double width = Math.Abs(currentPoint.X - _startPoint.X);
                double height = Math.Abs(currentPoint.Y - _startPoint.Y);

                Canvas.SetLeft(SelectionBox, x);
                Canvas.SetTop(SelectionBox, y);
                SelectionBox.Width = width;
                SelectionBox.Height = height;
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                
                double wpfX = Canvas.GetLeft(SelectionBox);
                double wpfY = Canvas.GetTop(SelectionBox);
                double wpfWidth = SelectionBox.Width;
                double wpfHeight = SelectionBox.Height;

                // 1. Tính toán tỷ lệ Scale của màn hình (DPI Scaling)
                PresentationSource source = PresentationSource.FromVisual(this);
                double dpiX = 1.0;
                double dpiY = 1.0;

                if (source != null && source.CompositionTarget != null)
                {
                    dpiX = source.CompositionTarget.TransformToDevice.M11;
                    dpiY = source.CompositionTarget.TransformToDevice.M22;
                }

                // 2. Chuyển đổi sang tọa độ pixel vật lý thực tế
                int physicalX = (int)(wpfX * dpiX);
                int physicalY = (int)(wpfY * dpiY);
                int physicalWidth = (int)(wpfWidth * dpiX);
                int physicalHeight = (int)(wpfHeight * dpiY);

                if (physicalWidth > 20 && physicalHeight > 20)
                {
                    if (WindowHandle != IntPtr.Zero)
                    {
                        // Stage 3A: Tính tọa độ tương đối so với client area của cửa sổ game.
                        // physicalX/Y hiện là tọa độ màn hình tuyệt đối. Trừ đi vị trí client
                        // area origin → được tọa độ tương đối cửa sổ.
                        var clientOrigin = new Capture.NativeMethods.POINT(0, 0);
                        Capture.NativeMethods.ClientToScreen(WindowHandle, ref clientOrigin);

                        int relX = physicalX - clientOrigin.X;
                        int relY = physicalY - clientOrigin.Y;

                        OnWindowRelativeRegionSelected?.Invoke(relX, relY, physicalWidth, physicalHeight);
                    }
                    else
                    {
                        // Chế độ cũ: tọa độ tuyệt đối màn hình
                        OnRegionSelected?.Invoke(physicalX, physicalY, physicalWidth, physicalHeight);
                    }
                }
                
                this.Close();
            }
        }

        // Bấm phím ESC để hủy quét vùng
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
            }
        }
    }
}