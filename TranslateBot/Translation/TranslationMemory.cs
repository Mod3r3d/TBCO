using System;
using System.Collections.Generic;
using TranslateBot.Database;
using TranslateBot.Dialogue;
using TranslateBot.Infrastructure;

namespace TranslateBot.Translation
{
    /// <summary>
    /// Translation Memory 2.0 (TBCO Master Plan Section 6).
    /// Kiến trúc Two-Level Cache:
    ///   - L1: In-Memory LRU Cache (truy vấn tức thì < 0.1ms)
    ///   - L2: Persistent SQLite Store (lưu trữ bền vững trên đĩa, tồn tại qua các phiên khởi động lại)
    /// </summary>
    public class TranslationMemory : IDisposable
    {
        private readonly int _maxEntries;
        private readonly IMemoryStore _persistentStore;
        private readonly object _lock = new();

        // L1 Exact LRU cache
        private readonly LinkedList<(string Key, string Value)> _lruOrder = new();
        private readonly Dictionary<string, LinkedListNode<(string Key, string Value)>> _exactCache = new();

        // L1 Fuzzy mapping
        private readonly Dictionary<string, string> _fuzzyMapping = new();

        public int HitCount { get; private set; }
        public int MissCount { get; private set; }
        public int L1EntryCount { get { lock (_lock) { return _exactCache.Count; } } }
        public int TotalPersistentCount => _persistentStore.GetCount();
        public IMemoryStore PersistentStore => _persistentStore;

        public TranslationMemory(int maxEntries = 1000, IMemoryStore? persistentStore = null)
        {
            _maxEntries = maxEntries;
            _persistentStore = persistentStore ?? new SqliteMemoryStore();
        }

        public string? TryGetTranslation(string normalizedText)
        {
            if (string.IsNullOrWhiteSpace(normalizedText)) return null;

            lock (_lock)
            {
                // 1. L1 Exact Match (RAM LRU)
                if (_exactCache.TryGetValue(normalizedText, out var node))
                {
                    _lruOrder.Remove(node);
                    _lruOrder.AddFirst(node);
                    HitCount++;
                    AppLogger.Info($"[CACHE_HIT_L1_EXACT] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\"");
                    return node.Value.Value;
                }

                // 2. L1 Fuzzy Match (RAM)
                if (_fuzzyMapping.TryGetValue(normalizedText, out var canonicalKey))
                {
                    if (_exactCache.TryGetValue(canonicalKey, out var canonicalNode))
                    {
                        _lruOrder.Remove(canonicalNode);
                        _lruOrder.AddFirst(canonicalNode);
                        HitCount++;
                        AppLogger.Info($"[CACHE_HIT_L1_FUZZY] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\" → canonical");
                        return canonicalNode.Value.Value;
                    }
                }

                // 3. L2 Persistent SQLite Exact Match
                string? persistentResult = _persistentStore.TryGet(normalizedText);
                if (!string.IsNullOrEmpty(persistentResult))
                {
                    // Nạp ngược vào L1 để các lần gọi sau đạt tốc độ tối đa
                    StoreL1(normalizedText, persistentResult);
                    HitCount++;
                    AppLogger.Info($"[CACHE_HIT_SQLITE] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\"");
                    return persistentResult;
                }

                // 4. L2 Persistent SQLite Fuzzy Match
                if (_persistentStore.TryGetFuzzy(normalizedText, out var canonicalDbText, out var canonicalTranslation))
                {
                    if (!string.IsNullOrEmpty(canonicalTranslation))
                    {
                        _fuzzyMapping[normalizedText] = canonicalDbText!;
                        StoreL1(canonicalDbText!, canonicalTranslation);
                        HitCount++;
                        AppLogger.Info($"[CACHE_HIT_SQLITE_FUZZY] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\" → \"{canonicalDbText}\"");
                        return canonicalTranslation;
                    }
                }

                // 5. In-Memory Fuzzy Scan (chỉ quét khi L1 có dưới 200 câu)
                if (_exactCache.Count <= 200)
                {
                    foreach (var kvp in _exactCache)
                    {
                        if (TextSimilarity.IsLikelyOcrCorrection(normalizedText, kvp.Key))
                        {
                            _fuzzyMapping[normalizedText] = kvp.Key;
                            _persistentStore.StoreFuzzyMapping(normalizedText, kvp.Key);
                            HitCount++;
                            AppLogger.Info($"[CACHE_HIT_FUZZY_SCAN] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\"");
                            return kvp.Value.Value.Value;
                        }
                    }
                }

                MissCount++;
                return null;
            }
        }

        public void Store(
            string normalizedText,
            string translation,
            string originalText = "",
            string? speaker = null,
            string provider = "Gemini",
            string? model = null)
        {
            if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(translation))
                return;

            lock (_lock)
            {
                // Lưu vào L1 RAM
                StoreL1(normalizedText, translation);

                // Lưu bền vững vào L2 SQLite
                _persistentStore.Store(
                    normalizedText,
                    string.IsNullOrEmpty(originalText) ? normalizedText : originalText,
                    translation,
                    speaker,
                    provider,
                    model);
            }
        }

        public void StoreTranslation(string normalizedText, string translation, string? speaker = null)
        {
            Store(normalizedText, translation, normalizedText, speaker);
        }

        public void StoreVariant(string variantText, string canonicalNormalizedText)
        {
            if (string.IsNullOrWhiteSpace(variantText) || string.IsNullOrWhiteSpace(canonicalNormalizedText))
                return;

            lock (_lock)
            {
                _fuzzyMapping[variantText] = canonicalNormalizedText;
                _persistentStore.StoreFuzzyMapping(variantText, canonicalNormalizedText);
            }
        }

        private void StoreL1(string normalizedText, string translation)
        {
            if (_exactCache.TryGetValue(normalizedText, out var existing))
            {
                _lruOrder.Remove(existing);
                existing.Value = (normalizedText, translation);
                _lruOrder.AddFirst(existing);
                return;
            }

            while (_exactCache.Count >= _maxEntries && _lruOrder.Last != null)
            {
                var oldest = _lruOrder.Last!;
                _exactCache.Remove(oldest.Value.Key);
                _lruOrder.RemoveLast();
            }

            var newNode = _lruOrder.AddFirst((normalizedText, translation));
            _exactCache[normalizedText] = newNode;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _exactCache.Clear();
                _lruOrder.Clear();
                _fuzzyMapping.Clear();
                _persistentStore.Clear();
                HitCount = 0;
                MissCount = 0;
            }
        }

        public void Dispose()
        {
            _persistentStore.Dispose();
        }
    }
}
