using System;
using System.IO;

namespace TranslateBot.Infrastructure
{
    // App build dạng WinExe -> KHÔNG có cửa sổ Console, nên mọi Console.WriteLine
    // trước đây (kể cả các dòng [LỖI GEMINI] để debug) đều vô hình với người dùng.
    // AppLogger ghi thêm ra file thật cạnh file .exe để người dùng (và Claude khi
    // debug qua log) có thể thực sự đọc được lỗi gì đã xảy ra.
    public static class AppLogger
    {
        private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        private static readonly object Lock = new();

        public static void Error(string message)
        {
            Write("ERROR", message);
            Console.WriteLine(message); // Vẫn giữ lại cho trường hợp chạy qua `dotnet run`
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
            Console.WriteLine(message);
        }

        public static void Info(string message)
        {
            Write("INFO", message);
            Console.WriteLine(message);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(LogDirectory);
                    string path = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
                    string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(path, line);
                }
            }
            catch
            {
                // Không để việc ghi log làm crash app dịch thuật.
            }
        }
    }
}
