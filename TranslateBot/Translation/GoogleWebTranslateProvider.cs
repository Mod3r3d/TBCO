using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TranslateBot.Infrastructure;

namespace TranslateBot.Translation
{
    /// <summary>
    /// Bộ dịch dự phòng Google Web Translate (không cần API Key, không giới hạn Quota).
    /// Kế thừa kỹ thuật đa tầng từ MORT (GoogleBasicTranslateAPI) và Translate-Bot (fast_translate_fallback).
    /// </summary>
    public class GoogleWebTranslateProvider : ITranslationProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        static GoogleWebTranslateProvider()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        }

        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "vi";

        public Task<string> TranslateAsync(string text)
        {
            return TranslateAsync(text, null);
        }

        public async Task<string> TranslateAsync(string text, TranslationContext? context)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string src = CleanLangCode(SourceLanguage);
            string tgt = CleanLangCode(TargetLanguage);

            // 1. Thử Endpoint chính: Chrome Dictionary Extension (hoạt động cực kỳ ổn định, không bị bot block)
            try
            {
                string result = await TranslateViaChromeDictAsync(text, src, tgt);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[GOOGLE_WEB] Dict-Chrome endpoint gặp sự cố ({ex.Message}), thử endpoint dự phòng...");
            }

            // 2. Thử Endpoint phụ: Google Translate Single GTX
            try
            {
                string result = await TranslateViaGtxAsync(text, src, tgt);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[GOOGLE_WEB_FAILED] Cả 2 endpoint Google Web đều thất bại: {ex.Message}");
            }

            return string.Empty;
        }

        private async Task<string> TranslateViaGtxAsync(string text, string src, string tgt)
        {
            string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={src}&tl={tgt}&dt=t&q={Uri.EscapeDataString(text)}";
            string json = await _httpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var sentences = root[0];
                if (sentences.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var item in sentences.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0)
                        {
                            string? part = item[0].GetString();
                            if (!string.IsNullOrEmpty(part))
                            {
                                sb.Append(part);
                            }
                        }
                    }
                    return sb.ToString().Trim();
                }
            }

            return string.Empty;
        }

        private async Task<string> TranslateViaChromeDictAsync(string text, string src, string tgt)
        {
            string url = $"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl={src}&tl={tgt}&q={Uri.EscapeDataString(text)}";
            string json = await _httpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var el in root.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        if (sb.Length > 0) sb.Append(" ");
                        sb.Append(el.GetString());
                    }
                }
                return sb.ToString().Trim();
            }
            else if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string CleanLangCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return "auto";

            // "en-US" -> "en", "vi-VN" -> "vi", "ja-JP" -> "ja"
            int hyphenIndex = code.IndexOf('-');
            if (hyphenIndex > 0)
            {
                return code.Substring(0, hyphenIndex).ToLowerInvariant();
            }
            return code.Trim().ToLowerInvariant();
        }
    }
}
