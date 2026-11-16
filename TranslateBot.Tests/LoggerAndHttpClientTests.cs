using System;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Infrastructure;
using TranslateBot.Translation;
using TranslateBot.Translation.Providers;

namespace TranslateBot.Tests
{
    [TestClass]
    public class LoggerAndHttpClientTests
    {
        [TestMethod]
        public void SharedHttpClient_Instance_ReturnsSameReusableInstance()
        {
            var client1 = SharedHttpClient.Instance;
            var client2 = SharedHttpClient.Instance;

            Assert.IsNotNull(client1);
            Assert.AreSame(client1, client2, "SharedHttpClient phải trả về cùng một singleton instance để tái sử dụng connection pool");
        }

        private static string ReadLogFileSafely(string path)
        {
            if (!File.Exists(path)) return string.Empty;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }

        [TestMethod]
        public void AppLogger_WritesToAppDataDirectory_AndFlushesCorrectly()
        {
            string logDir = AppPaths.GetLogsDirectory();
            Assert.IsTrue(Directory.Exists(logDir), "Thư mục logs trong LocalAppData phải tồn tại");

            string testMessage = $"[TEST_LOG_ENTRY_{Guid.NewGuid():N}] Unit test execution";
            AppLogger.Info(testMessage);
            AppLogger.Flush();

            string todayLog = Path.Combine(logDir, $"app-{DateTime.Now:yyyy-MM-dd}.log");
            Assert.IsTrue(File.Exists(todayLog), "File log ngày hôm nay phải tồn tại");

            string content = ReadLogFileSafely(todayLog);
            Assert.IsTrue(content.Contains(testMessage), "File log phải chứa thông điệp vừa được ghi");
        }

        [TestMethod]
        public void AppLogger_LogLevelFilter_FiltersDebugWhenMinLevelIsInfo()
        {
            AppLogger.MinimumLevel = LogLevel.Info;
            string debugMsg = $"[DEBUG_TEST_{Guid.NewGuid():N}] Should not be logged";
            AppLogger.Debug(debugMsg);
            AppLogger.Flush();

            string todayLog = Path.Combine(AppPaths.GetLogsDirectory(), $"app-{DateTime.Now:yyyy-MM-dd}.log");
            string content = ReadLogFileSafely(todayLog);
            Assert.IsFalse(content.Contains(debugMsg), "Debug message không được ghi vào file khi MinimumLevel là Info");
        }

        [TestMethod]
        public void TranslationRouter_Dispose_DisposesChildProvidersSafely()
        {
            var keyPool = new ApiKeyPool();
            var gemini = new GeminiProvider();
            var deepL = new DeepLProvider("test_key");
            var custom = new CustomApiTranslationProvider();
            var ollama = new OllamaTranslationProvider();
            var web = new GoogleWebTranslateProvider();

            var router = new TranslationRouter(keyPool, gemini, web)
            {
                DeepLProvider = deepL,
                CustomApiProvider = custom,
                LocalAiProvider = ollama
            };

            // Gọi Dispose không được ném ngoại lệ
            router.Dispose();
        }
    }
}
