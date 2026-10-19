using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TranslateBot.Infrastructure;

namespace TranslateBot.Dialogue
{
    public class DialogueTracker
    {
        private readonly object _syncLock = new();
        private DialogueJob? _currentDialogue;

        public DialogueJob? CurrentDialogue
        {
            get { lock (_syncLock) { return _currentDialogue; } }
            private set { lock (_syncLock) { _currentDialogue = value; } }
        }

        public event Action? OnTransitionDetected;
        private int _sequenceCounter = 0;
        private readonly Queue<DialogueJob> _completedQueue = new();
        private readonly DialogueDeduplicator _deduplicator = new();
        private string? _lastFinalizedNormalized;
        private DateTime _lastFinalizedTime = DateTime.MinValue;

        // Phase 3: Progressive Dialogue 2.0 & Adaptive Stabilization
        public StabilityConfig Config { get; set; }

        public DialogueTracker(StabilityConfig? config = null)
        {
            Config = config ?? StabilityConfig.Balanced;
        }

        public double EffectiveStableDurationMs => GetEffectiveStableDuration();

        private static bool EndsWithTerminalPunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.TrimEnd();
            if (t.Length == 0) return false;
            char last = t[^1];
            return last == '.' || last == '!' || last == '?' || last == '。' || last == '！' || last == '？'
                || last == '"' || last == '”' || last == '」' || last == '』';
        }

        private double GetEffectiveStableDuration()
        {
            if (!Config.EnableAdaptive || CurrentDialogue == null) return Config.StableDurationMs;

            double duration = Config.StableDurationMs;
            string norm = CurrentDialogue.NormalizedText;
            int len = norm.Length;

            // Bù giờ typewriter: văn bản càng dài thì khoảng dừng giữa các ký tự/dòng càng lớn
            if (len > 50) duration += 120;
            else if (len > 25) duration += 60;

            // Nếu câu CHƯA có dấu kết thúc câu (đang viết dở hoặc sắp xuống dòng), kiên nhẫn đợi thêm
            if (!EndsWithTerminalPunctuation(norm))
            {
                duration += 280;
            }

            return Math.Min(duration, Config.MaxWaitMs);
        }

        public void ProcessOcrResult(string text)
        {
            Action? transitionAction = null;
            lock (_syncLock)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    HandleEmptyText();
                    return;
                }

                string normalizedNew = NormalizeText(text);

                // Bỏ qua rác OCR quá ngắn (< MinTextLength hoặc không có đủ chữ cái hợp lệ)
                if (!IsValidDialogueText(normalizedNew, Config.MinTextLength))
                {
                    return;
                }

                // 1. Nếu chưa có câu nào đang theo dõi
                if (_currentDialogue == null)
                {
                    // Kiểm tra xem câu mới có phải là phần cuộn dòng tiếp theo của câu vừa finalize không (trong vòng 2.5 giây)
                    if (!string.IsNullOrEmpty(_lastFinalizedNormalized) && (DateTime.Now - _lastFinalizedTime).TotalMilliseconds < 2500)
                    {
                        if (TextSimilarity.TryFindScrollOverlap(_lastFinalizedNormalized, normalizedNew, out string stitched))
                        {
                            AppLogger.Info($"[TRACKER_SCROLL_RECONNECT] Nối câu #{_sequenceCounter} vừa kết thúc sớm với dòng mới: \"{stitched}\"");
                            _currentDialogue = new DialogueJob(_sequenceCounter, stitched, stitched);
                            _currentDialogue.State = DialogueState.Extending;
                            _currentDialogue.LastSeenAt = DateTime.Now;
                            return;
                        }
                    }

                    _sequenceCounter++;
                    _currentDialogue = new DialogueJob(_sequenceCounter, text, normalizedNew);
                    return;
                }

                string currNorm = _currentDialogue.NormalizedText;

                // 2. Fast path: Chữ đang dài ra (typewriter)
                if (TextSimilarity.IsFuzzyPrefix(currNorm, normalizedNew, Config.SimilarityThreshold))
                {
                    _currentDialogue.RawText = text;
                    _currentDialogue.NormalizedText = normalizedNew;
                    _currentDialogue.LastSeenAt = DateTime.Now;
                    _currentDialogue.State = DialogueState.Extending;
                    return;
                }

                // 3. Fast path: Chữ đứng im, không đổi
                if (normalizedNew == currNorm)
                {
                    UpdateStability();
                    return;
                }

                // 4. MỚI: Cuộn dòng (Scroll Overlap) — khi câu dài xuống dòng khiến dòng trên cuộn đi
                if (TextSimilarity.TryFindScrollOverlap(currNorm, normalizedNew, out string mergedScroll))
                {
                    AppLogger.Info($"[TRACKER_SCROLL_MERGE] #{_currentDialogue.SequenceId} Ghép cuộn dòng: \"{mergedScroll}\"");
                    _currentDialogue.RawText = mergedScroll;
                    _currentDialogue.NormalizedText = mergedScroll;
                    _currentDialogue.LastSeenAt = DateTime.Now;
                    _currentDialogue.State = DialogueState.Extending;
                    return;
                }

                // 5. Section 9: OCR Correction
                if (TextSimilarity.IsLikelyOcrCorrection(currNorm, normalizedNew))
                {
                    AppLogger.Info($"[TRACKER_OCR_CORRECTION] #{_currentDialogue.SequenceId} " +
                                      $"old=\"{currNorm}\" new=\"{normalizedNew}\"");
                    _currentDialogue.RawText = text;
                    _currentDialogue.NormalizedText = normalizedNew;
                    _currentDialogue.LastSeenAt = DateTime.Now;
                    _currentDialogue.State = DialogueState.Stable;
                    return;
                }

                // 6. Chuyển câu thoại mới (Transition Flush)
                if (_currentDialogue != null)
                {
                    _currentDialogue.State = DialogueState.Confirmed;
                }
                FinalizeCurrentDialogue("TRANSITION_FLUSH");
                transitionAction = OnTransitionDetected;
                _sequenceCounter++;
                _currentDialogue = new DialogueJob(_sequenceCounter, text, normalizedNew);
            }

            transitionAction?.Invoke();
        }

        public void CheckStability()
        {
            lock (_syncLock)
            {
                if (_currentDialogue != null)
                {
                    UpdateStability();
                }
            }
        }

        private void UpdateStability()
        {
            if (CurrentDialogue == null) return;

            var elapsed = (DateTime.Now - CurrentDialogue.LastSeenAt).TotalMilliseconds;

            if (CurrentDialogue.State == DialogueState.Partial || CurrentDialogue.State == DialogueState.Extending)
            {
                CurrentDialogue.State = DialogueState.Stable;
            }

            double stableDuration = GetEffectiveStableDuration();
            if (CurrentDialogue.State == DialogueState.Stable && elapsed >= stableDuration)
            {
                CurrentDialogue.State = DialogueState.Confirmed;
                FinalizeCurrentDialogue("STABLE_TIMEOUT");
            }
            else if (elapsed >= Config.MaxWaitMs)
            {
                CurrentDialogue.State = DialogueState.Confirmed;
                FinalizeCurrentDialogue("MAX_WAIT_TIMEOUT");
            }
        }

        private void HandleEmptyText()
        {
            if (CurrentDialogue != null)
            {
                var elapsed = (DateTime.Now - CurrentDialogue.LastSeenAt).TotalMilliseconds;
                if (elapsed > Config.EmptyGapMs)
                {
                    CurrentDialogue.State = DialogueState.Confirmed;
                    FinalizeCurrentDialogue("EMPTY_GAP");
                }
            }
        }

        private void FinalizeCurrentDialogue(string reason)
        {
            if (CurrentDialogue == null || CurrentDialogue.State != DialogueState.Confirmed)
                return;

            // Dedup: lưới an toàn cuối cùng chống việc CÙNG một câu bị enqueue 2 lần
            // (VD: false split lọt qua bước 4, hoặc OCR đọc lại đúng câu vừa flush).
            if (_deduplicator.IsDuplicate(CurrentDialogue.NormalizedText))
            {
                AppLogger.Warn($"[DIALOGUE_DEDUPED] #{CurrentDialogue.SequenceId} ({reason}): \"{CurrentDialogue.NormalizedText}\"");
                CurrentDialogue = null;
                return;
            }

            _deduplicator.Record(CurrentDialogue.NormalizedText);
            AppLogger.Info($"[DIALOGUE_CONFIRMED] #{CurrentDialogue.SequenceId} ({reason}): \"{CurrentDialogue.NormalizedText}\"");

            _lastFinalizedNormalized = CurrentDialogue.NormalizedText;
            _lastFinalizedTime = DateTime.Now;

            _completedQueue.Enqueue(CurrentDialogue);
            CurrentDialogue = null;
        }

        // Hàm này sẽ được Vòng lặp chính gọi để lấy các câu đã sẵn sàng dịch
        public List<DialogueJob> GetPendingTranslations()
        {
            var pending = new List<DialogueJob>();
            lock (_syncLock)
            {
                while (_completedQueue.TryDequeue(out var job))
                {
                    pending.Add(job);
                }
            }
            return pending;
        }

        public void Reset()
        {
            lock (_syncLock)
            {
                _currentDialogue = null;
                _completedQueue.Clear();
                _deduplicator.Reset();
                _lastFinalizedNormalized = null;
                _lastFinalizedTime = DateTime.MinValue;
            }
        }

        private static bool IsValidDialogueText(string text, int minLength = 3)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < minLength) return false;
            // Phải chứa ít nhất 2 ký tự chữ cái (tránh rác như "...", "---", "•", "??", "g")
            int letterCount = 0;
            foreach (char c in text)
            {
                if (char.IsLetter(c)) letterCount++;
                if (letterCount >= 2) return true;
            }
            return false;
        }

        private string NormalizeText(string text)
        {
            // Lọc bỏ timestamp video trình phát nếu người dùng quét trúng seekbar YouTube (VD: 1:38 / 17:10)
            string cleaned = Regex.Replace(text, @"\b\d{1,2}:\d{2}\s*/\s*\d{1,2}:\d{2}\b", " ");
            // Xóa khoảng trắng thừa hoặc xuống dòng
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }
    }
}
