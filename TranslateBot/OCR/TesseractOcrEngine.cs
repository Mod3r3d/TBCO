using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tesseract;
using TranslateBot.Infrastructure;

namespace TranslateBot.OCR
{
    /// <summary>
    /// Engine nhận dạng ký tự quang học Tesseract OCR (Section 8 của Master Plan)
    /// Hỗ trợ nạp đa ngôn ngữ, tự động dò tìm tessdata, và tự phục hồi an toàn khi thiếu file traineddata.
    /// </summary>
    public class TesseractOcrEngine : IOcrEngine, IDisposable
    {
        private TesseractEngine? _engine;
        private readonly object _lock = new();
        private bool _isInitialized;
        private bool _isAvailable;
        private string _language;
        private string? _customDataPath;
        private bool _hasLoggedMissing;

        public bool IsAvailable => _isAvailable;
        public string Language => _language;
        public string? ResolvedDataPath { get; private set; }

        public TesseractOcrEngine(string language = "eng", string? customDataPath = null)
        {
            _language = language;
            _customDataPath = customDataPath;
            TryInitialize();
        }

        private void TryInitialize()
        {
            lock (_lock)
            {
                if (_isInitialized) return;
                _isInitialized = true;

                string? dataPath = FindTessdataPath(_customDataPath);
                if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
                {
                    if (!_hasLoggedMissing)
                    {
                        _hasLoggedMissing = true;
                        AppLogger.Info($"[TESSERACT] Không tìm thấy thư mục 'tessdata'. Tesseract engine sẽ ở chế độ dự phòng.");
                    }
                    _isAvailable = false;
                    return;
                }

                // Kiểm tra có file .traineddata cho ngôn ngữ yêu cầu không
                string langFile = Path.Combine(dataPath, $"{_language}.traineddata");
                if (!File.Exists(langFile))
                {
                    // Thử tìm file bất kỳ nếu ngôn ngữ chỉ định chưa có
                    var files = Directory.GetFiles(dataPath, "*.traineddata");
                    if (files.Length == 0)
                    {
                        _isAvailable = false;
                        AppLogger.Info($"[TESSERACT] Thư mục '{dataPath}' không chứa file .traineddata nào.");
                        return;
                    }
                    // Dùng file đầu tiên tìm thấy
                    string firstLang = Path.GetFileNameWithoutExtension(files[0]);
                    AppLogger.Info($"[TESSERACT] Không có '{_language}.traineddata', tự động chuyển sang '{firstLang}'.");
                    _language = firstLang;
                }

                try
                {
                    ResolvedDataPath = dataPath;
                    _engine = new TesseractEngine(dataPath, _language, EngineMode.Default);
                    _isAvailable = true;
                    AppLogger.Info($"[TESSERACT_READY] Khởi tạo thành công Tesseract OCR (ngôn ngữ: {_language}, path: {dataPath})");
                }
                catch (Exception ex)
                {
                    _isAvailable = false;
                    AppLogger.Warn($"[TESSERACT_INIT_ERROR] Không thể khởi tạo Tesseract native engine: {ex.Message}");
                }
            }
        }

        public Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height)
        {
            if (!_isAvailable || _engine == null || bgraPixels == null || bgraPixels.Length == 0 || width <= 0 || height <= 0)
            {
                return Task.FromResult(OcrResult.Empty);
            }

            try
            {
                lock (_lock)
                {
                    using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    var bmpData = bitmap.LockBits(
                        new Rectangle(0, 0, width, height),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);

                    Marshal.Copy(bgraPixels, 0, bmpData.Scan0, Math.Min(bgraPixels.Length, bmpData.Stride * height));
                    bitmap.UnlockBits(bmpData);

                    using var ms = new MemoryStream();
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                    using var pix = Pix.LoadFromMemory(ms.ToArray());
                    using var page = _engine.Process(pix);

                    string text = page.GetText()?.Trim() ?? string.Empty;
                    return Task.FromResult(new OcrResult
                    {
                        Text = text,
                        IsTextClipped = false
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[TESSERACT_OCR_ERROR] Lỗi xử lý nhận diện: {ex.Message}");
                return Task.FromResult(OcrResult.Empty);
            }
        }

        private static string? FindTessdataPath(string? preferredPath)
        {
            if (!string.IsNullOrEmpty(preferredPath) && Directory.Exists(preferredPath))
            {
                return preferredPath;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localTessdata = Path.Combine(baseDir, "tessdata");
            if (Directory.Exists(localTessdata)) return localTessdata;

            string envPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX") ?? "";
            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            {
                return envPath;
            }

            string programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tessdata");
            if (Directory.Exists(programFiles)) return programFiles;

            return null;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _engine?.Dispose();
                _engine = null;
                _isAvailable = false;
            }
        }
    }
}
