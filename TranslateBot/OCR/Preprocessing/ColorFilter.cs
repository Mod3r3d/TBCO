using System;

namespace TranslateBot.OCR.Preprocessing
{
    /// <summary>
    /// Bộ lọc màu sắc và độ sáng phục vụ cô lập văn bản (Color & Luminance Isolation)
    /// </summary>
    public static class ColorFilter
    {
        // Lọc bỏ nền mờ: giữ lại các pixel có độ sáng lớn hơn hoặc bằng minLuminance,
        // các pixel tối hơn bị làm đen hoàn toàn (0, 0, 0).
        public static void ApplyLuminanceFilter(byte[] bgraPixels, byte minLuminance)
        {
            if (bgraPixels == null || bgraPixels.Length < 4) return;

            for (int i = 0; i < bgraPixels.Length; i += 4)
            {
                byte b = bgraPixels[i];
                byte g = bgraPixels[i + 1];
                byte r = bgraPixels[i + 2];

                int y = (r * 299 + g * 587 + b * 114) / 1000;

                if (y < minLuminance)
                {
                    bgraPixels[i] = 0;
                    bgraPixels[i + 1] = 0;
                    bgraPixels[i + 2] = 0;
                }
            }
        }

        // Cô lập dải màu cụ thể (ví dụ: chữ vàng của tên Servant FGO)
        public static void ApplyColorRangeFilter(
            byte[] bgraPixels,
            byte targetR,
            byte targetG,
            byte targetB,
            int tolerance)
        {
            if (bgraPixels == null || bgraPixels.Length < 4) return;

            int tolSquared = tolerance * tolerance;

            for (int i = 0; i < bgraPixels.Length; i += 4)
            {
                byte b = bgraPixels[i];
                byte g = bgraPixels[i + 1];
                byte r = bgraPixels[i + 2];

                int dr = r - targetR;
                int dg = g - targetG;
                int db = b - targetB;
                int distSq = dr * dr + dg * dg + db * db;

                if (distSq <= tolSquared)
                {
                    // Trúng dải màu mục tiêu -> biến thành chữ trắng rõ nét
                    bgraPixels[i] = 255;
                    bgraPixels[i + 1] = 255;
                    bgraPixels[i + 2] = 255;
                }
                else
                {
                    // Ngoài dải màu -> làm đen nền
                    bgraPixels[i] = 0;
                    bgraPixels[i + 1] = 0;
                    bgraPixels[i + 2] = 0;
                }
            }
        }
    }
}
