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
    /// - Callback OnOrderedResult được invoke NGOÀI LOCK để ngăn chặn triệt để nguy cơ deadlock hoặc callback re-entry.
    /// - Quản lý theo thế hệ (GenerationId) để ngăn câu từ phiên/hồ sơ cũ đè lên phiên mới.
    /// </summary>
    public class ReorderBuffer
    {
        private int _latestDispatchedSequence = -1;
        private long _currentGenerationId = 0;
        private long _droppedLateCount = 0;
        private readonly object _lock = new();

        public int LatestDispatchedSequence => _latestDispatchedSequence;
        public long CurrentGenerationId => _currentGenerationId;
        public long DroppedLateJobCount => _droppedLateCount;

        public Action<DialogueJob, string>? OnOrderedResult { get; set; }

        public void Submit(DialogueJob job, string result)
        {
            Action<DialogueJob, string>? callback = null;
            bool shouldDispatch = false;

            lock (_lock)
            {
                // Kiểm tra GenerationId: nếu khác thế hệ hiện tại (job thuộc phiên/cửa sổ/hồ sơ trước), bỏ qua ngay
                if (job.GenerationId != _currentGenerationId)
                {
                    _droppedLateCount++;
                    AppLogger.Info($"[GENERATION_MISMATCH_DROPPED] #{job.SequenceId} (Gen {job.GenerationId}) != Hien tai (Gen {_currentGenerationId}), bo qua.");
                    return;
                }

                // Câu đầu tiên hoặc câu mới hơn/bằng câu mới nhất -> Hiển thị ngay lập tức!
                if (_latestDispatchedSequence == -1 || job.SequenceId >= _latestDispatchedSequence)
                {
                    _latestDispatchedSequence = job.SequenceId;
                    shouldDispatch = true;
                    callback = OnOrderedResult;
                }
                else
                {
                    _droppedLateCount++;
                    // Câu cũ về muộn hơn câu đã có trên màn hình -> Bỏ qua để không bị giật lùi phụ đề
                    AppLogger.Info($"[LATE_TRANSLATION_DROPPED] #{job.SequenceId} den muon hon #{_latestDispatchedSequence}, bo qua.");
                }
            }

            // Phase 1 (P0): Invoke callback bên ngoài lock để tránh deadlock và contention
            if (shouldDispatch)
            {
                callback?.Invoke(job, result);
            }
        }

        public void Reset(long newGenerationId = 0)
        {
            lock (_lock)
            {
                _latestDispatchedSequence = -1;
                _currentGenerationId = newGenerationId;
                _droppedLateCount = 0;
            }
        }

        public void AdvanceGeneration(long nextGenerationId)
        {
            lock (_lock)
            {
                _latestDispatchedSequence = -1;
                _currentGenerationId = nextGenerationId;
            }
        }
    }
}
