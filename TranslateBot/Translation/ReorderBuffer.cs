using System;
using TranslateBot.Dialogue;
using TranslateBot.Infrastructure;

namespace TranslateBot.Translation
{
    /// <summary>
    /// Bộ điều phối kết quả dịch theo mô hình "Latest-Wins" (Thời gian thực).
    /// Kế thừa từ thiết kế của translate-bot.zip, loại bỏ hoàn toàn nguy cơ Deadlock:
    /// - Khi câu thoại mới hơn hoàn thành, lập tức đẩy lên màn hình.
    /// - Câu thoại cũ đến muộn (do mạng trễ hoặc retry) tự động bị bỏ qua để tránh giật lùi phụ đề.
    /// - Không bao giờ bị treo cứng hệ thống khi có câu bị Deduplicator lọc bỏ hoặc trôi nhanh.
    /// </summary>
    public class ReorderBuffer
    {
        private int _latestDispatchedSequence = -1;
        private readonly object _lock = new();

        public Action<DialogueJob, string>? OnOrderedResult { get; set; }

        public void Submit(DialogueJob job, string result)
        {
            lock (_lock)
            {
                // Câu đầu tiên hoặc câu mới hơn/bằng câu mới nhất -> Hiển thị ngay lập tức!
                if (_latestDispatchedSequence == -1 || job.SequenceId >= _latestDispatchedSequence)
                {
                    _latestDispatchedSequence = job.SequenceId;
                    OnOrderedResult?.Invoke(job, result);
                }
                else
                {
                    // Câu cũ về muộn hơn câu đã có trên màn hình -> Bỏ qua để không bị giật lùi phụ đề
                    AppLogger.Info($"[LATE_TRANSLATION_DROPPED] #{job.SequenceId} đến muộn hơn #{_latestDispatchedSequence}, bỏ qua.");
                }
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _latestDispatchedSequence = -1;
            }
        }
    }
}
