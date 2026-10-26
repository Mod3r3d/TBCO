using System.Threading.Tasks;

namespace TranslateBot.Translation
{
    public interface ITranslationProvider
    {
        // Legacy: dịch đơn giản không context (backward compat cho Snapshot, tests)
        Task<string> TranslateAsync(string text);

        // Stage 5: dịch có context — previous lines, glossary, speaker profile
        Task<string> TranslateAsync(string text, TranslationContext? context)
            => TranslateAsync(text);  // Default implementation: fallback về legacy

        // Stage 3: Hợp đồng dữ liệu translation có kiểu rõ ràng và metadata theo từng request
        async Task<TranslationResult> TranslateRequestAsync(TranslationRequest request)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string text = await TranslateAsync(request.Text, request.Context);
                sw.Stop();
                return new TranslationResult
                {
                    Text = text ?? "",
                    ProviderName = GetType().Name,
                    LatencyMs = sw.Elapsed.TotalMilliseconds,
                    Status = string.IsNullOrWhiteSpace(text) ? TranslationStatus.EmptyOutput : TranslationStatus.Success
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return TranslationResult.CreateFailure(GetType().Name, TranslationStatus.ProviderError, ex.Message, sw.Elapsed.TotalMilliseconds);
            }
        }
    }
}