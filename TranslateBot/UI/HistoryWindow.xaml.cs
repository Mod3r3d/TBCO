using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TranslateBot.Dialogue;

namespace TranslateBot.UI
{
    public class HistoryDisplayItem
    {
        public int SequenceId { get; set; }
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string? Speaker { get; set; }
        public string FormattedTime { get; set; } = string.Empty;
        public Visibility SpeakerVisibility => string.IsNullOrWhiteSpace(Speaker) ? Visibility.Collapsed : Visibility.Visible;
    }

    public partial class HistoryWindow : Window
    {
        private readonly DialogueHistory _history;
        private readonly ObservableCollection<HistoryDisplayItem> _displayList = new();
        private string _filterQuery = string.Empty;

        public HistoryWindow(DialogueHistory history)
        {
            InitializeComponent();
            _history = history ?? throw new ArgumentNullException(nameof(history));

            HistoryItemsControl.ItemsSource = _displayList;
            RefreshDisplay();

            _history.OnNewEntry += OnNewEntryReceived;
            _history.OnCleared += OnHistoryCleared;
        }

        protected override void OnClosed(EventArgs e)
        {
            _history.OnNewEntry -= OnNewEntryReceived;
            _history.OnCleared -= OnHistoryCleared;
            base.OnClosed(e);
        }

        private void OnNewEntryReceived(DialogueEntry entry)
        {
            Dispatcher.Invoke(() =>
            {
                if (MatchesFilter(entry, _filterQuery))
                {
                    _displayList.Add(MapToItem(entry));
                    if (AutoScrollCheck.IsChecked == true)
                    {
                        HistoryScrollViewer.ScrollToEnd();
                    }
                }
                UpdateStatusText();
            });
        }

        private void OnHistoryCleared()
        {
            Dispatcher.Invoke(() =>
            {
                _displayList.Clear();
                UpdateStatusText();
            });
        }

        private void RefreshDisplay()
        {
            _displayList.Clear();
            var filtered = _history.Entries.Where(e => MatchesFilter(e, _filterQuery));
            foreach (var entry in filtered)
            {
                _displayList.Add(MapToItem(entry));
            }

            UpdateStatusText();
            if (AutoScrollCheck.IsChecked == true)
            {
                HistoryScrollViewer.ScrollToEnd();
            }
        }

        private static HistoryDisplayItem MapToItem(DialogueEntry e)
        {
            return new HistoryDisplayItem
            {
                SequenceId = e.SequenceId,
                OriginalText = e.OriginalText,
                TranslatedText = e.TranslatedText,
                Speaker = e.Speaker,
                FormattedTime = e.FormattedTime
            };
        }

        private static bool MatchesFilter(DialogueEntry entry, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            return (entry.OriginalText?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                || (entry.TranslatedText?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                || (entry.Speaker?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filterQuery = SearchBox.Text.Trim();
            RefreshDisplay();
        }

        private void CopyAllBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = _history.ExportToText();
                Clipboard.SetText(text);
                MessageBox.Show($"Đã sao chép {_history.Count} câu thoại vào bộ nhớ tạm!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi sao chép: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyItemBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is HistoryDisplayItem item)
            {
                string spk = string.IsNullOrWhiteSpace(item.Speaker) ? "" : $"[{item.Speaker}] ";
                string text = $"{spk}{item.TranslatedText} (Gốc: {item.OriginalText})";
                Clipboard.SetText(text);
            }
        }

        public Action<string>? OnRequestSpeak { get; set; }

        private void SpeakItemBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is HistoryDisplayItem item)
            {
                OnRequestSpeak?.Invoke(item.TranslatedText);
            }
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Xuất Lịch Sử Hội Thoại",
                    Filter = "CSV Bảng Tính (*.csv)|*.csv|Văn Bản Text (*.txt)|*.txt|Dữ Liệu JSON (*.json)|*.json",
                    FileName = $"FGO_Dialogue_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (dialog.ShowDialog(this) == true)
                {
                    string content;
                    if (dialog.FilterIndex == 1) // CSV
                    {
                        var sessionMgr = new Session.SessionManager();
                        foreach (var ent in _history.Entries)
                        {
                            sessionMgr.AddRecord(ent.SequenceId, ent.OriginalText, ent.TranslatedText, ent.Speaker, "Gemini", ent.LatencyMs, false);
                        }
                        content = sessionMgr.ToCsvString();
                    }
                    else if (dialog.FilterIndex == 2) // TXT
                    {
                        content = _history.ExportToText();
                    }
                    else // JSON
                    {
                        content = _history.ExportToJson();
                    }

                    File.WriteAllText(dialog.FileName, content, System.Text.Encoding.UTF8);
                    MessageBox.Show($"Đã xuất file thành công tại:\n{dialog.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xuất file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ lịch sử hội thoại hiện tại?", 
                "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                _history.Clear();
            }
        }

        private void UpdateStatusText()
        {
            int total = _history.Count;
            int shown = _displayList.Count;
            StatusCountText.Text = string.IsNullOrWhiteSpace(_filterQuery)
                ? $"Tổng cộng: {total} câu thoại"
                : $"Hiển thị: {shown}/{total} câu thoại (theo bộ lọc)";
        }
    }
}
