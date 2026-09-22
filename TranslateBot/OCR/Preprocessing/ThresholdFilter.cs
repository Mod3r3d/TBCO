using System;

namespace TranslateBot.OCR.Preprocessing
{
    /// <summary>
    /// Thuật toán phân ngưỡng và nhị phân hóa (Binarization / Thresholding)
    /// </summary>
    public static class ThresholdFilter
    {
        // Áp dụng ngưỡng cố định (Fixed Thresholding)
        public static void ApplyFixedThreshold(byte[] bgraPixels, byte threshold)
        {
            if (bgraPixels == null || bgraPixels.Length < 4) return;

            for (int i = 0; i < bgraPixels.Length; i += 4)
            {
                byte b = bgraPixels[i];
                byte g = bgraPixels[i + 1];
                byte r = bgraPixels[i + 2];

                // Tính Luminance chuẩn ITU-R BT.601
                byte y = (byte)((r * 299 + g * 587 + b * 114) / 1000);
                byte val = (y >= threshold) ? (byte)255 : (byte)0;

                bgraPixels[i] = val;
                bgraPixels[i + 1] = val;
                bgraPixels[i + 2] = val;
                bgraPixels[i + 3] = 255;
            }
        }

        // Tự động tính ngưỡng phân tách tối ưu theo thuật toán Otsu
        public static byte CalculateOtsuThreshold(byte[] bgraPixels)
        {
            if (bgraPixels == null || bgraPixels.Length < 4) return 128;

            int totalPixels = bgraPixels.Length / 4;
            int[] histogram = new int[256];

            for (int i = 0; i < bgraPixels.Length; i += 4)
            {
                byte b = bgraPixels[i];
                byte g = bgraPixels[i + 1];
                byte r = bgraPixels[i + 2];
                int y = (r * 299 + g * 587 + b * 114) / 1000;
                histogram[y]++;
            }

            double sumAll = 0;
            for (int t = 0; t < 256; t++)
            {
                sumAll += t * histogram[t];
            }

            double sumBackground = 0;
            int weightBackground = 0;
            double maxVariance = 0;
            byte bestThreshold = 128;

            for (int t = 0; t < 256; t++)
            {
                weightBackground += histogram[t];
                if (weightBackground == 0) continue;

                int weightForeground = totalPixels - weightBackground;
                if (weightForeground == 0) break;

                sumBackground += t * histogram[t];

                double meanBackground = sumBackground / weightBackground;
                double meanForeground = (sumAll - sumBackground) / weightForeground;

                double diff = meanBackground - meanForeground;
                double betweenClassVariance = (double)weightBackground * weightForeground * diff * diff;

                if (betweenClassVariance > maxVariance)
                {
                    maxVariance = betweenClassVariance;
                    bestThreshold = (byte)t;
                }
            }

            return bestThreshold;
        }

        // Áp dụng thuật toán Otsu tự động
        public static void ApplyOtsuThreshold(byte[] bgraPixels)
        {
            byte otsuThreshold = CalculateOtsuThreshold(bgraPixels);
            ApplyFixedThreshold(bgraPixels, otsuThreshold);
        }
    }
}
