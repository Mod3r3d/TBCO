using System;
using System.Security.Cryptography;
using System.Text;

namespace TranslateBot.Infrastructure.Security
{
    /// <summary>
    /// Quản lý mã hóa và bảo mật các chuỗi nhạy cảm (API Key, Secret) qua Windows DPAPI (Phase 28 — Security).
    /// </summary>
    public static class SecretVault
    {
        private const string Prefix = "dpapi:";

        /// <summary>
        /// Mã hóa chuỗi nhạy cảm bằng DPAPI (gắn với tài khoản người dùng Windows hiện tại).
        /// </summary>
        public static string EncryptSecret(string plainSecret)
        {
            if (string.IsNullOrEmpty(plainSecret)) return string.Empty;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainSecret);
                byte[] cipherBytes = ProtectedData.Protect(
                    plainBytes,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);

                return Prefix + Convert.ToBase64String(cipherBytes);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[SECURITY] Lỗi mã hóa DPAPI: {ex.Message}");
                return plainSecret; // Fallback nếu môi trường không hỗ trợ DPAPI
            }
        }

        /// <summary>
        /// Giải mã chuỗi nhạy cảm từ DPAPI. Tự động nhận diện chuỗi có tiền tố dpapi: hoặc chuỗi chưa mã hóa.
        /// </summary>
        public static string DecryptSecret(string? cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return string.Empty;

            if (!cipherText.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Chuỗi plaintext chưa mã hóa (tương thích cấu hình cũ)
                return cipherText;
            }

            try
            {
                string rawBase64 = cipherText[Prefix.Length..];
                byte[] cipherBytes = Convert.FromBase64String(rawBase64);
                byte[] plainBytes = ProtectedData.Unprotect(
                    cipherBytes,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[SECURITY] Lỗi giải mã DPAPI: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Che giấu API key khi hiển thị trên log hoặc chẩn đoán, không để lộ bí mật.
        /// </summary>
        public static string MaskSecret(string? secret)
        {
            if (string.IsNullOrEmpty(secret)) return "[TRỐNG]";

            string s = secret.Trim();
            if (s.Length <= 8) return "******";

            // Hiển thị 6 ký tự đầu và 4 ký tự cuối (ví dụ AIzaSy...4xQp)
            return $"{s[..Math.Min(6, s.Length)]}...{s[^Math.Min(4, s.Length)..]}";
        }
    }
}
