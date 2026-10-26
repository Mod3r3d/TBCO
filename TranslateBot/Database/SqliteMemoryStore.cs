using System;
using System.IO;
using Microsoft.Data.Sqlite;
using TranslateBot.Infrastructure;
using TranslateBot.Translation;

namespace TranslateBot.Database
{
    /// <summary>
    /// Kho lưu trữ bộ nhớ dịch SQLite bền vững (Persistent Translation Memory 2.0).
    /// Hỗ trợ Schema v2 với CacheKey phân tách theo Game Profile, Cặp ngôn ngữ,
    /// metadata provenance và phân cấp ưu tiên UserApproved > ModelOutput > FallbackDegraded.
    /// </summary>
    public class SqliteMemoryStore : IMemoryStore
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _connection;
        private readonly object _lock = new();
        private bool _isDisposed;

        public SqliteMemoryStore(string? dbPath = null)
        {
            string resolvedPath = string.IsNullOrWhiteSpace(dbPath) ? AppPaths.GetDatabasePath() : dbPath;
            string? dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = resolvedPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            InitializeSchema();
            PruneOldEntries();
        }

        private void InitializeSchema()
        {
            lock (_lock)
            {
                using (var pragmaWal = _connection.CreateCommand())
                {
                    pragmaWal.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                    pragmaWal.ExecuteNonQuery();
                }

                // 1. Kiểm tra user_version hiện tại
                int currentVersion = 0;
                using (var verCmd = _connection.CreateCommand())
                {
                    verCmd.CommandText = "PRAGMA user_version;";
                    var res = verCmd.ExecuteScalar();
                    if (res != null && res != DBNull.Value)
                    {
                        currentVersion = Convert.ToInt32(res);
                    }
                }

                // 2. Kiểm tra xem bảng Translations đã tồn tại chưa
                bool tableExists = false;
                using (var checkCmd = _connection.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Translations';";
                    tableExists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;
                }

                if (!tableExists)
                {
                    CreateSchemaV2();
                    SetUserVersion(2);
                    return;
                }

                // 3. Kiểm tra xem bảng Translations có cột CacheKey chưa
                bool hasCacheKeyColumn = false;
                using (var colCmd = _connection.CreateCommand())
                {
                    colCmd.CommandText = "PRAGMA table_info(Translations);";
                    using var reader = colCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string colName = reader.GetString(1);
                        if (string.Equals(colName, "CacheKey", StringComparison.OrdinalIgnoreCase))
                        {
                            hasCacheKeyColumn = true;
                            break;
                        }
                    }
                }

                if (!hasCacheKeyColumn || currentVersion < 2)
                {
                    MigrateV1ToV2();
                    SetUserVersion(2);
                }
            }
        }

        private void CreateSchemaV2()
        {
            var statements = new[]
            {
                @"CREATE TABLE IF NOT EXISTS Translations (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CacheKey TEXT UNIQUE NOT NULL,
                    NormalizedText TEXT NOT NULL,
                    OriginalText TEXT,
                    Translation TEXT NOT NULL,
                    ProfileId TEXT DEFAULT 'global',
                    SourceLanguage TEXT DEFAULT 'auto',
                    TargetLanguage TEXT DEFAULT 'vi',
                    Speaker TEXT,
                    Provider TEXT DEFAULT 'Gemini',
                    Model TEXT,
                    EntryType INTEGER DEFAULT 0,
                    IsUserApproved INTEGER DEFAULT 0,
                    IsFallback INTEGER DEFAULT 0,
                    ReviewerSource TEXT,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    LastUsedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UseCount INTEGER DEFAULT 1,
                    Confidence REAL DEFAULT 1.0
                );",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_translations_cachekey ON Translations (CacheKey);",
                "CREATE INDEX IF NOT EXISTS idx_translations_lookup ON Translations (NormalizedText, ProfileId, SourceLanguage, TargetLanguage);",
                "CREATE INDEX IF NOT EXISTS idx_translations_profile ON Translations (ProfileId);",
                @"CREATE TABLE IF NOT EXISTS TranslationVariants (
                    VariantKey TEXT PRIMARY KEY,
                    VariantText TEXT NOT NULL,
                    CanonicalNormalizedText TEXT NOT NULL,
                    ProfileId TEXT DEFAULT 'global',
                    SourceLanguage TEXT DEFAULT 'auto',
                    TargetLanguage TEXT DEFAULT 'vi',
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );",
                "CREATE INDEX IF NOT EXISTS idx_variants_canonical ON TranslationVariants (CanonicalNormalizedText, ProfileId);"
            };

            foreach (var sql in statements)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private void MigrateV1ToV2()
        {
            AppLogger.Info("[SQLITE_MIGRATION] Bắt đầu nâng cấp schema từ v1 lên v2 (Context-Aware Cache & Safe TM)...");
            using var transaction = _connection.BeginTransaction();
            try
            {
                // Bước 1: Sao lưu dữ liệu cũ
                using (var backupCmd = _connection.CreateCommand())
                {
                    backupCmd.Transaction = transaction;
                    backupCmd.CommandText = "CREATE TABLE IF NOT EXISTS Translations_Backup_v1 AS SELECT * FROM Translations;";
                    backupCmd.ExecuteNonQuery();
                }

                // Bước 2: Đổi tên bảng cũ
                using (var renameCmd = _connection.CreateCommand())
                {
                    renameCmd.Transaction = transaction;
                    renameCmd.CommandText = "ALTER TABLE Translations RENAME TO Translations_Old_v1;";
                    renameCmd.ExecuteNonQuery();
                }

                // Bước 3: Tạo bảng mới v2
                using (var createCmd = _connection.CreateCommand())
                {
                    createCmd.Transaction = transaction;
                    createCmd.CommandText = @"
                        CREATE TABLE Translations (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            CacheKey TEXT UNIQUE NOT NULL,
                            NormalizedText TEXT NOT NULL,
                            OriginalText TEXT,
                            Translation TEXT NOT NULL,
                            ProfileId TEXT DEFAULT 'global',
                            SourceLanguage TEXT DEFAULT 'auto',
                            TargetLanguage TEXT DEFAULT 'vi',
                            Speaker TEXT,
                            Provider TEXT DEFAULT 'Gemini',
                            Model TEXT,
                            EntryType INTEGER DEFAULT 0,
                            IsUserApproved INTEGER DEFAULT 0,
                            IsFallback INTEGER DEFAULT 0,
                            ReviewerSource TEXT,
                            CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                            LastUsedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                            UseCount INTEGER DEFAULT 1,
                            Confidence REAL DEFAULT 1.0
                        );";
                    createCmd.ExecuteNonQuery();
                }

                // Bước 4: Chuyển toàn bộ 100% dữ liệu cũ sang v2 với CacheKey tự sinh
                using (var copyCmd = _connection.CreateCommand())
                {
                    copyCmd.Transaction = transaction;
                    copyCmd.CommandText = @"
                        INSERT OR IGNORE INTO Translations (
                            NormalizedText, OriginalText, Translation, Speaker, Provider, Model,
                            CreatedAt, LastUsedAt, UseCount, Confidence,
                            CacheKey, ProfileId, SourceLanguage, TargetLanguage, EntryType, IsUserApproved, IsFallback
                        )
                        SELECT 
                            NormalizedText, OriginalText, Translation, Speaker, Provider, Model,
                            CreatedAt, LastUsedAt, UseCount, Confidence,
                            ('v1::global::auto->vi::' || NormalizedText), 'global', 'auto', 'vi', 0, 0, 0
                        FROM Translations_Old_v1;";
                    copyCmd.ExecuteNonQuery();
                }

                // Bước 5: Nâng cấp TranslationVariants nếu có
                bool variantsExist = false;
                using (var checkVarCmd = _connection.CreateCommand())
                {
                    checkVarCmd.Transaction = transaction;
                    checkVarCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TranslationVariants';";
                    variantsExist = Convert.ToInt32(checkVarCmd.ExecuteScalar()) > 0;
                }

                if (variantsExist)
                {
                    bool hasVariantKey = false;
                    using (var varColCmd = _connection.CreateCommand())
                    {
                        varColCmd.Transaction = transaction;
                        varColCmd.CommandText = "PRAGMA table_info(TranslationVariants);";
                        using var r = varColCmd.ExecuteReader();
                        while (r.Read())
                        {
                            if (string.Equals(r.GetString(1), "VariantKey", StringComparison.OrdinalIgnoreCase))
                            {
                                hasVariantKey = true;
                                break;
                            }
                        }
                    }

                    if (!hasVariantKey)
                    {
                        using var alterVarCmd = _connection.CreateCommand();
                        alterVarCmd.Transaction = transaction;
                        alterVarCmd.CommandText = @"
                            ALTER TABLE TranslationVariants RENAME TO TranslationVariants_Old_v1;
                            CREATE TABLE TranslationVariants (
                                VariantKey TEXT PRIMARY KEY,
                                VariantText TEXT NOT NULL,
                                CanonicalNormalizedText TEXT NOT NULL,
                                ProfileId TEXT DEFAULT 'global',
                                SourceLanguage TEXT DEFAULT 'auto',
                                TargetLanguage TEXT DEFAULT 'vi',
                                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                            );
                            INSERT OR IGNORE INTO TranslationVariants (
                                VariantKey, VariantText, CanonicalNormalizedText, ProfileId, SourceLanguage, TargetLanguage, CreatedAt
                            )
                            SELECT 
                                ('v1::global::auto->vi::' || VariantText),
                                VariantText,
                                CanonicalNormalizedText,
                                'global',
                                'auto',
                                'vi',
                                CreatedAt
                            FROM TranslationVariants_Old_v1;
                            DROP TABLE TranslationVariants_Old_v1;";
                        alterVarCmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    using var createVarCmd = _connection.CreateCommand();
                    createVarCmd.Transaction = transaction;
                    createVarCmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS TranslationVariants (
                            VariantKey TEXT PRIMARY KEY,
                            VariantText TEXT NOT NULL,
                            CanonicalNormalizedText TEXT NOT NULL,
                            ProfileId TEXT DEFAULT 'global',
                            SourceLanguage TEXT DEFAULT 'auto',
                            TargetLanguage TEXT DEFAULT 'vi',
                            CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                        );";
                    createVarCmd.ExecuteNonQuery();
                }

                // Bước 6: Dọn bảng tạm và tạo indices
                using (var cleanCmd = _connection.CreateCommand())
                {
                    cleanCmd.Transaction = transaction;
                    cleanCmd.CommandText = @"
                        DROP TABLE IF EXISTS Translations_Old_v1;
                        DROP TABLE IF EXISTS Translations_Backup_v1;
                        CREATE UNIQUE INDEX IF NOT EXISTS idx_translations_cachekey ON Translations (CacheKey);
                        CREATE INDEX IF NOT EXISTS idx_translations_lookup ON Translations (NormalizedText, ProfileId, SourceLanguage, TargetLanguage);
                        CREATE INDEX IF NOT EXISTS idx_translations_profile ON Translations (ProfileId);
                        CREATE INDEX IF NOT EXISTS idx_variants_canonical ON TranslationVariants (CanonicalNormalizedText, ProfileId);";
                    cleanCmd.ExecuteNonQuery();
                }

                transaction.Commit();
                AppLogger.Info("[SQLITE_MIGRATION] Nâng cấp Schema v2 thành công 100%, bảo toàn dữ liệu cũ.");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                AppLogger.Error($"[SQLITE_MIGRATION_ERROR] Nâng cấp v2 thất bại: {ex.Message}. Đã rollback.");
                throw;
            }
        }

        private void SetUserVersion(int version)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"PRAGMA user_version = {version};";
            cmd.ExecuteNonQuery();
        }

        public string? TryGet(string normalizedText)
        {
            return TryGet(normalizedText, "global");
        }

        public string? TryGet(string normalizedText, string profileId, string sourceLang = "auto", string targetLang = "vi")
        {
            var entry = TryGetEntry(normalizedText, profileId, sourceLang, targetLang, allowFallbackDegraded: false);
            return entry?.Translation;
        }

        public TranslationMemoryEntry? TryGetEntry(
            string normalizedText, 
            string profileId = "global", 
            string sourceLang = "auto", 
            string targetLang = "vi", 
            bool allowFallbackDegraded = false)
        {
            if (string.IsNullOrWhiteSpace(normalizedText)) return null;

            string cleanProfile = string.IsNullOrWhiteSpace(profileId) ? "global" : profileId.Trim().ToLowerInvariant();
            string cleanSource = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang.Trim().ToLowerInvariant();
            string cleanTarget = string.IsNullOrWhiteSpace(targetLang) ? "vi" : targetLang.Trim().ToLowerInvariant();

            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    string fallbackFilter = allowFallbackDegraded ? "" : "AND IsFallback = 0 AND EntryType != 2";
                    cmd.CommandText = $@"
                        SELECT CacheKey, NormalizedText, OriginalText, Translation, ProfileId, SourceLanguage, TargetLanguage,
                               Speaker, Provider, Model, EntryType, IsUserApproved, IsFallback, ReviewerSource,
                               CreatedAt, LastUsedAt, UseCount, Confidence
                        FROM Translations 
                        WHERE NormalizedText = @norm 
                          AND (ProfileId = @profile OR ProfileId = 'global')
                          AND (SourceLanguage = @source OR SourceLanguage = 'auto')
                          AND (TargetLanguage = @target OR TargetLanguage = 'vi')
                          {fallbackFilter}
                        ORDER BY 
                            (CASE WHEN ProfileId = @profile THEN 10 ELSE 0 END) + (CASE WHEN IsUserApproved = 1 THEN 5 ELSE 0 END) DESC,
                            (CASE WHEN EntryType = 2 THEN 1 ELSE 0 END) ASC,
                            UseCount DESC
                        LIMIT 1;
                    ";
                    cmd.Parameters.AddWithValue("@norm", normalizedText.Trim());
                    cmd.Parameters.AddWithValue("@profile", cleanProfile);
                    cmd.Parameters.AddWithValue("@source", cleanSource);
                    cmd.Parameters.AddWithValue("@target", cleanTarget);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        var entry = new TranslationMemoryEntry
                        {
                            NormalizedText = reader.GetString(1),
                            OriginalText = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            Translation = reader.GetString(3),
                            ProfileId = reader.GetString(4),
                            SourceLanguage = reader.GetString(5),
                            TargetLanguage = reader.GetString(6),
                            Speaker = reader.IsDBNull(7) ? null : reader.GetString(7),
                            Provider = reader.IsDBNull(8) ? "Gemini" : reader.GetString(8),
                            Model = reader.IsDBNull(9) ? null : reader.GetString(9),
                            EntryType = (CacheEntryType)reader.GetInt32(10),
                            IsUserApproved = reader.GetInt32(11) == 1,
                            IsFallback = reader.GetInt32(12) == 1,
                            ReviewerSource = reader.IsDBNull(13) ? null : reader.GetString(13),
                            CreatedAt = reader.IsDBNull(14) ? DateTime.UtcNow : reader.GetDateTime(14),
                            LastUsedAt = reader.IsDBNull(15) ? DateTime.UtcNow : reader.GetDateTime(15),
                            UseCount = reader.GetInt32(16),
                            Confidence = reader.IsDBNull(17) ? 1.0 : reader.GetDouble(17)
                        };

                        string matchedKey = reader.GetString(0);
                        reader.Close();

                        // Cập nhật lượt dùng
                        using var updateCmd = _connection.CreateCommand();
                        updateCmd.CommandText = "UPDATE Translations SET UseCount = UseCount + 1, LastUsedAt = CURRENT_TIMESTAMP WHERE CacheKey = @key;";
                        updateCmd.Parameters.AddWithValue("@key", matchedKey);
                        updateCmd.ExecuteNonQuery();

                        return entry;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[SQLITE_TRYGET_ERROR] {ex.Message}");
                }
                return null;
            }
        }

        public void Store(
            string normalizedText,
            string originalText,
            string translatedText,
            string? speaker = null,
            string provider = "Gemini",
            string? model = null)
        {
            var entry = new TranslationMemoryEntry
            {
                NormalizedText = normalizedText,
                OriginalText = originalText,
                Translation = translatedText,
                ProfileId = "global",
                SourceLanguage = "auto",
                TargetLanguage = "vi",
                Speaker = speaker,
                Provider = provider,
                Model = model,
                EntryType = CacheEntryType.ModelOutput,
                IsUserApproved = false,
                IsFallback = false
            };
            Store(entry);
        }

        public void Store(TranslationMemoryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.NormalizedText) || string.IsNullOrWhiteSpace(entry.Translation))
                return;

            string key = entry.GetCacheKey();
            string cleanProfile = string.IsNullOrWhiteSpace(entry.ProfileId) ? "global" : entry.ProfileId.Trim().ToLowerInvariant();
            string cleanSource = string.IsNullOrWhiteSpace(entry.SourceLanguage) ? "auto" : entry.SourceLanguage.Trim().ToLowerInvariant();
            string cleanTarget = string.IsNullOrWhiteSpace(entry.TargetLanguage) ? "vi" : entry.TargetLanguage.Trim().ToLowerInvariant();

            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO Translations (
                            CacheKey, NormalizedText, OriginalText, Translation,
                            ProfileId, SourceLanguage, TargetLanguage, Speaker,
                            Provider, Model, EntryType, IsUserApproved, IsFallback,
                            ReviewerSource, CreatedAt, LastUsedAt, UseCount, Confidence
                        )
                        VALUES (
                            @key, @norm, @orig, @trans,
                            @profile, @sourceLang, @targetLang, @speaker,
                            @provider, @model, @entryType, @approved, @fallback,
                            @reviewer, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, @useCount, @confidence
                        )
                        ON CONFLICT(CacheKey) DO UPDATE SET
                            Translation = CASE 
                                WHEN Translations.IsUserApproved = 1 AND excluded.IsUserApproved = 0 THEN Translations.Translation
                                ELSE excluded.Translation
                            END,
                            OriginalText = coalesce(excluded.OriginalText, Translations.OriginalText),
                            Speaker = coalesce(excluded.Speaker, Translations.Speaker),
                            Provider = CASE 
                                WHEN Translations.IsUserApproved = 1 AND excluded.IsUserApproved = 0 THEN Translations.Provider
                                ELSE excluded.Provider
                            END,
                            Model = CASE 
                                WHEN Translations.IsUserApproved = 1 AND excluded.IsUserApproved = 0 THEN Translations.Model
                                ELSE excluded.Model
                            END,
                            EntryType = CASE
                                WHEN Translations.IsUserApproved = 1 AND excluded.IsUserApproved = 0 THEN Translations.EntryType
                                ELSE excluded.EntryType
                            END,
                            IsUserApproved = CASE
                                WHEN Translations.IsUserApproved = 1 THEN 1
                                ELSE excluded.IsUserApproved
                            END,
                            IsFallback = CASE
                                WHEN Translations.IsUserApproved = 1 AND excluded.IsUserApproved = 0 THEN Translations.IsFallback
                                ELSE excluded.IsFallback
                            END,
                            ReviewerSource = coalesce(excluded.ReviewerSource, Translations.ReviewerSource),
                            LastUsedAt = CURRENT_TIMESTAMP,
                            UseCount = Translations.UseCount + 1,
                            Confidence = excluded.Confidence;
                    ";
                    cmd.Parameters.AddWithValue("@key", key);
                    cmd.Parameters.AddWithValue("@norm", entry.NormalizedText.Trim());
                    cmd.Parameters.AddWithValue("@orig", string.IsNullOrEmpty(entry.OriginalText) ? (object)DBNull.Value : entry.OriginalText);
                    cmd.Parameters.AddWithValue("@trans", entry.Translation);
                    cmd.Parameters.AddWithValue("@profile", cleanProfile);
                    cmd.Parameters.AddWithValue("@sourceLang", cleanSource);
                    cmd.Parameters.AddWithValue("@targetLang", cleanTarget);
                    cmd.Parameters.AddWithValue("@speaker", entry.Speaker ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@provider", entry.Provider ?? "Gemini");
                    cmd.Parameters.AddWithValue("@model", entry.Model ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@entryType", (int)entry.EntryType);
                    cmd.Parameters.AddWithValue("@approved", entry.IsUserApproved ? 1 : 0);
                    cmd.Parameters.AddWithValue("@fallback", entry.IsFallback ? 1 : 0);
                    cmd.Parameters.AddWithValue("@reviewer", entry.ReviewerSource ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@useCount", Math.Max(1, entry.UseCount));
                    cmd.Parameters.AddWithValue("@confidence", entry.Confidence);

                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[SQLITE_STORE_ERROR] {ex.Message}");
                }
            }
        }

        public bool TryGetFuzzy(string normalizedText, out string? canonicalText, out string? translation)
        {
            return TryGetFuzzy(normalizedText, "global", "auto", "vi", out canonicalText, out translation);
        }

        public bool TryGetFuzzy(
            string normalizedText, 
            string profileId, 
            string sourceLang, 
            string targetLang, 
            out string? canonicalText, 
            out string? translation)
        {
            canonicalText = null;
            translation = null;

            if (string.IsNullOrWhiteSpace(normalizedText)) return false;

            string cleanProfile = string.IsNullOrWhiteSpace(profileId) ? "global" : profileId.Trim().ToLowerInvariant();
            string cleanSource = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang.Trim().ToLowerInvariant();
            string cleanTarget = string.IsNullOrWhiteSpace(targetLang) ? "vi" : targetLang.Trim().ToLowerInvariant();

            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        SELECT v.CanonicalNormalizedText 
                        FROM TranslationVariants v 
                        WHERE v.VariantText = @variant 
                          AND (v.ProfileId = @profile OR v.ProfileId = 'global')
                          AND (v.SourceLanguage = @source OR v.SourceLanguage = 'auto')
                          AND (v.TargetLanguage = @target OR v.TargetLanguage = 'vi')
                        ORDER BY (CASE WHEN v.ProfileId = @profile THEN 1 ELSE 0 END) DESC
                        LIMIT 1;
                    ";
                    cmd.Parameters.AddWithValue("@variant", normalizedText.Trim());
                    cmd.Parameters.AddWithValue("@profile", cleanProfile);
                    cmd.Parameters.AddWithValue("@source", cleanSource);
                    cmd.Parameters.AddWithValue("@target", cleanTarget);

                    var canonicalResult = cmd.ExecuteScalar()?.ToString();
                    if (!string.IsNullOrEmpty(canonicalResult))
                    {
                        canonicalText = canonicalResult;
                        translation = TryGet(canonicalResult, cleanProfile, cleanSource, cleanTarget);
                        return !string.IsNullOrEmpty(translation);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[SQLITE_FUZZY_ERROR] {ex.Message}");
                }
                return false;
            }
        }

        public void StoreFuzzyMapping(string variantText, string canonicalText)
        {
            StoreFuzzyMapping(variantText, canonicalText, "global", "auto", "vi");
        }

        public void StoreFuzzyMapping(
            string variantText, 
            string canonicalText, 
            string profileId = "global", 
            string sourceLang = "auto", 
            string targetLang = "vi")
        {
            if (string.IsNullOrWhiteSpace(variantText) || string.IsNullOrWhiteSpace(canonicalText))
                return;

            string cleanProfile = string.IsNullOrWhiteSpace(profileId) ? "global" : profileId.Trim().ToLowerInvariant();
            string cleanSource = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang.Trim().ToLowerInvariant();
            string cleanTarget = string.IsNullOrWhiteSpace(targetLang) ? "vi" : targetLang.Trim().ToLowerInvariant();
            string key = TranslationCacheKey.BuildKey(variantText, cleanProfile, cleanSource, cleanTarget);

            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO TranslationVariants (
                            VariantKey, VariantText, CanonicalNormalizedText,
                            ProfileId, SourceLanguage, TargetLanguage, CreatedAt
                        )
                        VALUES (@key, @variant, @canonical, @profile, @source, @target, CURRENT_TIMESTAMP);
                    ";
                    cmd.Parameters.AddWithValue("@key", key);
                    cmd.Parameters.AddWithValue("@variant", variantText.Trim());
                    cmd.Parameters.AddWithValue("@canonical", canonicalText.Trim());
                    cmd.Parameters.AddWithValue("@profile", cleanProfile);
                    cmd.Parameters.AddWithValue("@source", cleanSource);
                    cmd.Parameters.AddWithValue("@target", cleanTarget);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[SQLITE_STORE_FUZZY_ERROR] {ex.Message}");
                }
            }
        }

        public int GetCount()
        {
            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM Translations;";
                    var result = cmd.ExecuteScalar();
                    return Convert.ToInt32(result);
                }
                catch
                {
                    return 0;
                }
            }
        }

        public int GetCountByProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId)) return 0;
            string cleanProfile = profileId.Trim().ToLowerInvariant();

            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM Translations WHERE ProfileId = @profile;";
                    cmd.Parameters.AddWithValue("@profile", cleanProfile);
                    var result = cmd.ExecuteScalar();
                    return Convert.ToInt32(result);
                }
                catch
                {
                    return 0;
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM TranslationVariants; DELETE FROM Translations;";
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[SQLITE_CLEAR_ERROR] {ex.Message}");
                }
            }
        }

        public void ClearByProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId)) return;
            string cleanProfile = profileId.Trim().ToLowerInvariant();

            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        DELETE FROM TranslationVariants WHERE ProfileId = @profile;
                        DELETE FROM Translations WHERE ProfileId = @profile;
                    ";
                    cmd.Parameters.AddWithValue("@profile", cleanProfile);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[SQLITE_CLEAR_BY_PROFILE_ERROR] {ex.Message}");
                }
            }
        }

        public void PruneOldEntries(int maxEntries = 50000)
        {
            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    // Bảo vệ tuyệt đối bản dịch người dùng duyệt: IsUserApproved = 0 mới bị xóa theo LRU
                    cmd.CommandText = @"
                        DELETE FROM Translations 
                        WHERE Id IN (
                            SELECT Id FROM Translations 
                            WHERE IsUserApproved = 0
                            ORDER BY LastUsedAt ASC 
                            LIMIT MAX(0, (SELECT COUNT(*) FROM Translations WHERE IsUserApproved = 0) - @maxEntries)
                        );
                        DELETE FROM TranslationVariants 
                        WHERE CanonicalNormalizedText NOT IN (SELECT NormalizedText FROM Translations);";
                    cmd.Parameters.AddWithValue("@maxEntries", maxEntries);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[SQLITE_PRUNE_ERROR] {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            lock (_lock)
            {
                try
                {
                    _connection.Close();
                    _connection.Dispose();
                }
                catch { }
            }
        }
    }
}
