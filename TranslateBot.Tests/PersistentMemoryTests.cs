using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Database;
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
    }
}
