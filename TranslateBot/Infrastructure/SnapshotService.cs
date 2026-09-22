using System;
using System.Threading.Tasks;
using TranslateBot.Capture;
using TranslateBot.OCR;
using TranslateBot.OCR.Preprocessing;
using TranslateBot.Translation;

namespace TranslateBot.Infrastructure
{
    // Section 18: One-shot capture → OCR → translate → return.
    // Phục vụ cho Snapshot mode (F8): dịch ngay 1 frame mà KHÔNG cần bot đang chạy,
    // KHÔNG đi qua DialogueTracker (không cần state machine cho lệnh chụp tay).
    // Dùng cho: story text, menu, profile, lore, long paragraph, debug.
    public class SnapshotService
    {
        private readonly CaptureService _captureService;
        private readonly IOcrEngine _ocrEngine;
        private readonly ITranslationProvider _translationProvider;

        public PreprocessingOptions? Preprocessing { get; set; } = 
            PreprocessingOptions.FromPreset(PreprocessPreset.FgoDialogue);

        public SnapshotService(
            CaptureService captureService,
            IOcrEngine ocrEngine,
            ITranslationProvider translationProvider)
        {
            _captureService = captureService;
            _ocrEngine = ocrEngine;
            _translationProvider = translationProvider;
        }

        // Chế độ screen-absolute
        public Task<SnapshotResult> CaptureAndTranslateAsync(int x, int y, int width, int height)
        {
            byte[] frame = _captureService.CaptureRegion(x, y, width, height);
            return ProcessFrameAsync(frame, width, height);
        }

        // Chế độ window-relative
        public Task<SnapshotResult> CaptureAndTranslateAsync(IntPtr windowHandle, int relX, int relY, int width, int height)
        {
            byte[]? frame = _captureService.CaptureWindowRegion(windowHandle, relX, relY, width, height);
            return ProcessFrameAsync(frame ?? Array.Empty<byte>(), width, height);
        }

        private async Task<SnapshotResult> ProcessFrameAsync(byte[] frame, int width, int height)
        {
            if (frame.Length == 0)
            {
                AppLogger.Error("[SNAPSHOT_FAILED] Không thể chụp frame");
                return new SnapshotResult { Success = false };
            }

            try
            {
                // Milestone 2.3: Tiền xử lý hình ảnh cho Snapshot
                byte[] effectiveFrame = frame;
                int effectiveW = width;
                int effectiveH = height;

                if (Preprocessing != null && Preprocessing.Preset != PreprocessPreset.None)
                {
                    var processed = ImagePreprocessor.Preprocess(frame, width, height, Preprocessing);
                    effectiveFrame = processed.Pixels;
                    effectiveW = processed.Width;
                    effectiveH = processed.Height;
                }

                // 1. OCR
                var ocrResult = await _ocrEngine.ExtractTextAsync(effectiveFrame, effectiveW, effectiveH);
                string ocrText = ocrResult.Text;
                if (ocrResult.IsTextClipped)
                {
                    AppLogger.Warn("[SNAPSHOT_CLIP_WARNING] Text bị cắt ở mép dưới vùng chụp!");
                }
                if (string.IsNullOrWhiteSpace(ocrText))
                {
                    AppLogger.Info("[SNAPSHOT_EMPTY] OCR không nhận được chữ nào");
                    return new SnapshotResult { Success = true, OriginalText = "", TranslatedText = "(Không nhận được chữ)" };
                }

                // 2. Dịch
                string translated = await _translationProvider.TranslateAsync(ocrText);
                if (string.IsNullOrEmpty(translated))
                    translated = ocrText; // Fallback nguyên văn

                AppLogger.Info($"[SNAPSHOT_DONE] \"{ocrText[..Math.Min(50, ocrText.Length)]}...\"");

                return new SnapshotResult
                {
                    Success = true,
                    OriginalText = ocrText,
                    TranslatedText = translated
                };
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[SNAPSHOT_FAILED] {ex.Message}");
                return new SnapshotResult { Success = false, OriginalText = ex.Message };
            }
        }
    }

    public class SnapshotResult
    {
        public bool Success { get; set; }
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
    }
}
