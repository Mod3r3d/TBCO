using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using TranslateBot.Infrastructure;
using TranslateBot.Infrastructure.Security;
using TranslateBot.Translation;

namespace TranslateBot.UI
{
    public class CredentialViewModel
    {
        public ApiCredential Model { get; }

        public string Id => Model.Id;
        public string Provider => Model.Provider;
        public string MaskedSecret => SecretVault.MaskSecret(Model.Secret);

        public string StatusText
        {
            get
            {
                if (Model.Status == ApiKeyStatus.Cooldown && Model.CooldownUntil.HasValue)
                {
                    double remainSec = (Model.CooldownUntil.Value - DateTime.UtcNow).TotalSeconds;
                    if (remainSec > 0)
                        return $"Cooldown ({remainSec:F0}s)";
                }
                return Model.Status.ToString();
            }
        }

        public string StatusColor
        {
            get
            {
                return Model.Status switch
                {
                    ApiKeyStatus.Active => "#10B981",    // Xanh lá
                    ApiKeyStatus.Cooldown => "#F59E0B",  // Vàng cam
                    ApiKeyStatus.Invalid => "#EF4444",   // Đỏ
                    _ => "#6B7280"                       // Xám
                };
            }
        }

        public long SuccessCount => Model.SuccessCount;
        public long FailureCount => Model.FailureCount;

        public CredentialViewModel(ApiCredential model)
        {
            Model = model;
        }
    }

    public partial class ApiKeyManagerWindow : Window
    {
        private readonly ApiKeyPool _keyPool;
        private readonly AppConfig _config;
        private readonly GeminiProvider _testerProvider;
        private readonly ObservableCollection<CredentialViewModel> _viewModels = new();

        public ApiKeyManagerWindow(ApiKeyPool keyPool, AppConfig config)
        {
            InitializeComponent();
            _keyPool = keyPool;
            _config = config;
            _testerProvider = new GeminiProvider { ThrowOnApiError = true };

            CredentialsListView.ItemsSource = _viewModels;
            RefreshList();
        }

        private void RefreshList()
        {
            _viewModels.Clear();
            var allCreds = _keyPool.GetAllCredentials();
            foreach (var cred in allCreds)
            {
                _viewModels.Add(new CredentialViewModel(cred));
            }

            TotalKeysText.Text = $"{allCreds.Count} key";
            ActiveKeysText.Text = $"{allCreds.Count(c => c.IsAvailable)} key";
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
                MessageBox.Show($"Không thể mở trình duyệt: {ex.Message}");
            }
        }

        private void AddKeyBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ApiKeyDialog("")
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                string key = dialog.EnteredApiKey;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    int index = _keyPool.GetAllCredentials().Count + 1;
                    var cred = new ApiCredential
                    {
                        Id = $"gemini-{index:D2}",
                        Provider = "Gemini",
                        Secret = key,
                        Status = ApiKeyStatus.Active
                    };
                    _keyPool.AddOrUpdateCredential(cred);
                    RefreshList();
                    StatusMessageText.Text = $"Đã thêm key [{cred.Id}]";
                }
            }
        }

        private void BatchPasteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText())
            {
                MessageBox.Show("Clipboard không chứa văn bản!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string text = Clipboard.GetText();
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int added = 0;

            foreach (var line in lines)
            {
                string cleanKey = line.Trim();
                if (cleanKey.Length > 20)
                {
                    int index = _keyPool.GetAllCredentials().Count + 1;
                    var cred = new ApiCredential
                    {
                        Id = $"gemini-{index:D2}",
                        Provider = "Gemini",
                        Secret = cleanKey,
                        Status = ApiKeyStatus.Active
                    };
                    _keyPool.AddOrUpdateCredential(cred);
                    added++;
                }
            }

            RefreshList();
            StatusMessageText.Text = $"Đã dán và thêm thành công {added} API Key mới.";
        }

        private async void TestKeyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CredentialsListView.SelectedItem is not CredentialViewModel selected)
            {
                MessageBox.Show("Vui lòng chọn 1 key trong danh sách để kiểm tra.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StatusMessageText.Text = $"Đang gửi yêu cầu test đến Google với [{selected.Id}]...";
            try
            {
                string result = await _testerProvider.TranslateWithKeyAsync("Hello, welcome to Chaldea!", selected.Model.Secret);
                if (!string.IsNullOrEmpty(result))
                {
                    selected.Model.RecordSuccess(TimeSpan.FromMilliseconds(200));
                    RefreshList();
                    MessageBox.Show($"Key [{selected.Id}] HOẠT ĐỘNG TỐT!\nKết quả dịch thử: {result}", "Kiểm tra thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Key [{selected.Id}] trả về kết quả rỗng.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (GeminiApiException ex)
            {
                if (ex.StatusCode == 429)
                {
                    selected.Model.RecordCooldown(TimeSpan.FromSeconds(60), "Test 429 Quota Exceeded");
                    RefreshList();
                    MessageBox.Show($"Key [{selected.Id}] báo HẾT QUOTA (HTTP 429). Key đã chuyển sang Cooldown.", "Lỗi 429", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (ex.StatusCode == 401)
                {
                    selected.Model.RecordInvalid("Test 401 Invalid Token");
                    RefreshList();
                    MessageBox.Show($"Key [{selected.Id}] KHÔNG HỢP LỆ (HTTP 401). Vui lòng kiểm tra lại mã secret.", "Lỗi 401", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Lỗi kiểm tra key: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi kết nối: {ex.Message}", "Lỗi mạng", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StatusMessageText.Text = "Sẵn sàng";
            }
        }

        private void ToggleEnableBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CredentialsListView.SelectedItem is not CredentialViewModel selected) return;

            if (selected.Model.Status == ApiKeyStatus.Disabled)
            {
                selected.Model.Status = ApiKeyStatus.Active;
            }
            else
            {
                selected.Model.Status = ApiKeyStatus.Disabled;
            }
            RefreshList();
        }

        private void RemoveKeyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CredentialsListView.SelectedItem is not CredentialViewModel selected) return;

            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa key [{selected.Id}]?",
                "Xác nhận xóa",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                _keyPool.RemoveCredential(selected.Id);
                RefreshList();
                StatusMessageText.Text = $"Đã xóa key [{selected.Id}]";
            }
        }

        private void SaveAndApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            var creds = _keyPool.GetAllCredentials();
            ConfigManager.SaveCredentials(_config, creds);
            StatusMessageText.Text = "Đã lưu và áp dụng cấu hình Pool.";
            DialogResult = true;
            Close();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
