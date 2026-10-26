using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using TranslateBot.OCR.Preprocessing;

namespace TranslateBot.Evaluation
{
    public class OcrBenchmarkSample
    {
        public string SampleId { get; set; } = string.Empty;
        public string ProfileId { get; set; } = "fgo-jp";
        public string GameName { get; set; } = "Fate/Grand Order";
        public string Category { get; set; } = "StandardDialogue";
        public string GroundTruthText { get; set; } = string.Empty;
        public List<string> ProperNouns { get; set; } = new();
        public PreprocessPreset Preset { get; set; } = PreprocessPreset.FgoDialogue;
        public int Width { get; set; } = 480;
        public int Height { get; set; } = 120;

        /// <summary>
        /// Tạo mảng pixel BGRA đại diện trung thực cho tình huống game
        /// </summary>
        public byte[] GenerateBgraPixels()
        {
            using var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color bgColor = Color.FromArgb(24, 28, 36);
            Color textColor = Color.White;
            float fontSize = 14f;

            if (Category == "LightBackground")
            {
                bgColor = Color.FromArgb(235, 238, 242);
                textColor = Color.FromArgb(20, 20, 25);
            }
            else if (Category == "DarkBackground")
            {
                bgColor = Color.FromArgb(12, 14, 18);
                textColor = Color.FromArgb(250, 250, 250);
            }
            else if (Category == "SmallText")
            {
                fontSize = 9.5f;
            }

            g.Clear(bgColor);

            // Nếu có UI noise, vẽ các nhãn giả lập SKIP / AUTO / LOG
            if (Category == "UiNoise")
            {
                using var noiseFont = new Font("Arial", 8f, FontStyle.Bold);
                using var noiseBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString("AUTO", noiseFont, noiseBrush, new PointF(Width - 100, 5));
                g.DrawString("SKIP", noiseFont, noiseBrush, new PointF(Width - 50, 5));
                g.DrawString("LOG", noiseFont, noiseBrush, new PointF(Width - 150, 5));
            }

            using var font = new Font("Meiryo", fontSize, FontStyle.Regular);
            using var brush = new SolidBrush(textColor);

            float startY = 15f;
            if (Category == "ClippedEdge")
            {
                // Đẩy text xuống sát mép đáy để kiểm tra phát hiện clipped
                startY = Height - (fontSize * 2.2f);
            }

            var rect = new RectangleF(10, startY, Width - 20, Height - startY);
            g.DrawString(GroundTruthText, font, brush, rect);

            // Chuyển Bitmap thành BGRA byte array
            var bmpData = bmp.LockBits(
                new Rectangle(0, 0, Width, Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            int totalBytes = Math.Abs(bmpData.Stride) * Height;
            byte[] bgraBytes = new byte[totalBytes];
            Marshal.Copy(bmpData.Scan0, bgraBytes, 0, totalBytes);
            bmp.UnlockBits(bmpData);

            return bgraBytes;
        }
    }
}
