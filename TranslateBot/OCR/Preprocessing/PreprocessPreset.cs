using System;

namespace TranslateBot.OCR.Preprocessing
{
    public enum PreprocessPreset
    {
        None,
        FgoDialogue,
        FgoSpeaker,
        VnDialogue,
        WhiteTextOnDark,
        DarkTextOnLight,
        HighContrast,
        Custom
    }

    /// <summary>
    /// Các tham số tiền xử lý hình ảnh tối ưu cho nhận dạng ký tự quang học (OCR)
    /// </summary>
    public class PreprocessingOptions
    {
        public PreprocessPreset Preset { get; set; } = PreprocessPreset.None;
        
        // Phóng to ảnh (1x hoặc 2x) bằng nội suy Bilinear để làm rõ nét font game nhỏ
        public int ScaleFactor { get; set; } = 1;
        
        // Chuyển ảnh màu BGRA sang Grayscale
        public bool Grayscale { get; set; } = false;
        
        // Đảo ngược màu (Invert) - cần thiết khi chữ tối trên nền sáng hoặc ngược lại
        public bool Invert { get; set; } = false;
        
        // Tăng độ tương phản (1.0 = bình thường, > 1.0 = tăng tương phản)
        public double ContrastBoost { get; set; } = 1.0;
        
        // Nhị phân hóa ảnh (Binarization)
        public bool EnableThreshold { get; set; } = false;
        public bool UseOtsuThreshold { get; set; } = false;
        public byte ThresholdValue { get; set; } = 128;
        
        // Lọc theo độ sáng tối thiểu (Luminance Isolation) - loại bỏ khung thoại bán trong suốt
        public bool EnableLuminanceFilter { get; set; } = false;
        public byte MinLuminance { get; set; } = 120;

        // Lọc dải màu cụ thể (ví dụ: chữ vàng FGO tên nhân vật)
        public bool EnableColorFilter { get; set; } = false;
        public byte TargetR { get; set; } = 255;
        public byte TargetG { get; set; } = 215;
        public byte TargetB { get; set; } = 0;
        public int ColorTolerance { get; set; } = 60;

        public static PreprocessingOptions FromPreset(PreprocessPreset preset) => preset switch
        {
            PreprocessPreset.FgoDialogue => new PreprocessingOptions
            {
                Preset = PreprocessPreset.FgoDialogue,
                ScaleFactor = 2,
                Grayscale = true,
                ContrastBoost = 1.3,
                EnableLuminanceFilter = true,
                MinLuminance = 110
            },
            PreprocessPreset.FgoSpeaker => new PreprocessingOptions
            {
                Preset = PreprocessPreset.FgoSpeaker,
                ScaleFactor = 2,
                Grayscale = true,
                ContrastBoost = 1.5,
                EnableLuminanceFilter = true,
                MinLuminance = 150
            },
            PreprocessPreset.VnDialogue => new PreprocessingOptions
            {
                Preset = PreprocessPreset.VnDialogue,
                ScaleFactor = 2,
                Grayscale = true,
                ContrastBoost = 1.2
            },
            PreprocessPreset.WhiteTextOnDark => new PreprocessingOptions
            {
                Preset = PreprocessPreset.WhiteTextOnDark,
                ScaleFactor = 2,
                Grayscale = true,
                EnableThreshold = true,
                UseOtsuThreshold = true
            },
            PreprocessPreset.DarkTextOnLight => new PreprocessingOptions
            {
                Preset = PreprocessPreset.DarkTextOnLight,
                ScaleFactor = 2,
                Grayscale = true,
                Invert = true,
                EnableThreshold = true,
                UseOtsuThreshold = true
            },
            PreprocessPreset.HighContrast => new PreprocessingOptions
            {
                Preset = PreprocessPreset.HighContrast,
                Grayscale = true,
                ContrastBoost = 1.8
            },
            _ => new PreprocessingOptions { Preset = PreprocessPreset.None }
        };
    }
}
