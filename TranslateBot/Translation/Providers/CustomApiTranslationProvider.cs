using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TranslateBot.Infrastructure;

namespace TranslateBot.Translation.Providers
{
    /// <summary>
    /// Nhà cung cấp dịch thuật qua Custom API / OpenAI-Compatible Endpoint (Section 13 của Master Plan)
    /// Hỗ trợ: LM Studio, OpenRouter, Groq, vLLM, LocalAI, v.v.
    /// </summary>
    public class CustomApiTranslationProvider : ITranslationProvider
    {
        private readonly HttpClient _httpClient;

        public string EndpointUrl { get; set; } = "http://localhost:1234/v1/chat/completions";
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = "qwen2.5-72b-instruct";
        public double Temperature { get; set; } = 0.15;
        public string TargetLanguage { get; set; } = "tiếng Việt";

        public CustomApiTranslationProvider(
            string endpointUrl = "http://localhost:1234/v1/chat/completions",
            string apiKey = "",
            string modelName = "qwen2.5-72b-instruct",
            HttpClient? httpClient = null)
        {
            EndpointUrl = endpointUrl;
            ApiKey = apiKey;
            ModelName = modelName;
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        }

        public Task<string> TranslateAsync(string text)
            => TranslateAsync(text, null);

        public async Task<string> TranslateAsync(string text, TranslationContext? context)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            try
            {
                string systemInstruction = BuildSystemInstruction(TargetLanguage);
                string userPrompt = BuildUserPrompt(text, context, TargetLanguage);

                var payload = new
                {
                    model = ModelName,
                    temperature = Temperature,
                    messages = new[]
                    {
                        new { role = "system", content = systemInstruction },
                        new { role = "user", content = userPrompt }
                    }
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                string responseJson = await SendHttpRequestAsync(EndpointUrl, jsonPayload, ApiKey);

                if (string.IsNullOrWhiteSpace(responseJson)) return string.Empty;

                return ParseOpenAiResponse(responseJson);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[CUSTOM_API_ERROR] Lỗi gọi endpoint '{EndpointUrl}': {ex.Message}");
                return string.Empty;
            }
        }

        // Cho phép override để mock trong unit test
        public virtual async Task<string> SendHttpRequestAsync(string endpoint, string jsonPayload, string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {apiKey.Trim()}");
            }
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public static string ParseOpenAiResponse(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                var choices = obj["choices"] as JArray;
                if (choices != null && choices.Count > 0)
                {
                    string? content = choices[0]?["message"]?["content"]?.ToString();
                    return content?.Trim() ?? string.Empty;
                }
            }
            catch
            {
                // Parse error
            }
            return string.Empty;
        }

        private static string BuildSystemInstruction(string targetLanguage)
        {
            return $"Bạn là một dịch giả game chuyên nghiệp (Visual Novel, RPG kỳ ảo, Anime).\n" +
                   $"Đầu vào là văn bản OCR từ game. Nhiệm vụ của bạn:\n" +
                   $"1. Sửa các lỗi chính tả phổ biến do OCR.\n" +
                   $"2. Dịch thoát ý, mượt mà, tự nhiên theo đúng phong cách game/anime vietsub.\n" +
                   $"3. CHỈ TRẢ VỀ BẢN DỊCH {targetLanguage.ToUpperInvariant()}. Tuyệt đối KHÔNG giải thích, KHÔNG thêm lời mở đầu.";
        }

        private static string BuildUserPrompt(string text, TranslationContext? context, string targetLanguage)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Dịch câu thoại game sau đây sang {targetLanguage}:");

            if (context != null)
            {
                if (!string.IsNullOrEmpty(context.GameName)) sb.AppendLine($"Game: {context.GameName}");
                if (!string.IsNullOrEmpty(context.SpeakerName)) sb.AppendLine($"Speaker: {context.SpeakerName}");
                if (context.SpeakerProfile != null)
                {
                    var p = context.SpeakerProfile;
                    if (!string.IsNullOrEmpty(p.SpeakingStyle)) sb.AppendLine($"Style: {p.SpeakingStyle}");
                    if (!string.IsNullOrEmpty(p.PreferredPronouns)) sb.AppendLine($"Pronouns: {p.PreferredPronouns}");
                }
                if (context.PreviousLines.Count > 0)
                {
                    sb.AppendLine("Previous dialogue:");
                    foreach (var line in context.PreviousLines)
                    {
                        sb.AppendLine($"  \"{line.Original}\" -> \"{line.Translated}\"");
                    }
                }
                if (context.ActiveGlossary.Count > 0)
                {
                    sb.Append("Glossary: ");
                    var terms = new List<string>();
                    foreach (var g in context.ActiveGlossary)
                    {
                        terms.Add($"{g.Source}={g.Target}");
                    }
                    sb.AppendLine(string.Join(", ", terms));
                }
            }

            sb.AppendLine();
            sb.AppendLine("<SOURCE_TEXT>");
            sb.AppendLine(text);
            sb.AppendLine("</SOURCE_TEXT>");
            sb.AppendLine();
            sb.AppendLine($"Bản dịch {targetLanguage}:");

            return sb.ToString();
        }
    }
}
