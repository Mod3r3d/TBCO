using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Infrastructure;
using TranslateBot.Infrastructure.Security;

namespace TranslateBot.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class SecurityAndConfigTests
    {
        private string? _tempConfigFile;

        [TestInitialize]
        public void Setup()
        {
            _tempConfigFile = Path.Combine(Path.GetTempPath(), $"tbco_test_{Guid.NewGuid():N}.json");
            ConfigManager.CustomConfigPath = _tempConfigFile;
        }

        [TestCleanup]
        public void Cleanup()
        {
            ConfigManager.CustomConfigPath = null;
            if (!string.IsNullOrEmpty(_tempConfigFile) && File.Exists(_tempConfigFile))
            {
                try { File.Delete(_tempConfigFile); } catch { }
            }
        }

        [TestMethod]
        public void SecretVault_EncryptAndDecrypt_PreservesOriginalText()
        {
            string originalKey = "AIzaSyD-FakeSecretKeyForTestingPurpose123456";
            string encrypted = SecretVault.EncryptSecret(originalKey);

            Assert.AreNotEqual(originalKey, encrypted, "Chuỗi đã mã hóa phải khác chuỗi gốc");
            Assert.IsTrue(encrypted.StartsWith("dpapi:"), "Chuỗi mã hóa phải có tiền tố dpapi:");

            string decrypted = SecretVault.DecryptSecret(encrypted);
            Assert.AreEqual(originalKey, decrypted, "Chuỗi giải mã phải trùng khớp với chuỗi gốc");
        }

        [TestMethod]
        public void SecretVault_MaskSecret_HidesSensitivePortions()
        {
            string key = "AIzaSyD123456789SecretPayload4xQp";
            string masked = SecretVault.MaskSecret(key);

            Assert.IsTrue(masked.StartsWith("AIzaSy"), "Phải giữ lại 6 ký tự đầu nhận diện nhà cung cấp");
            Assert.IsTrue(masked.EndsWith("4xQp"), "Phải giữ lại 4 ký tự cuối kiểm tra key");
            Assert.IsTrue(masked.Contains("..."), "Phần giữa phải bị che giấu bằng dấu ...");
            Assert.IsFalse(masked.Contains("SecretPayload"), "Không được để lộ phần thân bí mật");
        }

        [TestMethod]
        public void SecretVault_Decrypt_PlaintextFallbackCompatibility()
        {
            // Nếu người dùng nhập thẳng plaintext hoặc cấu hình cũ chưa mã hóa
            string plain = "AIzaSyPlaintextKey";
            string decrypted = SecretVault.DecryptSecret(plain);

            Assert.AreEqual(plain, decrypted, "Chuỗi chưa có tiền tố dpapi: phải được trả về nguyên vẹn");
        }

        [TestMethod]
        public void ConfigManager_Migrate_PlaintextApiKeyToEncrypted()
        {
            var config = new AppConfig
            {
                ApiKey = "AIzaSyOldPlaintextKey",
                EncryptedApiKey = null
            };

            ConfigManager.Migrate(config);

            Assert.IsNull(config.ApiKey, "Sau khi migrate, ApiKey plaintext phải được xóa sạch để bảo mật");
            Assert.IsNotNull(config.EncryptedApiKey, "EncryptedApiKey phải được sinh ra");
            Assert.IsTrue(config.EncryptedApiKey.StartsWith("dpapi:"));

            string restored = ConfigManager.GetEffectiveApiKey(config);
            Assert.AreEqual("AIzaSyOldPlaintextKey", restored, "Key giải mã phải bằng đúng key ban đầu");
        }

        [TestMethod]
        public void ConfigManager_GetAndSetEffectiveApiKey_WorksSeamlessly()
        {
            var config = new AppConfig();
            ConfigManager.SetEffectiveApiKey(config, "MySuperSecretNewKey");

            Assert.IsNull(config.ApiKey);
            Assert.IsNotNull(config.EncryptedApiKey);

            string retrieved = ConfigManager.GetEffectiveApiKey(config);
            Assert.AreEqual("MySuperSecretNewKey", retrieved);
        }

        [TestMethod]
        public void ConfigManager_SaveAndLoadCredentials_PoolRoundtrip()
        {
            var config = new AppConfig();
            var creds = new List<Translation.ApiCredential>
            {
                new Translation.ApiCredential
                {
                    Id = "gemini-01",
                    Provider = "Gemini",
                    Secret = "AIzaSySecretKeyOne12345",
                    Status = Translation.ApiKeyStatus.Active
                },
                new Translation.ApiCredential
                {
                    Id = "gemini-02",
                    Provider = "Gemini",
                    Secret = "AIzaSySecretKeyTwo67890",
                    Status = Translation.ApiKeyStatus.Disabled
                }
            };

            ConfigManager.SaveCredentials(config, creds);

            var loadedConfig = ConfigManager.Load();
            var loadedCreds = ConfigManager.LoadCredentials(loadedConfig);

            Assert.AreEqual(2, loadedCreds.Count);
            Assert.AreEqual("AIzaSySecretKeyOne12345", loadedCreds[0].Secret);
            Assert.AreEqual(Translation.ApiKeyStatus.Active, loadedCreds[0].Status);
            Assert.AreEqual("AIzaSySecretKeyTwo67890", loadedCreds[1].Secret);
            Assert.AreEqual(Translation.ApiKeyStatus.Disabled, loadedCreds[1].Status);
        }
    }
}
