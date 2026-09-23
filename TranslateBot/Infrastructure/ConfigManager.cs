using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TranslateBot.Hotkeys;
using TranslateBot.Infrastructure.Security;
using TranslateBot.OCR;

namespace TranslateBot.Infrastructure
{
    // Lớp chứa dữ liệu cấu hình — mở rộng dần theo từng Stage.
    public class AppConfig
    {
        // ── Stage 1: Screen-absolute capture region ────────────────────────
        public int CaptureX { get; set; }
        public int CaptureY { get; set; }
        public int CaptureWidth { get; set; }
        public int CaptureHeight { get; set; }

        // ── Stage 3A: Window-relative capture ──────────────────────────────
        public bool UseWindowRelativeCapture { get; set; }
        public string? TargetWindowTitle { get; set; }
        public string? TargetProcessName { get; set; }
        public int RelativeCaptureX { get; set; }
        public int RelativeCaptureY { get; set; }
        public int RelativeCaptureWidth { get; set; }
        public int RelativeCaptureHeight { get; set; }

        // ── Stage 3B: OCR Area Manager (Section 16) ────────────────────────
        // Null hoặc rỗng = chưa thiết lập → engine dùng single-region fallback
        public List<OcrArea>? OcrAreas { get; set; }

        // ── Stage 3B: Exclusion Areas (Section 17) ─────────────────────────
        public List<ExclusionArea>? ExclusionAreas { get; set; }

        // ── Stage 3B: Floating Toolbar (Section 19) ────────────────────────
        public double ToolbarX { get; set; } = 100;
        public double ToolbarY { get; set; } = 100;
        public bool ShowToolbar { get; set; }

        // ── Stage 6: Overlay & UX (Section 20, 21, 36) ─────────────────────
        public bool OverlayEnabled { get; set; } = false;
        public string OverlayMode { get; set; } = "Overlay"; // Overlay, Layer, Windowed
        public string OverlayTheme { get; set; } = "FgoChaldea"; // Dark, Light, FgoChaldea, HighContrast
        public string DisplayMode { get; set; } = "Both"; // TranslationOnly, Both, OriginalAbove, Debug
        public string OverlayPosition { get; set; } = "Bottom"; // Bottom, Top, Custom
        public double OverlayX { get; set; } = 250;
        public double OverlayY { get; set; } = 650;
        public double OverlayWidth { get; set; } = 750;
        public double OverlayHeight { get; set; } = 150;
        public bool OverlayLocked { get; set; } = false;
        public double OverlayFontSize { get; set; } = 20;
        public double OverlayOpacity { get; set; } = 0.88;
        public int MaxHistoryEntries { get; set; } = 100;

        // ── Stage 7: Advanced Features (TTS, Session Log) (Section 37, 38) ──
        public bool TtsEnabled { get; set; } = false;
        public string TtsMode { get; set; } = "AutoReadTranslated"; // Off, AutoReadTranslated, AutoReadConfirmed, Manual
        public string? TtsVoice { get; set; }
        public int TtsRate { get; set; } = 0; // -10..10
        public int TtsVolume { get; set; } = 85; // 0..100
        public bool AutoSaveSession { get; set; } = true;

        // ── API Key (Lưu cấu hình hoặc fallback khi không dùng .env) ──────
        public string? ApiKey { get; set; }

        // ── Phase 28: Security & Windows DPAPI ─────────────────────────────
        public string? EncryptedApiKey { get; set; }

        // ── Phase 8: API Key Pool Multi-Key Credentials ────────────────────
        public List<ApiCredentialConfig>? ApiCredentials { get; set; }

        // ── Phase 1: Centralized Hotkey Bindings ───────────────────────────
        public List<HotkeyBinding>? CustomHotkeys { get; set; }

        // ── AutoHost UI & System Settings ──────────────────────────────────
        public bool AlwaysOnTop { get; set; } = false;
        public bool AutoCopyToClipboard { get; set; } = false;
        public string MainTheme { get; set; } = "AutoHostDark"; // AutoHostDark, FgoChaldea, HighContrast, Light
        public int ScanIntervalMs { get; set; } = 300;
        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "vi";
        public string AiModel { get; set; } = "gemini-3.5-flash";

        // ── Translation Provider Selection ─────────────────────────────────
        // "Gemini" (Google Gemini AI), "DeepL" (DeepL API), "GoogleWeb" (Google Web Translate miễn phí)
        public string TranslationProvider { get; set; } = "Gemini";
        public string? DeepLApiKey { get; set; }
        public string? EncryptedDeepLApiKey { get; set; }

        // ── OCR Engine Selection ────────────────────────────────────────────
        // "WindowsOcr" (mặc định, ổn định) hoặc "OneOcr" (thử nghiệm, chính xác hơn)
        public string OcrEngineType { get; set; } = "WindowsOcr";

        // ── Detached Floating Subtitle Window ──────────────────────────────
        public bool IsSubtitleDetached { get; set; } = false;
        public double SubtitleWindowX { get; set; } = 250;
        public double SubtitleWindowY { get; set; } = 650;
        public double SubtitleWindowWidth { get; set; } = 680;
        public double SubtitleWindowHeight { get; set; } = 160;
        public double SubtitleFontSize { get; set; } = 16;
        public bool SubtitleTopmost { get; set; } = true;
        public double SubtitleOpacity { get; set; } = 0.92;
    }

    public class ApiCredentialConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Provider { get; set; } = "Gemini";
        public string? EncryptedSecret { get; set; }
        public string Status { get; set; } = "Active";
        public DateTime? CooldownUntil { get; set; }
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
        public double AverageLatencyMs { get; set; }
    }

    // Lớp quản lý Đọc/Ghi file config.json
    public static class ConfigManager
    {
        private static readonly string ConfigPath = "config.json";

        public static AppConfig Load()
        {
            AppConfig config = new AppConfig();
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[LỖI CONFIG] Không thể đọc file: {ex.Message}");
                }
            }

            // Tự động chuyển đổi cấu hình cũ sang cấu hình bảo mật mới
            Migrate(config);
            return config;
        }

        public static void Migrate(AppConfig config)
        {
            bool modified = false;

            // Nếu có ApiKey dạng plaintext từ Step 7 mà chưa có EncryptedApiKey
            if (!string.IsNullOrEmpty(config.ApiKey) && string.IsNullOrEmpty(config.EncryptedApiKey))
            {
                config.EncryptedApiKey = SecretVault.EncryptSecret(config.ApiKey);
                config.ApiKey = null;
                modified = true;
                AppLogger.Info("[CONFIG_MIGRATION] Đã tự động mã hóa API Key sang Windows DPAPI.");
            }

            // Phase 8: Migrate key đơn sang ApiCredentials nếu danh sách chưa tồn tại
            if (config.ApiCredentials == null || config.ApiCredentials.Count == 0)
            {
                string? existingSecret = config.EncryptedApiKey;
                if (!string.IsNullOrEmpty(existingSecret))
                {
                    config.ApiCredentials = new List<ApiCredentialConfig>
                    {
                        new ApiCredentialConfig
                        {
                            Id = "gemini-primary",
                            Provider = "Gemini",
                            EncryptedSecret = existingSecret,
                            Status = "Active"
                        }
                    };
                    modified = true;
                    AppLogger.Info("[CONFIG_MIGRATION] Đã khởi tạo ApiCredentials pool từ API Key hiện có.");
                }
            }

            // Nếu có DeepLApiKey dạng plaintext mà chưa mã hóa DPAPI
            if (!string.IsNullOrEmpty(config.DeepLApiKey) && string.IsNullOrEmpty(config.EncryptedDeepLApiKey))
            {
                config.EncryptedDeepLApiKey = SecretVault.EncryptSecret(config.DeepLApiKey);
                config.DeepLApiKey = null;
                modified = true;
                AppLogger.Info("[CONFIG_MIGRATION] Đã tự động mã hóa DeepL API Key sang Windows DPAPI.");
            }

            if (modified)
            {
                Save(config);
            }
        }

        public static string GetEffectiveDeepLApiKey(AppConfig config)
        {
            if (!string.IsNullOrEmpty(config.EncryptedDeepLApiKey))
            {
                return SecretVault.DecryptSecret(config.EncryptedDeepLApiKey);
            }
            return config.DeepLApiKey ?? string.Empty;
        }

        public static void SetEffectiveDeepLApiKey(AppConfig config, string rawKey)
        {
            config.EncryptedDeepLApiKey = string.IsNullOrWhiteSpace(rawKey) ? null : SecretVault.EncryptSecret(rawKey.Trim());
            config.DeepLApiKey = null;
            Save(config);
        }

        public static string GetEffectiveApiKey(AppConfig config)
        {
            if (!string.IsNullOrEmpty(config.EncryptedApiKey))
            {
                return SecretVault.DecryptSecret(config.EncryptedApiKey);
            }
            if (config.ApiCredentials != null && config.ApiCredentials.Count > 0)
            {
                var first = config.ApiCredentials[0];
                return SecretVault.DecryptSecret(first.EncryptedSecret);
            }
            return config.ApiKey ?? string.Empty;
        }

        public static void SetEffectiveApiKey(AppConfig config, string rawKey)
        {
            config.EncryptedApiKey = SecretVault.EncryptSecret(rawKey);
            config.ApiKey = null;

            if (config.ApiCredentials == null) config.ApiCredentials = new List<ApiCredentialConfig>();
            var primary = config.ApiCredentials.FirstOrDefault(c => c.Id == "gemini-primary");
            if (primary != null)
            {
                primary.EncryptedSecret = config.EncryptedApiKey;
            }
            else
            {
                config.ApiCredentials.Insert(0, new ApiCredentialConfig
                {
                    Id = "gemini-primary",
                    Provider = "Gemini",
                    EncryptedSecret = config.EncryptedApiKey,
                    Status = "Active"
                });
            }

            Save(config);
        }

        public static List<Translation.ApiCredential> LoadCredentials(AppConfig config)
        {
            var list = new List<Translation.ApiCredential>();
            if (config.ApiCredentials == null) return list;

            foreach (var cfg in config.ApiCredentials)
            {
                string plainSecret = SecretVault.DecryptSecret(cfg.EncryptedSecret);
                if (string.IsNullOrEmpty(plainSecret)) continue;

                var cred = new Translation.ApiCredential
                {
                    Id = cfg.Id,
                    Provider = cfg.Provider,
                    Secret = plainSecret,
                    CooldownUntil = cfg.CooldownUntil,
                    SuccessCount = cfg.SuccessCount,
                    FailureCount = cfg.FailureCount,
                    AverageLatencyMs = cfg.AverageLatencyMs
                };

                if (Enum.TryParse<Translation.ApiKeyStatus>(cfg.Status, out var status))
                {
                    cred.Status = status;
                }
                list.Add(cred);
            }
            return list;
        }

        public static void SaveCredentials(AppConfig config, IEnumerable<Translation.ApiCredential> credentials)
        {
            config.ApiCredentials = credentials.Select(c => new ApiCredentialConfig
            {
                Id = c.Id,
                Provider = c.Provider,
                EncryptedSecret = SecretVault.EncryptSecret(c.Secret),
                Status = c.Status.ToString(),
                CooldownUntil = c.CooldownUntil,
                SuccessCount = c.SuccessCount,
                FailureCount = c.FailureCount,
                AverageLatencyMs = c.AverageLatencyMs
            }).ToList();

            if (config.ApiCredentials.Count > 0)
            {
                config.EncryptedApiKey = config.ApiCredentials[0].EncryptedSecret;
            }

            Save(config);
        }

        public static void Save(AppConfig config)
        {
            try
            {
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[LỖI CONFIG] Không thể lưu file: {ex.Message}");
            }
        }
    }
}