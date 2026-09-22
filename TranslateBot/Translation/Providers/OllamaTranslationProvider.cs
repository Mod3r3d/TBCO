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
    /// Nhà cung cấp dịch thuật Local AI qua Ollama (Section 14 của Master Plan)
    /// Hỗ trợ dịch ngoại tuyến 100% không phụ thuộc internet qua các model: Qwen2.5, Gemma 2, Llama 3.
    /// </summary>
    public class OllamaTranslationProvider : ITranslationProvider
    {
        private readonly HttpClient _httpClient;

        public string Host { get; set; } = "http://localhost:11434";
        public string ModelName { get; set; } = "qwen2.5:7b";
        public string TargetLanguage { get; set; } = "tiếng Việt";
        public double Temperature { get; set; } = 0.15;

        public string GenerateEndpoint => $"{Host.TrimEnd('/')}/api/generate";
        public string TagsEndpoint => $"{Host.TrimEnd('/')}/api/tags";

        public OllamaTranslationProvider(
            string host = "http://localhost:11434",
            string modelName = "qwen2.5:7b",
            HttpClient? httpClient = null)
        {
            Host = host;
            ModelName = modelName;
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<bool> CheckAvailabilityAsync()
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var response = await _httpClient.GetAsync(TagsEndpoint, cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public Task<string> TranslateAsync(string text)
            => TranslateAsync(text, null);

        public async Task<string> TranslateAsync(string text, TranslationContext? context)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            try
            {
                string system = BuildSystemInstruction(TargetLanguage);
                string prompt = BuildPrompt(text, context, TargetLanguage);

                var payload = new
                {
                    model = ModelName,
                    prompt = prompt,
                    system = system,
                    stream = false,
                    options = new
                    {
                        temperature = Temperature
                    }
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                string responseJson = await SendHttpRequestAsync(GenerateEndpoint, jsonPayload);

                if (string.IsNullOrWhiteSpace(responseJson)) return string.Empty;

                return ParseOllamaResponse(responseJson);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[OLLAMA_ERROR] Lỗi gọi Ollama ({Host}): {ex.Message}");
                return string.Empty;
            }
        }

        // Cho phép override để mock trong unit tests
        public virtual async Task<string> SendHttpRequestAsync(string endpoint, string jsonPayload)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public static string ParseOllamaResponse(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                string? responseText = obj["response"]?.ToString();
                return responseText?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildSystemInstruction(string targetLanguage)
        {
            return $"Bạn là một dịch giả game chuyên nghiệp (Visual Novel, Anime, RPG).\n" +
                   $"Dịch câu thoại game sau sang {targetLanguage.ToUpperInvariant()}.\n" +
                   $"CHỈ TRẢ VỀ BẢN DỊCH, tuyệt đối không giải thích hay thêm bớt.";
        }

        private static string BuildPrompt(string text, TranslationContext? context, string targetLanguage)
        {
            var sb = new StringBuilder();
            if (context != null)
            {
                if (!string.IsNullOrEmpty(context.GameName)) sb.AppendLine($"Game: {context.GameName}");
                if (!string.IsNullOrEmpty(context.SpeakerName)) sb.AppendLine($"Speaker: {context.SpeakerName}");
                if (context.SpeakerProfile != null)
                {
                    if (!string.IsNullOrEmpty(context.SpeakerProfile.PreferredPronouns))
                        sb.AppendLine($"Pronouns: {context.SpeakerProfile.PreferredPronouns}");
                }
                if (context.PreviousLines.Count > 0)
                {
                    sb.AppendLine("Previous context:");
                    foreach (var line in context.PreviousLines)
                    {
                        sb.AppendLine($"  {line.Original} -> {line.Translated}");
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

            sb.AppendLine($"Dịch sang {targetLanguage}: \"{text}\"");
            return sb.ToString();
        }
    }
}
