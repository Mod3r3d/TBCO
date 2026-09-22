using System;
using System.Collections.Generic;

namespace TranslateBot.Dialogue
{
    // Một mục lịch sử: câu thoại gốc + bản dịch, người nói, độ trễ và thời gian hiển thị.
    public class DialogueEntry
    {
        public int SequenceId { get; init; }
        public string OriginalText { get; init; } = string.Empty;
        public string TranslatedText { get; init; } = string.Empty;
        public string? Speaker { get; init; }
        public double LatencyMs { get; init; }
        public DateTime DisplayedAt { get; init; } = DateTime.Now;

        public string FormattedSpeaker => string.IsNullOrWhiteSpace(Speaker) ? "" : $"[{Speaker}] ";
        public string FormattedTime => DisplayedAt.ToString("HH:mm:ss");
    }

    public class DialogueHistory
    {
        private int _maxHistoryEntries = 200;
        private readonly LinkedList<DialogueEntry> _entries = new();

        public DialogueEntry? Current { get; private set; }
        public DialogueEntry? Previous { get; private set; }

        public event Action<DialogueEntry>? OnNewEntry;
        public event Action? OnCleared;

        public IReadOnlyCollection<DialogueEntry> Entries => _entries;
        public int Count => _entries.Count;
        public int MaxCapacity => _maxHistoryEntries;

        public void SetCapacity(int limit)
        {
            if (limit <= 0) limit = 50;
            _maxHistoryEntries = limit;
            while (_entries.Count > _maxHistoryEntries)
            {
                _entries.RemoveFirst();
            }
        }

        public void Add(int sequenceId, string originalText, string translatedText, string? speaker = null, double latencyMs = 0)
        {
            var entry = new DialogueEntry
            {
                SequenceId = sequenceId,
                OriginalText = originalText,
                TranslatedText = translatedText,
                Speaker = speaker,
                LatencyMs = latencyMs,
                DisplayedAt = DateTime.Now
            };

            Previous = Current;
            Current = entry;

            _entries.AddLast(entry);
            while (_entries.Count > _maxHistoryEntries)
            {
                _entries.RemoveFirst();
            }

            OnNewEntry?.Invoke(entry);
        }

        public void Add(DialogueEntry entry)
        {
            if (entry == null) return;
            Previous = Current;
            Current = entry;
            _entries.AddLast(entry);
            while (_entries.Count > _maxHistoryEntries)
            {
                _entries.RemoveFirst();
            }
            OnNewEntry?.Invoke(entry);
        }

        public void Add(int sequenceId, string originalText, string translatedText)
            => Add(sequenceId, originalText, translatedText, null, 0);

        public void Clear()
        {
            _entries.Clear();
            Current = null;
            Previous = null;
            OnCleared?.Invoke();
        }

        public string ExportToText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# FGO Dialogue Session Export — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"# Total lines: {_entries.Count}");
            sb.AppendLine("------------------------------------------------------------------");
            foreach (var item in _entries)
            {
                string spk = string.IsNullOrWhiteSpace(item.Speaker) ? "" : $"[{item.Speaker}] ";
                sb.AppendLine($"[{item.FormattedTime}] #{item.SequenceId:D4} {spk}{item.TranslatedText}");
                sb.AppendLine($"    Gốc: {item.OriginalText}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public string ExportToJson()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(_entries, Newtonsoft.Json.Formatting.Indented);
        }
    }
}
