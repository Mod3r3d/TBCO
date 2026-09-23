using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TranslateBot.Infrastructure;

namespace TranslateBot.Translation.Providers
{
    /// <summary>
    /// Nhà cung cấp dịch thuật DeepL API (Section 13 của Master Plan)
    /// Hỗ trợ cả Free API (key đuôi ':fx') và Pro API.
    /// </summary>
    public class DeepLProvider : ITranslationProvider
    {
        private readonly HttpClient _httpClient;
        private string _apiKey;

        public string ApiKey
        {
            get => _apiKey;
            set => _apiKey = value;
        }

        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "VI"; // DeepL yêu cầu mã ngôn ngữ viết hoa (VI, EN, JA)

        public bool IsFreePlan => !string.IsNullOrEmpty(_apiKey) && _apiKey.TrimEnd().EndsWith(":fx", StringComparison.OrdinalIgnoreCase);

        public string EndpointUrl => IsFreePlan
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";

        public DeepLProvider(string apiKey = "", HttpClient? httpClient = null)
        {
            _apiKey = apiKey;
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        }

        public Task<string> TranslateAsync(string text)
            => TranslateAsync(text, null);

        public async Task<string> TranslateAsync(string text, TranslationContext? context)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                AppLogger.Warn("[DEEPL] Chưa cấu hình DeepL Auth Key");
                return string.Empty;
            }

            try
            {
                var formValues = new List<KeyValuePair<string, string>>
                {
                    new("text", text),
                    new("target_lang", NormalizeTargetLanguage(TargetLanguage))
                };

                string sourceLang = NormalizeSourceLanguage(SourceLanguage);
                if (!string.IsNullOrEmpty(sourceLang) && !sourceLang.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    formValues.Add(new("source_lang", sourceLang));
                }

                // DeepL v2 hỗ trợ tham số context để tinh chỉnh dịch theo ngữ cảnh
                if (context != null)
                {
                    string contextText = BuildContextHint(context);
                    if (!string.IsNullOrEmpty(contextText))
                    {
                        formValues.Add(new("context", contextText));
                    }
                }

                using var requestContent = new FormUrlEncodedContent(formValues);
                string responseJson = await SendHttpRequestAsync(EndpointUrl, requestContent, _apiKey);

                if (string.IsNullOrWhiteSpace(responseJson)) return string.Empty;

                return ParseDeepLResponse(responseJson);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[DEEPL_ERROR] Lỗi gọi DeepL API: {ex.Message}");
                return string.Empty;
            }
        }

        // Cho phép override để mock trong unit tests
        public virtual async Task<string> SendHttpRequestAsync(string endpoint, HttpContent content, string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("Authorization", $"DeepL-Auth-Key {apiKey.Trim()}");
            request.Content = content;

            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public static string ParseDeepLResponse(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                var translations = obj["translations"] as JArray;
                if (translations != null && translations.Count > 0)
                {
                    return translations[0]?["text"]?.ToString() ?? string.Empty;
                }
            }
            catch
            {
                // Parse error
            }
            return string.Empty;
        }

        private static string NormalizeTargetLanguage(string? lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "VI";
            string l = lang.Trim().ToUpperInvariant();
            if (l == "JA-JP" || l == "JA") return "JA";
            if (l == "EN-US" || l == "EN-GB" || l == "EN") return "EN-US";
            if (l == "VI-VN" || l == "VI") return "VI";
            return l;
        }

        private static string NormalizeSourceLanguage(string? lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "auto";
            string l = lang.Trim().ToUpperInvariant();
            if (l == "JA-JP" || l == "JA") return "JA";
            if (l == "EN-US" || l == "EN-GB" || l == "EN") return "EN";
            if (l == "VI-VN" || l == "VI") return "VI";
            return l;
        }

        private static string BuildContextHint(TranslationContext context)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(context.GameName))
            {
                sb.Append($"Game: {context.GameName}. ");
            }
            if (!string.IsNullOrEmpty(context.SpeakerName))
            {
                sb.Append($"Speaker: {context.SpeakerName}. ");
            }
            if (context.PreviousLines.Count > 0)
            {
                var last = context.PreviousLines[^1];
                sb.Append($"Previous: {last.Original} -> {last.Translated}.");
            }
            return sb.ToString();
        }
    }
}
