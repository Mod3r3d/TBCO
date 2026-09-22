using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.OCR.Preprocessing;

namespace TranslateBot.Tests
{
    [TestClass]
    public class ImagePreprocessingTests
    {
        [TestMethod]
        public void ImagePreprocessor_Grayscale_AppliesItuLumaFormula()
        {
            // Pure Red: (B=0, G=0, R=255, A=255) -> Luma = (255 * 299) / 1000 = 76
            byte[] bgra = new byte[] { 0, 0, 255, 255 };
            ImagePreprocessor.ApplyGrayscale(bgra);

            Assert.AreEqual(76, bgra[0]); // B
            Assert.AreEqual(76, bgra[1]); // G
            Assert.AreEqual(76, bgra[2]); // R
            Assert.AreEqual(255, bgra[3]); // A
        }

        [TestMethod]
        public void ImagePreprocessor_Invert_InvertsRgbChannels()
        {
            byte[] bgra = new byte[] { 50, 100, 200, 255 };
            ImagePreprocessor.ApplyInvert(bgra);

            Assert.AreEqual(205, bgra[0]); // 255 - 50
            Assert.AreEqual(155, bgra[1]); // 255 - 100
            Assert.AreEqual(55, bgra[2]);  // 255 - 200
            Assert.AreEqual(255, bgra[3]); // Alpha unchanged
        }

        [TestMethod]
        public void ImagePreprocessor_Scale2xNearest_DoublesDimensionsAndReplicatesPixels()
        {
            // 2x2 image (4 pixels)
            // [Pixel0, Pixel1]
            // [Pixel2, Pixel3]
            int w = 2, h = 2;
            byte[] src = new byte[w * h * 4];
            
            // Pixel 0 (0, 0): Red
            src[0] = 0; src[1] = 0; src[2] = 255; src[3] = 255;
            // Pixel 1 (1, 0): Green
            src[4] = 0; src[5] = 255; src[6] = 0; src[7] = 255;
            // Pixel 2 (0, 1): Blue
            src[8] = 255; src[9] = 0; src[10] = 0; src[11] = 255;
            // Pixel 3 (1, 1): White
            src[12] = 255; src[13] = 255; src[14] = 255; src[15] = 255;

            byte[] dst = ImagePreprocessor.Scale2xNearest(src, w, h);

            int dstW = 4, dstH = 4;
            Assert.AreEqual(dstW * dstH * 4, dst.Length);

            // Pixel (0, 0) and (1, 0) should both be Red
            Assert.AreEqual(255, dst[2]); // (0, 0) R
            Assert.AreEqual(255, dst[6]); // (1, 0) R

            // Pixel (0, 1) and (1, 1) should both be Red (2x2 block for Pixel 0)
            int row1Stride = dstW * 4;
            Assert.AreEqual(255, dst[row1Stride + 2]); // (0, 1) R
            Assert.AreEqual(255, dst[row1Stride + 6]); // (1, 1) R
        }

        [TestMethod]
        public void ThresholdFilter_FixedThreshold_BinarizesBlackAndWhite()
        {
            // Pixel 0: dark (luma < 128) -> should become 0
            // Pixel 1: bright (luma >= 128) -> should become 255
            byte[] bgra = new byte[]
            {
                50, 50, 50, 255,
                200, 200, 200, 255
            };

            ThresholdFilter.ApplyFixedThreshold(bgra, 128);

            Assert.AreEqual(0, bgra[0]);
            Assert.AreEqual(0, bgra[1]);
            Assert.AreEqual(0, bgra[2]);

            Assert.AreEqual(255, bgra[4]);
            Assert.AreEqual(255, bgra[5]);
            Assert.AreEqual(255, bgra[6]);
        }

        [TestMethod]
        public void ThresholdFilter_OtsuThreshold_CalculatesOptimalThreshold()
        {
            // Bimodal image: half pixels around 30, half around 220
            byte[] bgra = new byte[100 * 4];
            for (int i = 0; i < 50; i++)
            {
                bgra[i * 4] = 30;
                bgra[i * 4 + 1] = 30;
                bgra[i * 4 + 2] = 30;
                bgra[i * 4 + 3] = 255;
            }
            for (int i = 50; i < 100; i++)
            {
                bgra[i * 4] = 220;
                bgra[i * 4 + 1] = 220;
                bgra[i * 4 + 2] = 220;
                bgra[i * 4 + 3] = 255;
            }

            byte otsu = ThresholdFilter.CalculateOtsuThreshold(bgra);
            // Otsu should comfortably fall between 30 and 220
            Assert.IsTrue(otsu >= 30 && otsu <= 220, $"Otsu threshold ({otsu}) must separate the two clusters");
        }

        [TestMethod]
        public void ColorFilter_LuminanceFilter_IsolatesBrightTextFromDarkBackground()
        {
            byte[] bgra = new byte[]
            {
                // Dark translucent textbox background (Luma ~ 40)
                40, 40, 40, 255,
                // Bright white subtitle text (Luma ~ 240)
                240, 240, 240, 255
            };

            ColorFilter.ApplyLuminanceFilter(bgra, minLuminance: 120);

            // Dark pixel erased to 0
            Assert.AreEqual(0, bgra[0]);
            Assert.AreEqual(0, bgra[1]);
            Assert.AreEqual(0, bgra[2]);

            // Bright text remains intact
            Assert.AreEqual(240, bgra[4]);
            Assert.AreEqual(240, bgra[5]);
            Assert.AreEqual(240, bgra[6]);
        }

        [TestMethod]
        public void ColorFilter_ColorRangeFilter_IsolatesGoldSpeakerColor()
        {
            byte[] bgra = new byte[]
            {
                // Gold color #FFD700 (R=255, G=215, B=0)
                0, 215, 255, 255,
                // Blue background #0000FF (R=0, G=0, B=255)
                255, 0, 0, 255
            };

            ColorFilter.ApplyColorRangeFilter(bgra, targetR: 255, targetG: 215, targetB: 0, tolerance: 30);

            // Gold matches -> turned to pure white 255
            Assert.AreEqual(255, bgra[0]);
            Assert.AreEqual(255, bgra[1]);
            Assert.AreEqual(255, bgra[2]);

            // Blue doesn't match -> black 0
            Assert.AreEqual(0, bgra[4]);
            Assert.AreEqual(0, bgra[5]);
            Assert.AreEqual(0, bgra[6]);
        }

        [TestMethod]
        public void ImagePreprocessor_WithPreset_TransformsDimensionsAndPixels()
        {
            int w = 10, h = 10;
            byte[] pixels = new byte[w * h * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 180;
                pixels[i + 1] = 180;
                pixels[i + 2] = 180;
                pixels[i + 3] = 255;
            }

            var options = PreprocessingOptions.FromPreset(PreprocessPreset.FgoDialogue);
            var result = ImagePreprocessor.Preprocess(pixels, w, h, options);

            Assert.AreEqual(w * 2, result.Width);
            Assert.AreEqual(h * 2, result.Height);
            Assert.AreEqual((w * 2) * (h * 2) * 4, result.Pixels.Length);
        }
    }
}
