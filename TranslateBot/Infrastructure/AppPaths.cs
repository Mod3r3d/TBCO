using System;
using System.IO;

namespace TranslateBot.Infrastructure
{
    /// <summary>
    /// Quản lý tập trung các đường dẫn dữ liệu người dùng tại %LOCALAPPDATA%\TBCO (P0-7).
    /// Đảm bảo ứng dụng không ghi dữ liệu trực tiếp vào Program Files hoặc thư mục cài đặt thiếu quyền ghi.
    /// </summary>
    public static class AppPaths
    {
        private static string? _appDataDir;

        private static bool IsTestEnvironment()
        {
            try
            {
                string? entry = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
                if (entry != null && (entry.Contains("testhost", StringComparison.OrdinalIgnoreCase) || entry.Contains("test", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
                string friendly = AppDomain.CurrentDomain.FriendlyName;
                return friendly.Contains("testhost", StringComparison.OrdinalIgnoreCase) || friendly.Contains("test", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string AppDataDir
        {
            get
            {
                if (_appDataDir == null)
                {
                    if (IsTestEnvironment())
                    {
                        string testDir = Path.Combine(Path.GetTempPath(), "TBCO_TestRun");
                        Directory.CreateDirectory(testDir);
                        _appDataDir = testDir;
                        return _appDataDir;
                    }

                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    _appDataDir = Path.Combine(localAppData, "TBCO");
                    try
                    {
                        if (!Directory.Exists(_appDataDir))
                        {
                            Directory.CreateDirectory(_appDataDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"[APPDATA_CREATE_ERROR] Không thể tạo thư mục LocalAppData: {ex.Message}");
                        _appDataDir = AppDomain.CurrentDomain.BaseDirectory;
                    }
                }
                return _appDataDir;
            }
            set => _appDataDir = value; // Cho phép override trong unit tests hoặc portable mode
        }

        public static string GetConfigPath()
        {
            string appDataConfig = Path.Combine(AppDataDir, "config.json");
            string legacyConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            // Không thực hiện copy từ thư mục build trong môi trường test
            if (IsTestEnvironment())
            {
                return appDataConfig;
            }

            // Migration một lần: nếu có config cũ ở thư mục app mà LocalAppData chưa có
            if (!File.Exists(appDataConfig) && File.Exists(legacyConfig))
            {
                try
                {
                    File.Copy(legacyConfig, appDataConfig, overwrite: false);
                    AppLogger.Info("[CONFIG_MIGRATION] Đã chuyển file cấu hình config.json sang %LOCALAPPDATA%\\TBCO.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[CONFIG_MIGRATION_WARN] Không thể copy config cũ: {ex.Message}");
                }
            }

            return appDataConfig;
        }

        public static string GetDatabasePath()
        {
            string appDataDb = Path.Combine(AppDataDir, "tbco_memory.db");
            string legacyDb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tbco_memory.db");

            // Migration một lần: nếu có db cũ ở thư mục app mà LocalAppData chưa có
            if (!File.Exists(appDataDb) && File.Exists(legacyDb))
            {
                try
                {
                    File.Copy(legacyDb, appDataDb, overwrite: false);
                    AppLogger.Info("[DB_MIGRATION] Đã chuyển cơ sở dữ liệu tbco_memory.db sang %LOCALAPPDATA%\\TBCO.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[DB_MIGRATION_WARN] Không thể copy db cũ: {ex.Message}");
                }
            }

            return appDataDb;
        }

        public static string GetSessionsDirectory()
        {
            string dir = Path.Combine(AppDataDir, "sessions");
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[SESSIONS_DIR_ERROR] Không thể tạo thư mục sessions: {ex.Message}");
            }
            return dir;
        }
    }
}
