using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace TranslateBot.OCR
{
    public class WindowsOcrEngine : IOcrEngine
    {
        private readonly OcrEngine _ocrEngine;

        // Giới hạn để tránh phóng to vùng quá lớn làm tốn hiệu năng vô ích
        // (VD: nếu người dùng lỡ chọn "Toàn màn hình" làm vùng OCR).
        private const int MaxPixelsToUpscale = 4_000_000;
        private const int UpscaleFactor = 2;

        // Ngưỡng phát hiện text bị cắt ở mép dưới: nếu dòng OCR cuối cùng
        // có BoundingRect nằm sát mép dưới (≤ 5% chiều cao vùng chụp sau upscale).
        private const double ClipDetectionThreshold = 0.05;

        // Chống spam log: cảnh báo cắt cụt có thể đúng liên tục suốt lúc câu dài hiển
        // thị (mỗi 300ms một lần) - chỉ log lại sau mỗi 10 giây.
        private static DateTime _lastClipWarningAt = DateTime.MinValue;
        private static readonly TimeSpan ClipWarningCooldown = TimeSpan.FromSeconds(10);

        public WindowsOcrEngine(string languageTag = "en-US")
        {
            var language = new Windows.Globalization.Language(languageTag);
            
            if (!OcrEngine.IsLanguageSupported(language))
            {
                throw new NotSupportedException($"Ngôn ngữ {languageTag} chưa được cài đặt trên Windows của bạn!");
            }
            
            _ocrEngine = OcrEngine.TryCreateFromLanguage(language);
        }

        public async Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height)
        {
            if (bgraPixels == null || bgraPixels.Length == 0)
                return OcrResult.Empty;

            try
            {
                byte[] pixelsForOcr = bgraPixels;
                int ocrWidth = width;
                int ocrHeight = height;

                // Phóng to ảnh trước khi đưa vào Windows OCR: phụ đề game (đặc biệt
                // các font nghiêng/trang trí như trong FGO) thường bị đọc sai từng
                // ký tự khi ảnh gốc quá nhỏ (VD: "I suppose" bị đọc thành "•uppose").
                // Phóng to 2x bằng nội suy Bicubic chất lượng cao giúp OCR "nhìn rõ"
                // nét chữ hơn, đặc biệt hiệu quả với chữ nghiêng hoặc có viền/bóng.
                if ((long)width * height > 0 && (long)width * height <= MaxPixelsToUpscale)
                {
                    try
                    {
                        (pixelsForOcr, ocrWidth, ocrHeight) = UpscaleForOcr(bgraPixels, width, height, UpscaleFactor);
                    }
                    catch (Exception upscaleEx)
                    {
                        // Nếu upscale lỗi vì lý do gì đó, fallback về ảnh gốc thay vì
                        // làm hỏng luôn cả lượt OCR này.
                        Infrastructure.AppLogger.Error($"[LỖI OCR-UPSCALE] {upscaleEx.Message}");
                        pixelsForOcr = bgraPixels;
                        ocrWidth = width;
                        ocrHeight = height;
                    }
                }

                using var softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, ocrWidth, ocrHeight, BitmapAlphaMode.Ignore);
                softwareBitmap.CopyFromBuffer(pixelsForOcr.AsBuffer());

                var result = await _ocrEngine.RecognizeAsync(softwareBitmap);

                // Phát hiện text bị cắt ở mép dưới vùng chụp
                bool isClipped = DetectTextClipping(result, ocrHeight);
                if (isClipped && DateTime.UtcNow - _lastClipWarningAt > ClipWarningCooldown)
                {
                    _lastClipWarningAt = DateTime.UtcNow;
                    // AppLogger thay vì Console.WriteLine: app build dạng WinExe không có
                    // cửa sổ Console, nên Console.WriteLine trước đây KHÔNG BAO GIỜ hiển thị
                    // ở đâu cả - người dùng không thể biết cảnh báo này tồn tại.
                    TranslateBot.Infrastructure.AppLogger.Warn(
                        $"[OCR_CLIP_WARNING] Text chạm đáy vùng chụp! Nên tăng CaptureHeight " +
                        $"(hoặc chiều cao vùng OCR đang dùng) để không bị mất dòng cuối. " +
                        $"Vùng chụp hiện tại: {width}×{height}px");
                }

                return new OcrResult
                {
                    Text = result.Text,
                    IsTextClipped = isClipped
                };
            }
            catch (Exception ex)
            {
                Infrastructure.AppLogger.Error($"[LỖI OCR] {ex.Message}");
                return OcrResult.Empty;
            }
        }

        // Kiểm tra nếu dòng OCR cuối cùng có BoundingRect sát mép dưới
        private static bool DetectTextClipping(Windows.Media.Ocr.OcrResult result, int imageHeight)
        {
            if (result.Lines == null || result.Lines.Count == 0)
                return false;

            // Lấy dòng cuối cùng - phòng thủ trường hợp dòng rỗng (không có Words)
            // để tránh IndexOutOfRangeException làm hỏng cả lượt OCR.
            var lastLine = result.Lines[result.Lines.Count - 1];
            if (lastLine.Words == null || lastLine.Words.Count == 0)
                return false;

            var bounds = lastLine.Words[lastLine.Words.Count - 1].BoundingRect;

            // Nếu cạnh dưới của dòng cuối nằm sát mép dưới ảnh (≤ 5% chiều cao)
            double bottomGap = imageHeight - (bounds.Y + bounds.Height);
            double gapRatio = bottomGap / imageHeight;

            return gapRatio <= ClipDetectionThreshold;
        }

        // Phóng to ảnh BGRA bằng GDI+ (System.Drawing), nội suy Bicubic chất lượng cao.
        private static (byte[] pixels, int width, int height) UpscaleForOcr(byte[] bgraPixels, int width, int height, int scale)
        {
            int newWidth = width * scale;
            int newHeight = height * scale;

            using var src = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var srcRect = new Rectangle(0, 0, width, height);
            var srcData = src.LockBits(srcRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(bgraPixels, 0, srcData.Scan0, Math.Min(bgraPixels.Length, srcData.Stride * height));
            src.UnlockBits(srcData);

            using var dst = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Tăng tương phản (Contrast Enhancement) kế thừa từ translate-bot (ImageEnhance.Contrast):
                // Giúp tách biệt rõ nét chữ game khỏi nền trong suốt, viền bóng hoặc hiệu ứng đổ bóng.
                float contrast = 1.35f;
                float t = (1.0f - contrast) / 2.0f;
                var colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] { contrast, 0, 0, 0, 0 },
                    new float[] { 0, contrast, 0, 0, 0 },
                    new float[] { 0, 0, contrast, 0, 0 },
                    new float[] { 0, 0, 0, 1.0f, 0 },
                    new float[] { t, t, t, 0, 1 }
                });

                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                g.DrawImage(src, new Rectangle(0, 0, newWidth, newHeight), 0, 0, width, height, GraphicsUnit.Pixel, attributes);
            }

            var dstRect = new Rectangle(0, 0, newWidth, newHeight);
            var dstData = dst.LockBits(dstRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int bytes = Math.Abs(dstData.Stride) * newHeight;
            byte[] output = new byte[bytes];
            Marshal.Copy(dstData.Scan0, output, 0, bytes);
            dst.UnlockBits(dstData);

            return (output, newWidth, newHeight);
        }
    }
}
