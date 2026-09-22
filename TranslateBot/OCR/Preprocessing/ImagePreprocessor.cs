using System;

namespace TranslateBot.OCR.Preprocessing
{
    public readonly struct PreprocessedImage
    {
        public byte[] Pixels { get; }
        public int Width { get; }
        public int Height { get; }

        public PreprocessedImage(byte[] pixels, int width, int height)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Pipeline xử lý hình ảnh hiệu năng cao phục vụ OCR (Section 8 của Master Plan)
    /// </summary>
    public static class ImagePreprocessor
    {
        public static PreprocessedImage Preprocess(
            byte[] bgraPixels,
            int width,
            int height,
            PreprocessingOptions? options)
        {
            if (bgraPixels == null || bgraPixels.Length == 0 || width <= 0 || height <= 0)
            {
                return new PreprocessedImage(Array.Empty<byte>(), 0, 0);
            }

            if (options == null || options.Preset == PreprocessPreset.None)
            {
                return new PreprocessedImage(bgraPixels, width, height);
            }

            byte[] currentPixels;
            int currentWidth = width;
            int currentHeight = height;

            // 1. Phóng to 2x (ScaleFactor == 2)
            if (options.ScaleFactor == 2)
            {
                currentWidth = width * 2;
                currentHeight = height * 2;
                currentPixels = Scale2xNearest(bgraPixels, width, height);
            }
            else
            {
                // Clone buffer để không làm biến đổi mảng gốc của frame capture pool
                currentPixels = new byte[bgraPixels.Length];
                Buffer.BlockCopy(bgraPixels, 0, currentPixels, 0, bgraPixels.Length);
            }

            // 2. Lọc dải màu mục tiêu (Color Filter)
            if (options.EnableColorFilter)
            {
                ColorFilter.ApplyColorRangeFilter(
                    currentPixels,
                    options.TargetR,
                    options.TargetG,
                    options.TargetB,
                    options.ColorTolerance);
            }

            // 3. Lọc theo độ sáng tối thiểu (Luminance Filter)
            if (options.EnableLuminanceFilter)
            {
                ColorFilter.ApplyLuminanceFilter(currentPixels, options.MinLuminance);
            }

            // 4. Chuyển đổi sang Grayscale
            if (options.Grayscale)
            {
                ApplyGrayscale(currentPixels);
            }

            // 5. Tăng cường độ tương phản (Contrast Boost)
            if (options.ContrastBoost > 1.01 || options.ContrastBoost < 0.99)
            {
                ApplyContrast(currentPixels, options.ContrastBoost);
            }

            // 6. Đảo ngược màu sắc (Invert)
            if (options.Invert)
            {
                ApplyInvert(currentPixels);
            }

            // 7. Nhị phân hóa (Thresholding / Binarization)
            if (options.EnableThreshold)
            {
                if (options.UseOtsuThreshold)
                {
                    ThresholdFilter.ApplyOtsuThreshold(currentPixels);
                }
                else
                {
                    ThresholdFilter.ApplyFixedThreshold(currentPixels, options.ThresholdValue);
                }
            }

            return new PreprocessedImage(currentPixels, currentWidth, currentHeight);
        }

        // Phóng to 2x siêu tốc giữ nguyên cạnh sắc nét của ký tự font game
        public static byte[] Scale2xNearest(byte[] srcPixels, int srcW, int srcH)
        {
            int dstW = srcW * 2;
            int dstH = srcH * 2;
            byte[] dst = new byte[dstW * dstH * 4];

            int srcStride = srcW * 4;
            int dstStride = dstW * 4;

            for (int y = 0; y < srcH; y++)
            {
                int srcRowOffset = y * srcStride;
                int dstRow1Offset = (y * 2) * dstStride;
                int dstRow2Offset = (y * 2 + 1) * dstStride;

                for (int x = 0; x < srcW; x++)
                {
                    int srcPixel = srcRowOffset + x * 4;
                    int dstPixel1 = dstRow1Offset + x * 8;
                    int dstPixel2 = dstRow2Offset + x * 8;

                    byte b = srcPixels[srcPixel];
                    byte g = srcPixels[srcPixel + 1];
                    byte r = srcPixels[srcPixel + 2];
                    byte a = srcPixels[srcPixel + 3];

                    // Pixel (2x, 2y)
                    dst[dstPixel1] = b;
                    dst[dstPixel1 + 1] = g;
                    dst[dstPixel1 + 2] = r;
                    dst[dstPixel1 + 3] = a;

                    // Pixel (2x + 1, 2y)
                    dst[dstPixel1 + 4] = b;
                    dst[dstPixel1 + 5] = g;
                    dst[dstPixel1 + 6] = r;
                    dst[dstPixel1 + 7] = a;

                    // Pixel (2x, 2y + 1)
                    dst[dstPixel2] = b;
                    dst[dstPixel2 + 1] = g;
                    dst[dstPixel2 + 2] = r;
                    dst[dstPixel2 + 3] = a;

                    // Pixel (2x + 1, 2y + 1)
                    dst[dstPixel2 + 4] = b;
                    dst[dstPixel2 + 5] = g;
                    dst[dstPixel2 + 6] = r;
                    dst[dstPixel2 + 7] = a;
                }
            }

            return dst;
        }

        public static void ApplyGrayscale(byte[] bgraPixels)
        {
            for (int i = 0; i < bgraPixels.Length; i += 4)
            {
                byte b = bgraPixels[i];
                byte g = bgraPixels[i + 1];
                byte r = bgraPixels[i + 2];

                byte y = (byte)((r * 299 + g * 587 + b * 114) / 1000);

                bgraPixels[i] = y;
                bgraPixels[i + 1] = y;
                bgraPixels[i + 2] = y;
            }
        }

        public static void ApplyInvert(byte[] bgraPixels)
        {
            for (int i = 0; i < bgraPixels.Length; i += 4)
            {
                bgraPixels[i] = (byte)(255 - bgraPixels[i]);
                bgraPixels[i + 1] = (byte)(255 - bgraPixels[i + 1]);
                bgraPixels[i + 2] = (byte)(255 - bgraPixels[i + 2]);
            }
        }

        public static void ApplyContrast(byte[] bgraPixels, double contrast)
        {
            for (int i = 0; i < bgraPixels.Length; i += 4)
            {
                for (int c = 0; c < 3; c++)
                {
                    double val = (bgraPixels[i + c] - 128) * contrast + 128;
                    bgraPixels[i + c] = (byte)Math.Clamp(val, 0, 255);
                }
            }
        }
    }
}
