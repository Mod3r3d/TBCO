using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TranslateBot.Infrastructure;

namespace TranslateBot.OCR
{
    // ═══════════════════════════════════════════════════════════════════════
    // OneOCR Engine — engine OCR neural từ Microsoft, chính xác hơn Windows
    // OCR cũ với chữ nhỏ/nghiêng/có viền.
    //
    // Cần 3 file: oneocr.dll, oneocr.onemodel, onnxruntime.dll
    // Lấy từ Snipping Tool hoặc Photos trên Windows 11 bản mới.
    //
    // P/Invoke theo đúng API mà MORT implement (github.com/killkimno/MORT).
    // ═══════════════════════════════════════════════════════════════════════

    // ── Native structs ─────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    internal struct OneOcrImg
    {
        public int t;          // type flag (1 = image data)
        public int col;        // width
        public int row;        // height
        public int _unk;       // unknown/padding
        public long step;      // stride (bytes per row)
        public IntPtr data_ptr; // pointer to pixel data
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OneOcrBoundingBox
    {
        public float x1, y1;
        public float x2, y2;
        public float x3, y3;
        public float x4, y4;
    }

    // ── Native P/Invoke ────────────────────────────────────────────────────
    internal static class OneOcrNativeMethods
    {
        // QUAN TRỌNG: chỉ dùng TÊN FILE TRẦN (không có "DLL\" ở trước). Windows chỉ áp dụng
        // đường dẫn tìm kiếm được thêm qua SetDllDirectory() cho các tên module KHÔNG chứa
        // dấu phân cách thư mục - nếu để "DLL\oneocr.dll", Windows sẽ coi đây là một đường
        // dẫn tương đối theo THƯ MỤC LÀM VIỆC HIỆN TẠI của process (current working
        // directory), bỏ qua hoàn toàn SetDllDirectory(). Với ứng dụng double-click từ
        // Explorer, CWD thường trùng thư mục exe nên có vẻ "hoạt động", nhưng chạy qua
        // shortcut có "Start in" khác, Task Scheduler, hay launcher khác thì sẽ luôn thất bại
        // một cách âm thầm (bị catch, log lỗi, rồi fallback về Windows OCR - không crash,
        // nhưng OneOCR không bao giờ thực sự chạy được).
        private const string DllName = "oneocr.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long CreateOcrInitOptions(out long ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long OcrInitOptionsSetUseModelDelayLoad(long ctx, byte flag);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long CreateOcrPipeline(string modelPath, string key, long ctx, out long pipeline);

        [DllImport(DllName, EntryPoint = "CreateOcrPipeline", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern long CreateOcrPipeline_Utf16(string modelPath, string key, long ctx, out long pipeline);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long CreateOcrProcessOptions(out long opt);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long OcrProcessOptionsSetMaxRecognitionLineCount(long opt, long count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long RunOcrPipeline(long pipeline, ref OneOcrImg img, long opt, out long instance);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long GetOcrLineCount(long instance, out long count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long GetOcrLine(long instance, long index, out long line);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long GetOcrLineContent(long line, out IntPtr content);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long GetOcrLineBoundingBox(long line, out IntPtr boundingBoxPtr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long GetOcrLineWordCount(long instance, out long count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long GetOcrWord(long instance, long index, out long word);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long GetOcrWordContent(long word, out IntPtr content);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long GetOcrWordBoundingBox(long word, out IntPtr boundingBoxPtr);
    }

    // ── Engine ─────────────────────────────────────────────────────────────
    public class OneOcrEngine : IOcrEngine, IDisposable
    {
        private bool _disposed;
        private bool _initialized;
        private long _pipeline;
        private long _opt;
        private bool _isAvailable;

        // Cho phép code bên ngoài (VD: OcrEngineWithFallback) biết được OneOCR có thực sự
        // dùng được hay không SAU KHI đã thử init lần đầu (init là lazy + async, không throw
        // exception khi thất bại, nên không thể biết qua try/catch ở nơi gọi constructor).
        public bool HasAttemptedInit => _initialized;
        public bool IsAvailable => _isAvailable;

        private readonly string _dllDirectory;
        private static readonly string[] RequiredFiles = { "oneocr.dll", "oneocr.onemodel", "onnxruntime.dll" };

        // Model encryption key — giống MORT
        private const string ModelKey = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public OneOcrEngine()
        {
            _dllDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DLL");
        }

        // ═══ IOcrEngine ════════════════════════════════════════════════════

        public async Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height)
        {
            if (bgraPixels == null || bgraPixels.Length == 0)
                return OcrResult.Empty;

            // Lazy init — tránh crash constructor
            if (!_initialized)
            {
                await InitializeAsync();
            }

            if (!_isAvailable)
                return OcrResult.Empty;

            // OneOCR là synchronous → chạy trên thread pool
            return await Task.Run(() => RunOcrOnPixels(bgraPixels, width, height));
        }

        // ═══ Initialization ═══════════════════════════════════════════════

        private async Task InitializeAsync()
        {
            if (_initialized) return;
            _initialized = true; // đánh dấu đã thử init, dù thành công hay thất bại

            try
            {
                AppLogger.Info("[ONEOCR] Bắt đầu khởi tạo...");

                // Bước 1: Tìm và copy DLL nếu chưa có
                await EnsureDllsAvailableAsync();

                // Kiểm tra lại sau khi copy
                if (!RequiredFiles.All(f => File.Exists(Path.Combine(_dllDirectory, f))))
                {
                    AppLogger.Warn("[ONEOCR] Thiếu file DLL. OneOCR không khả dụng.");
                    _isAvailable = false;
                    return;
                }

                // Bước 2: Set DLL search directory
                SetDllDirectory(_dllDirectory);

                // Bước 3: Tạo pipeline
                string modelPath = Path.GetFullPath(Path.Combine(_dllDirectory, "oneocr.onemodel"));
                AppLogger.Info($"[ONEOCR] Model path: {modelPath}");

                long ctx = 0;
                // CreateOcrInitOptions (optional, có thể skip nếu ctx = 0)
                try
                {
                    OneOcrNativeMethods.CreateOcrInitOptions(out ctx);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[ONEOCR] CreateOcrInitOptions exception (non-fatal): {ex.Message}");
                    ctx = 0;
                }

                if (!TryCreatePipeline(modelPath, ModelKey, ctx, out _pipeline, out string details))
                {
                    AppLogger.Error($"[ONEOCR] Không thể tạo pipeline: {details}");
                    _isAvailable = false;
                    return;
                }

                // Bước 4: Tạo process options
                long res = OneOcrNativeMethods.CreateOcrProcessOptions(out _opt);
                if (res != 0)
                {
                    AppLogger.Error($"[ONEOCR] CreateOcrProcessOptions failed: {res}");
                    _isAvailable = false;
                    return;
                }

                res = OneOcrNativeMethods.OcrProcessOptionsSetMaxRecognitionLineCount(_opt, 1000);
                if (res != 0)
                {
                    AppLogger.Error($"[ONEOCR] SetMaxRecognitionLineCount failed: {res}");
                    _isAvailable = false;
                    return;
                }

                _isAvailable = true;
                AppLogger.Info("[ONEOCR] Khởi tạo thành công!");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[ONEOCR] Lỗi khởi tạo: {ex.Message}");
                _isAvailable = false;
            }
        }

        // ═══ Pipeline Creation (Fallback giống MORT) ═══════════════════════

        private bool TryCreatePipeline(string modelPath, string key, long ctx, out long pipeline, out string details)
        {
            pipeline = 0;
            details = "";

            // 1) UTF-8 (default marshaling)
            try
            {
                long r = OneOcrNativeMethods.CreateOcrPipeline(modelPath, key, ctx, out pipeline);
                details += $"UTF8={r}";
                if (r == 0) return true;
            }
            catch (Exception ex)
            {
                details += $"UTF8 ex: {ex.GetType().Name}:{ex.Message}";
            }

            // 2) UTF-16
            try
            {
                long r = OneOcrNativeMethods.CreateOcrPipeline_Utf16(modelPath, key, ctx, out pipeline);
                details += $" | UTF16={r}";
                if (r == 0) return true;
            }
            catch (Exception ex)
            {
                details += $" | UTF16 ex: {ex.GetType().Name}:{ex.Message}";
            }

            // 3) Copy model to ASCII-only temp path (workaround for Unicode paths)
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "oneocr_model");
                Directory.CreateDirectory(tempDir);
                string tmpFile = Path.Combine(tempDir, "oneocr_model_" + Guid.NewGuid().ToString("N") + ".onemodel");
                File.Copy(modelPath, tmpFile, true);

                try
                {
                    long r = OneOcrNativeMethods.CreateOcrPipeline(tmpFile, key, ctx, out pipeline);
                    details += $" | TempUTF8={r}";
                    if (r == 0) return true;
                }
                catch (Exception ex)
                {
                    details += $" | TempUTF8 ex: {ex.GetType().Name}:{ex.Message}";
                }

                try
                {
                    long r = OneOcrNativeMethods.CreateOcrPipeline_Utf16(tmpFile, key, ctx, out pipeline);
                    details += $" | TempUTF16={r}";
                    if (r == 0) return true;
                }
                catch (Exception ex)
                {
                    details += $" | TempUTF16 ex: {ex.GetType().Name}:{ex.Message}";
                }
            }
            catch (Exception ex)
            {
                details += $" | Copy failed: {ex.GetType().Name}:{ex.Message}";
            }

            return false;
        }

        // ═══ OCR Execution ═════════════════════════════════════════════════

        private OcrResult RunOcrOnPixels(byte[] bgraPixels, int width, int height)
        {
            Bitmap? bitmap = null;
            BitmapData? bitmapData = null;

            try
            {
                // OneOCR cần Format24bppRgb (3 channels), không phải BGRA (4 channels)
                // Chuyển đổi BGRA → 24bpp RGB Bitmap
                bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format24bppRgb);

                int stride = Math.Abs(bitmapData.Stride);
                byte[] buffer = new byte[stride * height];

                for (int y = 0; y < height; y++)
                {
                    int srcRowOffset = y * width * 4; // BGRA = 4 bytes/pixel
                    int dstRowOffset = y * stride;

                    for (int x = 0; x < width; x++)
                    {
                        int src = srcRowOffset + x * 4;
                        int dst = dstRowOffset + x * 3;

                        // BGRA → BGR (24bpp)
                        buffer[dst + 0] = bgraPixels[src + 0]; // B
                        buffer[dst + 1] = bgraPixels[src + 1]; // G
                        buffer[dst + 2] = bgraPixels[src + 2]; // R
                    }
                }

                Marshal.Copy(buffer, 0, bitmapData.Scan0, buffer.Length);

                // Tạo Img struct cho native API
                var img = new OneOcrImg
                {
                    t = 1,
                    col = width,
                    row = height,
                    _unk = 0,
                    step = stride,
                    data_ptr = bitmapData.Scan0
                };

                // Chạy OCR pipeline
                long res = OneOcrNativeMethods.RunOcrPipeline(_pipeline, ref img, _opt, out long instance);
                if (res != 0)
                {
                    AppLogger.Error($"[ONEOCR] RunOcrPipeline failed: {res}");
                    return OcrResult.Empty;
                }

                // Lấy số dòng
                res = OneOcrNativeMethods.GetOcrLineCount(instance, out long lineCount);
                if (res != 0)
                {
                    AppLogger.Error($"[ONEOCR] GetOcrLineCount failed: {res}");
                    return OcrResult.Empty;
                }

                // Đọc text từng dòng
                var sb = new StringBuilder();
                for (long i = 0; i < lineCount; i++)
                {
                    res = OneOcrNativeMethods.GetOcrLine(instance, i, out long line);
                    if (res != 0 || line == 0) continue;

                    res = OneOcrNativeMethods.GetOcrLineContent(line, out IntPtr contentPtr);
                    if (res != 0 || contentPtr == IntPtr.Zero) continue;

                    string lineText = PtrToManagedString(contentPtr) ?? string.Empty;
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(lineText);
                }

                return new OcrResult
                {
                    Text = sb.ToString(),
                    IsTextClipped = false
                };
            }
            catch (DllNotFoundException ex)
            {
                AppLogger.Error($"[ONEOCR] DLL not found: {ex.Message}");
                _isAvailable = false;
                return OcrResult.Empty;
            }
            catch (BadImageFormatException ex)
            {
                AppLogger.Warn($"[ONEOCR] BadImageFormat (bitness mismatch?): {ex.Message}");
                _isAvailable = false;
                return OcrResult.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[ONEOCR] RunOcr error: {ex.Message}");
                return OcrResult.Empty;
            }
            finally
            {
                if (bitmap != null && bitmapData != null)
                {
                    try { bitmap.UnlockBits(bitmapData); } catch { }
                    bitmap.Dispose();
                }
            }
        }

        /// <summary>
        /// Chuyển native UTF-8 C-string pointer → .NET managed string.
        /// </summary>
        private static string? PtrToManagedString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            catch
            {
                // Fallback: đọc byte thủ công
                int length = 0;
                while (Marshal.ReadByte(ptr, length) != 0)
                    length++;

                byte[] buffer = new byte[length];
                Marshal.Copy(ptr, buffer, 0, length);
                return Encoding.UTF8.GetString(buffer);
            }
        }

        // ═══ DLL Discovery ═════════════════════════════════════════════════

        private async Task EnsureDllsAvailableAsync()
        {
            if (!Directory.Exists(_dllDirectory))
                Directory.CreateDirectory(_dllDirectory);

            // Kiểm tra xem tất cả file đã có chưa
            if (RequiredFiles.All(f => File.Exists(Path.Combine(_dllDirectory, f))))
            {
                AppLogger.Info("[ONEOCR] Tất cả DLL đã có sẵn trong thư mục DLL/");
                return;
            }

            // Tìm từ Snipping Tool hoặc Photos qua PowerShell (giống MORT)
            AppLogger.Info("[ONEOCR] Đang tìm DLL từ Windows apps...");

            string? sourcePath = await FindDllViaAppxPackage("Microsoft.ScreenSketch", "SnippingTool");
            if (sourcePath == null)
            {
                sourcePath = await FindDllViaAppxPackage("Microsoft.Windows.Photos", null);
            }

            if (sourcePath == null)
            {
                // Fallback: dò thủ công trong WindowsApps
                sourcePath = FindDllManually();
            }

            if (sourcePath == null)
            {
                AppLogger.Info(
                    "[ONEOCR] Không tìm thấy file DLL OneOCR trên máy. " +
                    "Cần Snipping Tool hoặc Photos (Windows 11 bản mới). " +
                    "Bạn có thể copy thủ công 3 file (oneocr.dll, oneocr.onemodel, onnxruntime.dll) " +
                    "vào thư mục DLL/ cạnh TBCO.exe.");
                return;
            }

            AppLogger.Info($"[ONEOCR] Tìm thấy DLL tại: {sourcePath}");
            foreach (var file in RequiredFiles)
            {
                string src = Path.Combine(sourcePath, file);
                string dst = Path.Combine(_dllDirectory, file);
                if (File.Exists(src) && !File.Exists(dst))
                {
                    try
                    {
                        File.Copy(src, dst);
                        AppLogger.Info($"[ONEOCR] Đã copy: {file}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"[ONEOCR] Không thể copy {file}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Tìm DLL qua PowerShell Get-AppxPackage (giống MORT).
        /// </summary>
        private static async Task<string?> FindDllViaAppxPackage(string appName, string? subFolder)
        {
            try
            {
                var info = new ProcessStartInfo("powershell.exe",
                    $"-Command \"(Get-AppxPackage -Name {appName}).InstallLocation\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };

                using var p = Process.Start(info);
                if (p == null) return null;

                string output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                string basePath = output.Trim();
                if (string.IsNullOrEmpty(basePath)) return null;

                // Nếu có subfolder (VD: SnippingTool nằm trong SnippingTool/ subfolder)
                string searchPath = subFolder != null
                    ? Path.Combine(basePath, subFolder)
                    : basePath;

                if (File.Exists(Path.Combine(searchPath, "oneocr.dll")) &&
                    RequiredFiles.All(f => File.Exists(Path.Combine(searchPath, f))))
                {
                    return searchPath;
                }

                // Thử basePath trực tiếp nếu subfolder không có
                if (subFolder != null &&
                    File.Exists(Path.Combine(basePath, "oneocr.dll")) &&
                    RequiredFiles.All(f => File.Exists(Path.Combine(basePath, f))))
                {
                    return basePath;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[ONEOCR] Lỗi khi dò {appName}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Fallback: dò thủ công trong WindowsApps folders.
        /// </summary>
        private static string? FindDllManually()
        {
            var searchPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WindowsApps"),
            };

            var appPatterns = new[] { "Microsoft.ScreenSketch_*", "Microsoft.Windows.Photos_*" };

            foreach (var basePath in searchPaths)
            {
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    foreach (var pattern in appPatterns)
                    {
                        var dirs = Directory.GetDirectories(basePath, pattern);
                        foreach (var dir in dirs.OrderByDescending(d => d))
                        {
                            // Kiểm tra trực tiếp
                            if (RequiredFiles.All(f => File.Exists(Path.Combine(dir, f))))
                                return dir;

                            // Kiểm tra subfolder SnippingTool
                            string snippingDir = Path.Combine(dir, "SnippingTool");
                            if (Directory.Exists(snippingDir) &&
                                RequiredFiles.All(f => File.Exists(Path.Combine(snippingDir, f))))
                                return snippingDir;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    AppLogger.Warn($"[ONEOCR] Không có quyền truy cập: {basePath}");
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[ONEOCR] Lỗi dò tìm DLL: {ex.Message}");
                }
            }

            return null;
        }

        // ═══ Cleanup ═══════════════════════════════════════════════════════

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
        }

        ~OneOcrEngine() => Dispose(false);
    }
}
