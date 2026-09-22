using System;

namespace TranslateBot.Dialogue
{
    // Lớp này gói gọn mọi thông tin của một câu thoại
    public class DialogueJob
    {
        public int SequenceId { get; set; } // Số thứ tự để giữ đúng mạch truyện
        public string RawText { get; set; }
        public string NormalizedText { get; set; }
        public DialogueState State { get; set; }
        public DateTime LastSeenAt { get; set; }
        public bool IsTranslated { get; set; }

        // Stage 5: Prefetch flag — câu dịch trước khi Confirmed (priority thấp)
        public bool IsPrefetch { get; set; }

        // Phase 20: Hỗ trợ Coalescing / Hủy stale prefetch & Priority
        public bool IsCancelled { get; set; }
        public int Priority { get; set; } = 0; // -1: Low (Prefetch), 0: Normal, 1: High (User Confirmed)

        // Stage 5: Context gửi kèm request dịch (previous lines, glossary, speaker)
        public Translation.TranslationContext? Context { get; set; }

        public DialogueJob(int sequenceId, string text, string normalizedText)
        {
            SequenceId = sequenceId;
            RawText = text;
            NormalizedText = normalizedText;
            State = DialogueState.Partial;
            LastSeenAt = DateTime.Now;
            IsTranslated = false;
            IsPrefetch = false;
        }
    }
}
