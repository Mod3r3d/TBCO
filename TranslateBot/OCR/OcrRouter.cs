using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using TranslateBot.Diagnostics;
using TranslateBot.Infrastructure;
using TranslateBot.OCR.Preprocessing;

namespace TranslateBot.OCR
{
    public enum OcrRoutingStrategy
    {
        Fastest,     // Tốc độ cao nhất: Windows OCR (phần cứng Windows native, 15-30ms)
        Balanced,    // Cân bằng: OneOCR (nếu khả dụng) -> Windows OCR dự phòng
        BestQuality, // Chất lượng tối đa: OneOCR / Tesseract -> Windows OCR dự phòng
        Custom
    }

    /// <summary>
    /// Bộ định tuyến nhận dạng quang học OCR Router (Section 8 của Master Plan).
    /// Quản lý danh sách các engine OCR (Windows OCR, OneOCR, Tesseract),
    /// tự động chuyển đổi sang engine dự phòng khi engine chính gặp lỗi hoặc trả về text rỗng,
    /// và tích hợp bộ tiền xử lý hình ảnh Preprocessing.
    /// </summary>
    public class OcrRouter : IOcrEngine
    {
        private readonly List<(string Name, IOcrEngine Engine)> _engines = new();
        private OcrRoutingStrategy _strategy = OcrRoutingStrategy.Balanced;
        private string _activeEngineName = "None";
        private readonly object _lock = new();

        public OcrRoutingStrategy Strategy
        {
            get => _strategy;
            set => _strategy = value;
        }

        public string ActiveEngineName => _activeEngineName;
        public PreprocessingOptions? DefaultPreprocessing { get; set; }
        public PerformanceMetrics? Metrics { get; set; }

        public IReadOnlyList<(string Name, IOcrEngine Engine)> RegisteredEngines
        {
            get
            {
                lock (_lock) { return _engines.ToList().AsReadOnly(); }
            }
        }

        public OcrRouter(OcrRoutingStrategy strategy = OcrRoutingStrategy.Balanced)
        {
            _strategy = strategy;
        }

        public void RegisterEngine(string name, IOcrEngine engine)
        {
            if (engine == null) return;
            lock (_lock)
            {
                _engines.RemoveAll(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                _engines.Add((name, engine));
                if (_activeEngineName == "None")
                {
                    _activeEngineName = name;
                }
            }
        }

        public bool UnregisterEngine(string name)
        {
            lock (_lock)
            {
                return _engines.RemoveAll(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
            }
        }

        public IOcrEngine? GetEngine(string name)
        {
            lock (_lock)
            {
                return _engines.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Engine;
            }
        }

        public async Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height)
            => await ExtractTextAsync(bgraPixels, width, height, DefaultPreprocessing);

        public async Task<OcrResult> ExtractTextAsync(
            byte[] bgraPixels,
            int width,
            int height,
            PreprocessingOptions? preprocessing)
        {
            if (bgraPixels == null || bgraPixels.Length == 0 || width <= 0 || height <= 0)
            {
                return OcrResult.Empty;
            }

            // 1. Áp dụng tiền xử lý hình ảnh nếu có cấu hình
            byte[] effectivePixels = bgraPixels;
            int effectiveWidth = width;
            int effectiveHeight = height;

            if (preprocessing != null && preprocessing.Preset != PreprocessPreset.None)
            {
                var processed = ImagePreprocessor.Preprocess(bgraPixels, width, height, preprocessing);
                effectivePixels = processed.Pixels;
                effectiveWidth = processed.Width;
                effectiveHeight = processed.Height;
            }

            // 2. Xác định chuỗi engine theo chiến lược
            List<(string Name, IOcrEngine Engine)> engineChain = GetEngineChain();
            if (engineChain.Count == 0)
            {
                AppLogger.Warn("[OCR_ROUTER] Không có OCR engine nào được đăng ký.");
                return OcrResult.Empty;
            }

            var sw = Stopwatch.StartNew();

            // 3. Thử lần lượt từng engine
            for (int i = 0; i < engineChain.Count; i++)
            {
                var (engineName, engine) = engineChain[i];
                try
                {
                    var result = await engine.ExtractTextAsync(effectivePixels, effectiveWidth, effectiveHeight);
                    sw.Stop();

                    if (!string.IsNullOrWhiteSpace(result.Text))
                    {
                        _activeEngineName = engineName;
                        if (Metrics != null)
                        {
                            Metrics.RecordOcrExecuted(sw.Elapsed.TotalMilliseconds);
                        }
                        return result;
                    }

                    // Nếu text rỗng nhưng là engine duy nhất, vẫn trả về
                    if (engineChain.Count == 1)
                    {
                        _activeEngineName = engineName;
                        return result;
                    }

                    // Text rỗng và còn engine khác -> log và thử tiếp engine sau
                    AppLogger.Info($"[OCR_ROUTER_EMPTY] Engine '{engineName}' không tìm thấy chữ, thử engine kế tiếp...");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[OCR_ROUTER_FAIL] Engine '{engineName}' ném lỗi: {ex.Message} -> Chuyển sang fallback.");
                }
            }

            _activeEngineName = engineChain[0].Name;
            return OcrResult.Empty;
        }

        private List<(string Name, IOcrEngine Engine)> GetEngineChain()
        {
            lock (_lock)
            {
                if (_engines.Count == 0) return new List<(string Name, IOcrEngine Engine)>();

                return _strategy switch
                {
                    OcrRoutingStrategy.Fastest =>
                        _engines.OrderBy(e => e.Name.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList(),

                    OcrRoutingStrategy.BestQuality =>
                        _engines.OrderBy(e =>
                            e.Name.Contains("OneOCR", StringComparison.OrdinalIgnoreCase) ? 0 :
                            e.Name.Contains("Tesseract", StringComparison.OrdinalIgnoreCase) ? 1 : 2).ToList(),

                    _ => // Balanced
                        _engines.OrderBy(e =>
                            e.Name.Contains("OneOCR", StringComparison.OrdinalIgnoreCase) ? 0 :
                            e.Name.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? 1 : 2).ToList()
                };
            }
        }
    }
}
