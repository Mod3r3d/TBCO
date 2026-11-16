using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TranslateBot.Infrastructure
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// Bộ ghi log ứng dụng bất đồng bộ (P1-4).
    /// - Không chặn I/O trên đường nóng (sử dụng Channel background writer).
    /// - Lưu file tại %LOCALAPPDATA%\TBCO\logs (không ghi vào thư mục cài đặt).
    /// - Hỗ trợ lọc theo LogLevel và giới hạn xoay vòng file (rolling 5MB/ngày).
    /// </summary>
    public static class AppLogger
    {
        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
        private static readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(2000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        private static readonly Task _writerTask;
        private static readonly CancellationTokenSource _cts = new();
        private static LogLevel _minLevel = LogLevel.Info;

        public static LogLevel MinimumLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        static AppLogger()
        {
            _writerTask = Task.Run(ProcessLogQueueAsync);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Flush();
        }

        public static void Debug(string message)
        {
            if (_minLevel <= LogLevel.Debug)
            {
                Enqueue("DEBUG", message);
            }
        }

        public static void Info(string message)
        {
            if (_minLevel <= LogLevel.Info)
            {
                Enqueue("INFO", message);
            }
        }

        public static void Warn(string message, Exception? ex = null)
        {
            if (_minLevel <= LogLevel.Warn)
            {
                string msg = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
                Enqueue("WARN", msg);
            }
        }

        public static void Error(string message, Exception? ex = null)
        {
            if (_minLevel <= LogLevel.Error)
            {
                string msg = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
                Enqueue("ERROR", msg);
            }
        }

        private static void Enqueue(string level, string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
            Console.WriteLine(line);
            _channel.Writer.TryWrite(line);
        }

        private static async Task ProcessLogQueueAsync()
        {
            var token = _cts.Token;
            try
            {
                while (await _channel.Reader.WaitToReadAsync(token))
                {
                    while (_channel.Reader.TryRead(out var line))
                    {
                        WriteToFile(line);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LOGGER_BACKGROUND_ERROR] {ex.Message}");
            }
        }

        private static void WriteToFile(string line)
        {
            try
            {
                string logDir = AppPaths.GetLogsDirectory();
                string basePath = Path.Combine(logDir, $"app-{DateTime.Now:yyyy-MM-dd}.log");

                // Rolling log nếu file vượt quá 5MB
                if (File.Exists(basePath))
                {
                    var fileInfo = new FileInfo(basePath);
                    if (fileInfo.Length > MaxFileSizeBytes)
                    {
                        string rollPath = Path.Combine(logDir, $"app-{DateTime.Now:yyyy-MM-dd}_{DateTime.Now:HHmmss}.log");
                        try
                        {
                            File.Move(basePath, rollPath, overwrite: true);
                        }
                        catch { }
                    }
                }

                using var stream = new FileStream(basePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
                writer.WriteLine(line);
            }
            catch
            {
                // Không để việc ghi log làm crash ứng dụng dịch thuật
            }
        }

        public static void Flush()
        {
            while (_channel.Reader.TryRead(out var line))
            {
                WriteToFile(line);
            }
        }
    }
}
