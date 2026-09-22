using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using TranslateBot.Capture;

namespace TranslateBot.UI
{
    // Dialog cho phép người dùng chọn cửa sổ game từ danh sách các cửa sổ
    // đang mở trên Windows. Sau khi chọn, callback OnWindowSelected được gọi
    // với WindowInfo tương ứng để MainWindow thiết lập window-relative capture.
    public partial class WindowSelector : Window
    {
        public Action<WindowInfo>? OnWindowSelected { get; set; }

        // Wrapper class cho ListView binding (thêm SizeDisplay property)
        private class WindowDisplayItem
        {
            public IntPtr Handle { get; init; }
            public string Title { get; init; } = string.Empty;
            public string ProcessName { get; init; } = string.Empty;
            public string SizeDisplay { get; init; } = string.Empty;
            public WindowInfo Source { get; init; } = null!;
        }

        public WindowSelector()
        {
            InitializeComponent();
            RefreshWindowList();
        }

        private void RefreshWindowList()
        {
            var windows = WindowEnumerator.GetVisibleWindows();
            var items = new List<WindowDisplayItem>();

            foreach (var w in windows)
            {
                items.Add(new WindowDisplayItem
                {
                    Handle = w.Handle,
                    Title = w.Title.Length > 60 ? w.Title[..57] + "..." : w.Title,
                    ProcessName = w.ProcessName,
                    SizeDisplay = $"{(int)w.Bounds.Width}×{(int)w.Bounds.Height}",
                    Source = w
                });
            }

            WindowListView.ItemsSource = items;
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshWindowList();
        }

        private void SelectBtn_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void WindowListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ConfirmSelection();
        }

        private void ConfirmSelection()
        {
            if (WindowListView.SelectedItem is WindowDisplayItem selected)
            {
                OnWindowSelected?.Invoke(selected.Source);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Vui lòng chọn một cửa sổ từ danh sách.",
                    "Chưa chọn", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
