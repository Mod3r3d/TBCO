using System;
using System.Collections.Generic;
using TranslateBot.Database;
using TranslateBot.Dialogue;
using TranslateBot.Infrastructure;

namespace TranslateBot.Translation
{
    /// <summary>
    /// Translation Memory 2.0 (TBCO Master Plan Section 6 & Giai đoạn 4).
    /// Kiến trúc Two-Level Cache có cô lập theo Game Profile và Cặp ngôn ngữ:
    ///   - L1: In-Memory LRU Cache (truy vấn tức thì &lt; 0.1ms, key phân tách theo profile, bảo vệ UserApproved)
    ///   - L2: Persistent SQLite Store (lưu trữ bền vững trên đĩa, Schema v2 với CacheKey)
    /// </summary>
    public class TranslationMemory : IDisposable
    {
        private readonly int _maxEntries;
        private readonly IMemoryStore _persistentStore;
        private readonly object _lock = new();

        // L1 Exact LRU cache: Key là chuỗi cache key đầy đủ (v1::profile::source->target::normalizedText)
        private readonly LinkedList<(string CacheKey, string NormalizedText, string Translation, string ProfileId, bool IsUserApproved, CacheEntryType EntryType)> _lruOrder = new();
        private readonly Dictionary<string, LinkedListNode<(string CacheKey, string NormalizedText, string Translation, string ProfileId, bool IsUserApproved, CacheEntryType EntryType)>> _exactCache = new();

        // L1 Fuzzy mapping: Key là (profile::source->target::variantText) -> canonicalCacheKey
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
            return TryGetTranslation(normalizedText, "global", "auto", "vi");
        }

        public string? TryGetTranslation(
            string normalizedText, 
            string profileId, 
            string sourceLang = "auto", 
            string targetLang = "vi")
        {
            if (string.IsNullOrWhiteSpace(normalizedText)) return null;

            string cleanProfile = string.IsNullOrWhiteSpace(profileId) ? "global" : profileId.Trim().ToLowerInvariant();
            string cleanSource = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang.Trim().ToLowerInvariant();
            string cleanTarget = string.IsNullOrWhiteSpace(targetLang) ? "vi" : targetLang.Trim().ToLowerInvariant();

            string exactKey = TranslationCacheKey.BuildKey(normalizedText, cleanProfile, cleanSource, cleanTarget);
            string globalKey = TranslationCacheKey.BuildKey(normalizedText, "global", cleanSource, cleanTarget);
            string fuzzyKey = $"{cleanProfile}::{cleanSource}->{cleanTarget}::{normalizedText.Trim()}";

            lock (_lock)
            {
                // 1. L1 Exact Match (Profile khớp chính xác)
                if (_exactCache.TryGetValue(exactKey, out var node))
                {
                    if (node.Value.EntryType != CacheEntryType.FallbackDegraded)
                    {
                        _lruOrder.Remove(node);
                        _lruOrder.AddFirst(node);
                        HitCount++;
                        AppLogger.Info($"[CACHE_HIT_L1_EXACT] [{cleanProfile}] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\"");
                        return node.Value.Translation;
                    }
                }

                // 2. L1 Exact Match (Fallback sang Profile 'global' nếu khác)
                if (cleanProfile != "global" && _exactCache.TryGetValue(globalKey, out var globalNode))
                {
                    if (globalNode.Value.EntryType != CacheEntryType.FallbackDegraded)
                    {
                        _lruOrder.Remove(globalNode);
                        _lruOrder.AddFirst(globalNode);
                        HitCount++;
                        AppLogger.Info($"[CACHE_HIT_L1_GLOBAL] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\"");
                        return globalNode.Value.Translation;
                    }
                }

                // 3. L1 Fuzzy Match (Cô lập trong cùng Profile và Cặp ngôn ngữ)
                if (_fuzzyMapping.TryGetValue(fuzzyKey, out var canonicalKey))
                {
                    if (_exactCache.TryGetValue(canonicalKey, out var canonicalNode))
                    {
                        if (canonicalNode.Value.EntryType != CacheEntryType.FallbackDegraded)
                        {
                            _lruOrder.Remove(canonicalNode);
                            _lruOrder.AddFirst(canonicalNode);
                            HitCount++;
                            AppLogger.Info($"[CACHE_HIT_L1_FUZZY] [{cleanProfile}] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\" → canonical");
                            return canonicalNode.Value.Translation;
                        }
                    }
                }

                // 4. L2 Persistent SQLite Exact Match (có xét độ ưu tiên Profile và UserApproved)
                var entry = _persistentStore.TryGetEntry(normalizedText, cleanProfile, cleanSource, cleanTarget, allowFallbackDegraded: false);
                if (entry != null && !string.IsNullOrEmpty(entry.Translation))
                {
                    // Nạp ngược vào L1 để các lần gọi sau đạt tốc độ tối đa
                    StoreL1(entry.GetCacheKey(), entry.NormalizedText, entry.Translation, entry.ProfileId, entry.IsUserApproved, entry.EntryType);
                    HitCount++;
                    AppLogger.Info($"[CACHE_HIT_SQLITE] [{entry.ProfileId}] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\"");
                    return entry.Translation;
                }

                // 5. L2 Persistent SQLite Fuzzy Match
                if (_persistentStore.TryGetFuzzy(normalizedText, cleanProfile, cleanSource, cleanTarget, out var canonicalDbText, out var canonicalTranslation))
                {
                    if (!string.IsNullOrEmpty(canonicalTranslation))
                    {
                        string foundKey = TranslationCacheKey.BuildKey(canonicalDbText!, cleanProfile, cleanSource, cleanTarget);
                        _fuzzyMapping[fuzzyKey] = foundKey;
                        StoreL1(foundKey, canonicalDbText!, canonicalTranslation, cleanProfile, false, CacheEntryType.ModelOutput);
                        HitCount++;
                        AppLogger.Info($"[CACHE_HIT_SQLITE_FUZZY] [{cleanProfile}] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\" → \"{canonicalDbText}\"");
                        return canonicalTranslation;
                    }
                }

                // 6. In-Memory Fuzzy Scan (chỉ quét khi L1 có dưới 200 câu và chỉ quét trong cùng profile hoặc global)
                if (_exactCache.Count <= 200)
                {
                    foreach (var kvp in _exactCache)
                    {
                        if (kvp.Value.Value.EntryType == CacheEntryType.FallbackDegraded)
                            continue;

                        // Fuzzy matching chỉ so sánh giữa các câu cùng Game Profile hoặc profile global
                        if (kvp.Value.Value.ProfileId != cleanProfile && kvp.Value.Value.ProfileId != "global")
                            continue;

                        if (TextSimilarity.IsLikelyOcrCorrection(normalizedText, kvp.Value.Value.NormalizedText))
                        {
                            string targetCanonicalKey = kvp.Key;
                            _fuzzyMapping[fuzzyKey] = targetCanonicalKey;
                            _persistentStore.StoreFuzzyMapping(normalizedText, kvp.Value.Value.NormalizedText, cleanProfile, cleanSource, cleanTarget);
                            HitCount++;
                            AppLogger.Info($"[CACHE_HIT_FUZZY_SCAN] [{cleanProfile}] \"{normalizedText[..Math.Min(40, normalizedText.Length)]}...\"");
                            return kvp.Value.Value.Translation;
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
            Store(
                normalizedText, 
                translation, 
                profileId: "global", 
                sourceLang: "auto", 
                targetLang: "vi", 
                originalText: originalText, 
                speaker: speaker, 
                provider: provider, 
                model: model);
        }

        public void Store(
            string normalizedText,
            string translation,
            string profileId,
            string sourceLang = "auto",
            string targetLang = "vi",
            string originalText = "",
            string? speaker = null,
            string provider = "Gemini",
            string? model = null,
            CacheEntryType entryType = CacheEntryType.ModelOutput,
            bool isUserApproved = false,
            bool isFallback = false)
        {
            var entry = new TranslationMemoryEntry
            {
                NormalizedText = normalizedText,
                OriginalText = string.IsNullOrEmpty(originalText) ? normalizedText : originalText,
                Translation = translation,
                ProfileId = profileId,
                SourceLanguage = sourceLang,
                TargetLanguage = targetLang,
                Speaker = speaker,
                Provider = provider,
                Model = model,
                EntryType = entryType,
                IsUserApproved = isUserApproved,
                IsFallback = isFallback
            };

            Store(entry);
        }

        public void Store(TranslationMemoryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.NormalizedText) || string.IsNullOrWhiteSpace(entry.Translation))
                return;

            string cacheKey = entry.GetCacheKey();

            lock (_lock)
            {
                // Lưu vào L1 RAM có bảo vệ UserApproved
                StoreL1(cacheKey, entry.NormalizedText, entry.Translation, entry.ProfileId, entry.IsUserApproved, entry.EntryType);

                // Lưu bền vững vào L2 SQLite
                _persistentStore.Store(entry);
            }
        }

        public void StoreTranslation(
            string normalizedText, 
            string translation, 
            string? speaker = null,
            string profileId = "global",
            string sourceLang = "auto",
            string targetLang = "vi")
        {
            Store(
                normalizedText, 
                translation, 
                profileId: profileId,
                sourceLang: sourceLang,
                targetLang: targetLang,
                originalText: normalizedText, 
                speaker: speaker);
        }

        public void StoreVariant(
            string variantText, 
            string canonicalNormalizedText,
            string profileId = "global",
            string sourceLang = "auto",
            string targetLang = "vi")
        {
            if (string.IsNullOrWhiteSpace(variantText) || string.IsNullOrWhiteSpace(canonicalNormalizedText))
                return;

            string cleanProfile = string.IsNullOrWhiteSpace(profileId) ? "global" : profileId.Trim().ToLowerInvariant();
            string cleanSource = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang.Trim().ToLowerInvariant();
            string cleanTarget = string.IsNullOrWhiteSpace(targetLang) ? "vi" : targetLang.Trim().ToLowerInvariant();

            string canonicalKey = TranslationCacheKey.BuildKey(canonicalNormalizedText, cleanProfile, cleanSource, cleanTarget);
            string fuzzyKey = $"{cleanProfile}::{cleanSource}->{cleanTarget}::{variantText.Trim()}";

            lock (_lock)
            {
                _fuzzyMapping[fuzzyKey] = canonicalKey;
                _persistentStore.StoreFuzzyMapping(variantText, canonicalNormalizedText, cleanProfile, cleanSource, cleanTarget);
            }
        }

        private void StoreL1(
            string cacheKey, 
            string normalizedText, 
            string translation, 
            string profileId,
            bool isUserApproved = false,
            CacheEntryType entryType = CacheEntryType.ModelOutput)
        {
            if (_exactCache.TryGetValue(cacheKey, out var existing))
            {
                _lruOrder.Remove(existing);

                // Bảo vệ bản dịch đã duyệt của người dùng: unapproved model output không được ghi đè
                if (existing.Value.IsUserApproved && !isUserApproved)
                {
                    _lruOrder.AddFirst(existing);
                    return;
                }

                existing.Value = (cacheKey, normalizedText, translation, profileId, isUserApproved, entryType);
                _lruOrder.AddFirst(existing);
                return;
            }

            while (_exactCache.Count >= _maxEntries && _lruOrder.Last != null)
            {
                var oldest = _lruOrder.Last!;
                _exactCache.Remove(oldest.Value.CacheKey);
                _lruOrder.RemoveLast();
            }

            var newNode = _lruOrder.AddFirst((cacheKey, normalizedText, translation, profileId, isUserApproved, entryType));
            _exactCache[cacheKey] = newNode;
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

        public void ClearByProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId)) return;
            string cleanProfile = profileId.Trim().ToLowerInvariant();
            string profilePattern = $"::{cleanProfile}::";

            lock (_lock)
            {
                var keysToRemove = new List<string>();
                foreach (var k in _exactCache.Keys)
                {
                    if (k.Contains(profilePattern, StringComparison.OrdinalIgnoreCase))
                    {
                        keysToRemove.Add(k);
                    }
                }
                foreach (var k in keysToRemove)
                {
                    if (_exactCache.TryGetValue(k, out var node))
                    {
                        _lruOrder.Remove(node);
                        _exactCache.Remove(k);
                    }
                }

                var fuzzyKeysToRemove = new List<string>();
                foreach (var k in _fuzzyMapping.Keys)
                {
                    if (k.StartsWith($"{cleanProfile}::", StringComparison.OrdinalIgnoreCase))
                    {
                        fuzzyKeysToRemove.Add(k);
                    }
                }
                foreach (var k in fuzzyKeysToRemove)
                {
                    _fuzzyMapping.Remove(k);
                }

                _persistentStore.ClearByProfile(cleanProfile);
            }
        }

        public int GetCountByProfile(string profileId)
        {
            return _persistentStore.GetCountByProfile(profileId);
        }

        public void Dispose()
        {
            _persistentStore.Dispose();
        }
    }
}
