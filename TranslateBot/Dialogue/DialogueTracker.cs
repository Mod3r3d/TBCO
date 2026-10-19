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

        // Phase 3: Progressive Dialogue 2.0 & Adaptive Stabilization
        public StabilityConfig Config { get; set; }

        public DialogueTracker(StabilityConfig? config = null)
        {
            Config = config ?? StabilityConfig.Balanced;
        }

        public double EffectiveStableDurationMs => GetEffectiveStableDuration();

        private double GetEffectiveStableDuration()
        {
            if (!Config.EnableAdaptive || CurrentDialogue == null) return Config.StableDurationMs;

            int len = CurrentDialogue.NormalizedText.Length;
            if (len > 50) return Config.StableDurationMs + 80;
            if (len > 25) return Config.StableDurationMs + 40;
            return Config.StableDurationMs;
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

                // 4. Section 9: OCR Correction
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

                // 5. Chuyển câu thoại mới (Transition Flush)
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
