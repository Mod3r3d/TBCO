using System;
using System.IO;
using Microsoft.Data.Sqlite;
using TranslateBot.Infrastructure;

namespace TranslateBot.Database
{
    public class SqliteMemoryStore : IMemoryStore
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _connection;
        private readonly object _lock = new();
        private bool _isDisposed;

        public SqliteMemoryStore(string dbPath = "tbco_memory.db")
        {
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            InitializeSchema();
        }

        private void InitializeSchema()
        {
            lock (_lock)
            {
                var statements = new[]
                {
                    "PRAGMA journal_mode = WAL;",
                    "PRAGMA synchronous = NORMAL;",
                    @"CREATE TABLE IF NOT EXISTS Translations (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        NormalizedText TEXT UNIQUE NOT NULL,
                        OriginalText TEXT,
                        Translation TEXT NOT NULL,
                        Speaker TEXT,
                        Provider TEXT,
                        Model TEXT,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        LastUsedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        UseCount INTEGER DEFAULT 1,
                        Confidence REAL DEFAULT 1.0
                    );",
                    "CREATE INDEX IF NOT EXISTS idx_translations_normalized ON Translations (NormalizedText);",
                    @"CREATE TABLE IF NOT EXISTS TranslationVariants (
                        VariantText TEXT PRIMARY KEY,
                        CanonicalNormalizedText TEXT NOT NULL,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    );",
                    "CREATE INDEX IF NOT EXISTS idx_variants_canonical ON TranslationVariants (CanonicalNormalizedText);"
                };

                foreach (var sql in statements)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public string? TryGet(string normalizedText)
        {
            if (string.IsNullOrWhiteSpace(normalizedText)) return null;

            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT Translation FROM Translations WHERE NormalizedText = @text LIMIT 1;";
                    cmd.Parameters.AddWithValue("@text", normalizedText);

                    var result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        // Cập nhật lượt sử dụng ngầm
                        using var updateCmd = _connection.CreateCommand();
                        updateCmd.CommandText = "UPDATE Translations SET UseCount = UseCount + 1, LastUsedAt = CURRENT_TIMESTAMP WHERE NormalizedText = @text;";
                        updateCmd.Parameters.AddWithValue("@text", normalizedText);
                        updateCmd.ExecuteNonQuery();

                        return result.ToString();
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
            if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(translatedText))
                return;

            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO Translations (NormalizedText, OriginalText, Translation, Speaker, Provider, Model, LastUsedAt, UseCount)
                        VALUES (@norm, @orig, @trans, @speaker, @provider, @model, CURRENT_TIMESTAMP, 1)
                        ON CONFLICT(NormalizedText) DO UPDATE SET
                            Translation = excluded.Translation,
                            LastUsedAt = CURRENT_TIMESTAMP,
                            UseCount = Translations.UseCount + 1;
                    ";
                    cmd.Parameters.AddWithValue("@norm", normalizedText);
                    cmd.Parameters.AddWithValue("@orig", originalText ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@trans", translatedText);
                    cmd.Parameters.AddWithValue("@speaker", speaker ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@provider", provider ?? "Gemini");
                    cmd.Parameters.AddWithValue("@model", model ?? (object)DBNull.Value);

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
            canonicalText = null;
            translation = null;

            if (string.IsNullOrWhiteSpace(normalizedText)) return false;

            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        SELECT t.NormalizedText, t.Translation 
                        FROM TranslationVariants v 
                        JOIN Translations t ON v.CanonicalNormalizedText = t.NormalizedText 
                        WHERE v.VariantText = @variant 
                        LIMIT 1;
                    ";
                    cmd.Parameters.AddWithValue("@variant", normalizedText);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        canonicalText = reader.GetString(0);
                        translation = reader.GetString(1);
                        return true;
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
            if (string.IsNullOrWhiteSpace(variantText) || string.IsNullOrWhiteSpace(canonicalText))
                return;

            lock (_lock)
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO TranslationVariants (VariantText, CanonicalNormalizedText, CreatedAt)
                        VALUES (@variant, @canonical, CURRENT_TIMESTAMP);
                    ";
                    cmd.Parameters.AddWithValue("@variant", variantText);
                    cmd.Parameters.AddWithValue("@canonical", canonicalText);
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
