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
    }
}