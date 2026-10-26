using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Database;
using TranslateBot.Dialogue;
using TranslateBot.Translation;

namespace TranslateBot.Tests
{
    [TestClass]
    public class PersistentMemoryTests
    {
        private string _testDbPath = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"tbco_test_mem_{Guid.NewGuid():N}.db");
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }
                var walFile = _testDbPath + "-wal";
                if (File.Exists(walFile)) File.Delete(walFile);
                var shmFile = _testDbPath + "-shm";
                if (File.Exists(shmFile)) File.Delete(shmFile);
            }
            catch
            {
                // Ignore cleanup errors on temp files
            }
        }

        [TestMethod]
        public void PersistentMemory_WritesToL1AndL2_AndSurvivesRestart()
        {
            // Instance 1: Store translation
            using (var store1 = new SqliteMemoryStore(_testDbPath))
            using (var mem1 = new TranslationMemory(maxEntries: 100, persistentStore: store1))
            {
                mem1.StoreTranslation("Senpai, are you ready?", "Tiền bối đã sẵn sàng chưa?", "Mash");
                var result1 = mem1.TryGetTranslation("Senpai, are you ready?");
                Assert.AreEqual("Tiền bối đã sẵn sàng chưa?", result1);
                Assert.AreEqual(1, mem1.L1EntryCount);
                Assert.AreEqual(1, mem1.TotalPersistentCount);
            }

            // Instance 2: Fresh instance with same database file
            using (var store2 = new SqliteMemoryStore(_testDbPath))
            using (var mem2 = new TranslationMemory(maxEntries: 100, persistentStore: store2))
            {
                // L1 should initially be empty
                Assert.AreEqual(0, mem2.L1EntryCount);
                Assert.AreEqual(1, mem2.TotalPersistentCount);

                // TryGet should fetch from SQLite L2 and hydrate L1
                var result2 = mem2.TryGetTranslation("Senpai, are you ready?");
                Assert.AreEqual("Tiền bối đã sẵn sàng chưa?", result2);
                Assert.AreEqual(1, mem2.L1EntryCount, "Sau khi hit L2, kết quả phải được nạp vào L1");
                Assert.AreEqual(1, mem2.HitCount);
            }
        }

        [TestMethod]
        public void PersistentMemory_StoresAndResolvesFuzzyVariants_AcrossRestarts()
        {
            string canonical = "Senpai, daijoubu desu ka?";
            string variantGlitch = "Senpal, daijoubu desu ka?";
            string translation = "Tiền bối, anh có sao không?";

            using (var store1 = new SqliteMemoryStore(_testDbPath))
            using (var mem1 = new TranslationMemory(maxEntries: 100, persistentStore: store1))
            {
                mem1.StoreTranslation(canonical, translation, "Mash");
                mem1.StoreVariant(variantGlitch, canonical);
            }

            using (var store2 = new SqliteMemoryStore(_testDbPath))
            using (var mem2 = new TranslationMemory(maxEntries: 100, persistentStore: store2))
            {
                // Truy vấn bằng chuỗi OCR lỗi variantGlitch
                var result = mem2.TryGetTranslation(variantGlitch);
                Assert.AreEqual(translation, result, "Truy vấn bằng biến thể fuzzy phải lấy được bản dịch gốc");
            }
        }

        [TestMethod]
        public void PersistentMemory_L1EvictionMaintainsLimit_WhileL2RetainsAll()
        {
            using (var store = new SqliteMemoryStore(_testDbPath))
            using (var mem = new TranslationMemory(maxEntries: 3, persistentStore: store))
            {
                mem.StoreTranslation("Text 1", "Dịch 1");
                mem.StoreTranslation("Text 2", "Dịch 2");
                mem.StoreTranslation("Text 3", "Dịch 3");
                mem.StoreTranslation("Text 4", "Dịch 4");
                mem.StoreTranslation("Text 5", "Dịch 5");

                Assert.AreEqual(3, mem.L1EntryCount, "L1 chỉ được giữ tối đa 3 entry theo LRU");
                Assert.AreEqual(5, mem.TotalPersistentCount, "L2 SQLite phải lưu toàn bộ 5 entry");

                // Text 1 đã bị đẩy ra khỏi L1, nhưng khi truy vấn vẫn thành công nhờ L2
                var res1 = mem.TryGetTranslation("Text 1");
                Assert.AreEqual("Dịch 1", res1);
            }
        }

        [TestMethod]
        public void PersistentMemory_MigrationV1ToV2_PreservesAllDataAndAllowsSameTextDifferentProfiles()
        {
            // 1. Khởi tạo một SQLite database cũ chuẩn schema v1
            string connStr = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = _testDbPath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate
            }.ToString();

            using (var rawConn = new Microsoft.Data.Sqlite.SqliteConnection(connStr))
            {
                rawConn.Open();
                using var cmd = rawConn.CreateCommand();
                cmd.CommandText = @"
                    PRAGMA user_version = 0;
                    CREATE TABLE Translations (
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
                    );
                    INSERT INTO Translations (NormalizedText, OriginalText, Translation, Speaker, Provider)
                    VALUES ('Hello world', 'Hello world', 'Xin chào thế giới', 'Narrator', 'Gemini');
                    INSERT INTO Translations (NormalizedText, OriginalText, Translation, Speaker, Provider)
                    VALUES ('Good morning', 'Good morning', 'Chào buổi sáng', 'Mash', 'Gemini');
                ";
                cmd.ExecuteNonQuery();
                rawConn.Close();
            }

            // 2. Mở bằng SqliteMemoryStore: tự động kích hoạt migration sang Schema v2 an toàn
            using (var store = new SqliteMemoryStore(_testDbPath))
            using (var mem = new TranslationMemory(maxEntries: 100, persistentStore: store))
            {
                // 3. Xác minh 100% dữ liệu cũ được giữ nguyên vẹn
                var res1 = mem.TryGetTranslation("Hello world");
                Assert.AreEqual("Xin chào thế giới", res1, "Dữ liệu cũ từ v1 phải được bảo toàn 100%");

                var res2 = mem.TryGetTranslation("Good morning");
                Assert.AreEqual("Chào buổi sáng", res2, "Dữ liệu cũ từ v1 phải được bảo toàn 100%");

                Assert.AreEqual(2, mem.TotalPersistentCount);

                // 4. Ở Schema v1, nếu insert cùng NormalizedText nhưng cho profile khác thì sẽ xung đột UNIQUE.
                // Ở Schema v2, thao tác này thành công hoàn toàn nhờ CacheKey phân theo Profile!
                mem.StoreTranslation("Hello world", "Chào toàn thể thế giới (FGO Edition)", speaker: "Ritsuka", profileId: "fgo");

                var fgoRes = mem.TryGetTranslation("Hello world", profileId: "fgo");
                Assert.AreEqual("Chào toàn thể thế giới (FGO Edition)", fgoRes);

                var globalRes = mem.TryGetTranslation("Hello world", profileId: "global");
                Assert.AreEqual("Xin chào thế giới", globalRes);
            }
        }

        [TestMethod]
        public void PersistentMemory_ProfileIsolation_PreventsCrossGameCachePollution()
        {
            using (var store = new SqliteMemoryStore(_testDbPath))
            using (var mem = new TranslationMemory(maxEntries: 100, persistentStore: store))
            {
                // FGO: "Ready?" -> "Master, sẵn sàng chưa?"
                mem.StoreTranslation("Ready?", "Master, sẵn sàng chưa?", speaker: "Mash", profileId: "fgo-jp");

                // Visual Novel: "Ready?" -> "Cậu đã chuẩn bị xong chưa?"
                mem.StoreTranslation("Ready?", "Cậu đã chuẩn bị xong chưa?", speaker: "Heroine", profileId: "vn-generic");

                // Global fallback: "Attack!" -> "Tấn công!"
                mem.StoreTranslation("Attack!", "Tấn công!", profileId: "global");

                // 1. FGO query nhận đúng bản dịch FGO
                Assert.AreEqual("Master, sẵn sàng chưa?", mem.TryGetTranslation("Ready?", profileId: "fgo-jp"));

                // 2. VN query nhận đúng bản dịch VN
                Assert.AreEqual("Cậu đã chuẩn bị xong chưa?", mem.TryGetTranslation("Ready?", profileId: "vn-generic"));

                // 3. Honkai query cho câu "Ready?" (chưa có trong honkai và không có trong global) -> null
                Assert.IsNull(mem.TryGetTranslation("Ready?", profileId: "honkai-star-rail"));

                // 4. FGO query cho "Attack!" (chỉ có trong global) -> fallback lấy từ global
                Assert.AreEqual("Tấn công!", mem.TryGetTranslation("Attack!", profileId: "fgo-jp"));
            }
        }

        [TestMethod]
        public void PersistentMemory_UserApproved_HasPriorityAndCannotBeOverwrittenByUnapprovedModel()
        {
            using (var store = new SqliteMemoryStore(_testDbPath))
            using (var mem = new TranslationMemory(maxEntries: 100, persistentStore: store))
            {
                // 1. Model output translation
                mem.Store(new TranslationMemoryEntry
                {
                    NormalizedText = "Let us proceed.",
                    Translation = "Chúng ta hãy tiếp tục.",
                    ProfileId = "fgo",
                    EntryType = CacheEntryType.ModelOutput,
                    IsUserApproved = false
                });

                Assert.AreEqual("Chúng ta hãy tiếp tục.", mem.TryGetTranslation("Let us proceed.", profileId: "fgo"));

                // 2. User reviews and approves a better translation
                mem.Store(new TranslationMemoryEntry
                {
                    NormalizedText = "Let us proceed.",
                    Translation = "Ta cùng xuất phát thôi, Master.",
                    ProfileId = "fgo",
                    EntryType = CacheEntryType.UserApproved,
                    IsUserApproved = true,
                    ReviewerSource = "UserManualReview"
                });

                Assert.AreEqual("Ta cùng xuất phát thôi, Master.", mem.TryGetTranslation("Let us proceed.", profileId: "fgo"));

                // 3. Model output mới xuất hiện thử đè câu này
                mem.Store(new TranslationMemoryEntry
                {
                    NormalizedText = "Let us proceed.",
                    Translation = "Cùng tiến bước nào.",
                    ProfileId = "fgo",
                    EntryType = CacheEntryType.ModelOutput,
                    IsUserApproved = false
                });

                // Bản dịch đã duyệt phải được bảo vệ nguyên vẹn
                Assert.AreEqual("Ta cùng xuất phát thôi, Master.", mem.TryGetTranslation("Let us proceed.", profileId: "fgo"));
            }
        }

        [TestMethod]
        public void PersistentMemory_FallbackDegraded_IsBlockedFromCanonicalCacheHit()
        {
            using (var store = new SqliteMemoryStore(_testDbPath))
            using (var mem = new TranslationMemory(maxEntries: 100, persistentStore: store))
            {
                // Lưu bản dịch fallback chất lượng thấp (VD: Google Web thô khi primary gặp sự cố)
                store.Store(new TranslationMemoryEntry
                {
                    NormalizedText = "Mystic Eyes of Death Perception",
                    Translation = "Mắt huyền bí của cái chết nhận thức",
                    ProfileId = "tsukihime",
                    Provider = "GoogleWebTranslate",
                    EntryType = CacheEntryType.FallbackDegraded,
                    IsFallback = true
                });

                // 1. TryGet thông thường phải CHẶN bản dịch degraded này để primary provider dịch lại khi khả dụng
                var canonicalHit = mem.TryGetTranslation("Mystic Eyes of Death Perception", profileId: "tsukihime");
                Assert.IsNull(canonicalHit, "Bản dịch FallbackDegraded không được phép trở thành canonical cache hit khi primary provider khả dụng");

                // 2. Nếu chủ động cho phép fallback (allowFallbackDegraded = true)
                var fallbackAllowed = store.TryGetEntry("Mystic Eyes of Death Perception", profileId: "tsukihime", allowFallbackDegraded: true);
                Assert.IsNotNull(fallbackAllowed);
                Assert.AreEqual("Mắt huyền bí của cái chết nhận thức", fallbackAllowed.Translation);
            }
        }

        [TestMethod]
        public void PersistentMemory_FuzzyMatch_IsIsolatedByProfile()
        {
            using (var store = new SqliteMemoryStore(_testDbPath))
            using (var mem = new TranslationMemory(maxEntries: 100, persistentStore: store))
            {
                string canonicalFgo = "Senpai, ikimashou!";
                string variantFgo = "Senpal, ikimashou!";
                string transFgo = "Tiền bối, chúng ta đi thôi!";

                mem.StoreTranslation(canonicalFgo, transFgo, speaker: "Mash", profileId: "fgo");
                mem.StoreVariant(variantFgo, canonicalFgo, profileId: "fgo");

                // Truy vấn variant trong profile fgo -> hit thành công
                var fgoHit = mem.TryGetTranslation(variantFgo, profileId: "fgo");
                Assert.AreEqual(transFgo, fgoHit);

                // Truy vấn variant trong profile khác (genshin) -> không được match chéo
                var genshinHit = mem.TryGetTranslation(variantFgo, profileId: "genshin");
                Assert.IsNull(genshinHit, "Fuzzy mapping không được phép match chéo giữa các Game Profile khác nhau");
            }
        }

        [TestMethod]
        public void PersistentMemory_FuzzyMatch_TerminalPunctuationDistinguishesQuestionAndStatement()
        {
            // Câu nghi vấn vs khẳng định: không thể là OCR correction nhầm dấu câu quan trọng này
            Assert.IsFalse(TextSimilarity.IsLikelyOcrCorrection("Are you ready?", "Are you ready."));
            Assert.IsFalse(TextSimilarity.IsLikelyOcrCorrection("Ready!", "Ready?"));
            Assert.IsFalse(TextSimilarity.IsLikelyOcrCorrection("Thật vậy sao?", "Thật vậy sao!"));

            // Cùng câu nghi vấn:
            Assert.IsTrue(TextSimilarity.IsLikelyOcrCorrection("Are you ready?", "Are you ready?"));
        }

        [TestMethod]
        public void PersistentMemory_ClearByProfile_OnlyClearsTargetProfile()
        {
            using (var store = new SqliteMemoryStore(_testDbPath))
            using (var mem = new TranslationMemory(maxEntries: 100, persistentStore: store))
            {
                mem.StoreTranslation("FGO 1", "Dịch FGO 1", profileId: "fgo");
                mem.StoreTranslation("FGO 2", "Dịch FGO 2", profileId: "fgo");

                mem.StoreTranslation("Genshin 1", "Dịch Genshin 1", profileId: "genshin");
                mem.StoreTranslation("Genshin 2", "Dịch Genshin 2", profileId: "genshin");
                mem.StoreTranslation("Genshin 3", "Dịch Genshin 3", profileId: "genshin");

                Assert.AreEqual(2, mem.GetCountByProfile("fgo"));
                Assert.AreEqual(3, mem.GetCountByProfile("genshin"));
                Assert.AreEqual(5, mem.TotalPersistentCount);

                // Xóa riêng profile fgo
                mem.ClearByProfile("fgo");

                Assert.AreEqual(0, mem.GetCountByProfile("fgo"));
                Assert.AreEqual(3, mem.GetCountByProfile("genshin"));
                Assert.AreEqual(3, mem.TotalPersistentCount);

                // L1 cache cũng bị xóa đối với fgo
                Assert.IsNull(mem.TryGetTranslation("FGO 1", profileId: "fgo"));
                // Genshin vẫn còn nguyên cả L1 lẫn L2
                Assert.AreEqual("Dịch Genshin 1", mem.TryGetTranslation("Genshin 1", profileId: "genshin"));
            }
        }
    }
}
