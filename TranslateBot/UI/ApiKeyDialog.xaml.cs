using System;
using System.Diagnostics;
using System.Windows;

namespace TranslateBot.UI
{
    public partial class ApiKeyDialog : Window
    {
        public string EnteredApiKey => ApiKeyTextBox.Text.Trim();

        public ApiKeyDialog(string currentKey = "")
        {
            InitializeComponent();
            ApiKeyTextBox.Text = currentKey;
            ApiKeyTextBox.Focus();
            if (!string.IsNullOrEmpty(currentKey))
            {
                ApiKeyTextBox.SelectAll();
            }
        }

        private void PasteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                ApiKeyTextBox.Text = Clipboard.GetText().Trim();
            }
        }

        private void GetFreeKeyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://aistudio.google.com/app/apikey",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể mở trình duyệt: {ex.Message}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            string key = EnteredApiKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("Vui lòng nhập API Key hợp lệ!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
