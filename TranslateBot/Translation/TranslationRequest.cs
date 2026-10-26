using System;
using System.Threading;

namespace TranslateBot.Translation
{
    /// <summary>
    /// Hợp đồng yêu cầu dịch thuật bất biến và có kiểu tường minh (Stage 3 của plan.md).
    /// Đóng gói trọn vẹn văn bản, cặp ngôn ngữ, context snapshot, profile ID và cancellation token.
    /// </summary>
    public class TranslationRequest
    {
        public string Text { get; set; } = "";
        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "vi";
        public TranslationContext? Context { get; set; }
        public string? ProfileId { get; set; }
        public int SequenceId { get; set; }
        public bool AllowCache { get; set; } = true;
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

        public TranslationRequest() { }

        public TranslationRequest(
            string text, 
            TranslationContext? context = null, 
            string sourceLanguage = "auto", 
            string targetLanguage = "vi", 
            int sequenceId = 0,
            CancellationToken cancellationToken = default)
        {
            Text = text ?? "";
            Context = context;
            SourceLanguage = sourceLanguage ?? "auto";
            TargetLanguage = targetLanguage ?? "vi";
            SequenceId = sequenceId;
            CancellationToken = cancellationToken;
        }
    }
}
