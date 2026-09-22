using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TranslateBot.Overlay
{
    public enum OverlayMode
    {
        Overlay,    // Thẻ nổi có khung bo góc, nửa trong suốt
        Layer,      // HUD tối giản không viền, phụ đề hòa trực tiếp vào game
        Windowed    // Chế độ cửa sổ thông thường
    }

    public enum OverlayTheme
    {
        Dark,           // Giao diện tối hiện đại (Slate/Dark Gray)
        Light,          // Giao diện sáng thanh lịch
        FgoChaldea,     // Tông màu FGO Chaldea (Xanh thẫm vũ trụ + Viền xanh neon + Điểm nhấn Vàng Kim)
        HighContrast    // Tương phản cao (Đen tuyền + Chữ vàng rực rỡ, tối ưu đọc nhanh)
    }

    public enum DialogueDisplayMode
    {
        TranslationOnly, // Chỉ hiện câu dịch
        Both,            // Hiện cả câu gốc (nhỏ/mờ) và câu dịch (lớn)
        OriginalAbove,   // Câu gốc phía trên, câu dịch phía dưới
        Debug            // Kèm thông số kỹ thuật (OCR raw, sequence ID, latency, cache)
    }

    public enum OverlayPosition
    {
        Bottom,     // Ghim đáy cửa sổ game (khu vực textbox chuẩn VN/FGO)
        Top,        // Ghim đỉnh cửa sổ game
        Custom      // Người dùng tự kéo thả tự do
    }

    public class OverlayStyle
    {
        public OverlayMode Mode { get; set; } = OverlayMode.Overlay;
        public OverlayTheme Theme { get; set; } = OverlayTheme.FgoChaldea;
        public DialogueDisplayMode DisplayMode { get; set; } = DialogueDisplayMode.Both;
        public OverlayPosition Position { get; set; } = OverlayPosition.Bottom;

        // Typography
        public string FontFamily { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 20.0;
        public FontWeight FontWeight { get; set; } = FontWeights.Medium;
        public double OriginalFontSize => Math.Max(12.0, FontSize * 0.7);

        // Card Styling
        public double CardOpacity { get; set; } = 0.88;
        public CornerRadius CornerRadius { get; set; } = new CornerRadius(12);
        public Thickness BorderThickness { get; set; } = new Thickness(1.5);
        public Thickness Padding { get; set; } = new Thickness(16, 12, 16, 12);

        // Brushes
        public Brush BackgroundBrush { get; set; } = Brushes.Transparent;
        public Brush BorderBrush { get; set; } = Brushes.Transparent;
        public Brush TextBrush { get; set; } = Brushes.White;
        public Brush SecondaryTextBrush { get; set; } = Brushes.LightGray;
        public Brush SpeakerBadgeBrush { get; set; } = Brushes.Gold;
        public Brush SpeakerTextBrush { get; set; } = Brushes.Black;
        public Brush DebugTextBrush { get; set; } = Brushes.LimeGreen;

        // Shadow / Glow
        public bool HasTextShadow { get; set; } = true;
        public Color ShadowColor { get; set; } = Colors.Black;
        public double ShadowBlurRadius { get; set; } = 8.0;
        public double ShadowOpacity { get; set; } = 0.6;

        public static OverlayStyle CreatePreset(OverlayTheme theme, OverlayMode mode)
        {
            var style = new OverlayStyle
            {
                Theme = theme,
                Mode = mode
            };

            // 1. Áp dụng bảng màu theo Theme
            switch (theme)
            {
                case OverlayTheme.Light:
                    style.BackgroundBrush = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
                    style.BorderBrush = new SolidColorBrush(Color.FromArgb(200, 226, 232, 240));
                    style.TextBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42)); // Slate 900
                    style.SecondaryTextBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Slate 500
                    style.SpeakerBadgeBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue 500
                    style.SpeakerTextBrush = Brushes.White;
                    style.DebugTextBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    style.ShadowColor = Color.FromRgb(148, 163, 184);
                    style.ShadowOpacity = 0.25;
                    style.CardOpacity = 0.94;
                    break;

                case OverlayTheme.FgoChaldea:
                    // Chaldea deep navy + cyber blue glow + gold accent
                    style.BackgroundBrush = new SolidColorBrush(Color.FromArgb(235, 11, 19, 43)); // Deep Chaldea navy
                    style.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 56, 189, 248)); // Sky 400 glow
                    style.TextBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));
                    style.SecondaryTextBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                    style.SpeakerBadgeBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber Gold
                    style.SpeakerTextBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
                    style.DebugTextBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                    style.ShadowColor = Color.FromRgb(2, 6, 23);
                    style.ShadowOpacity = 0.75;
                    style.CardOpacity = 0.88;
                    break;

                case OverlayTheme.HighContrast:
                    style.BackgroundBrush = new SolidColorBrush(Color.FromArgb(250, 0, 0, 0)); // Pitch Black
                    style.BorderBrush = new SolidColorBrush(Color.FromRgb(250, 204, 21)); // Vibrant Yellow border
                    style.TextBrush = new SolidColorBrush(Color.FromRgb(250, 204, 21)); // Vibrant Yellow text
                    style.SecondaryTextBrush = Brushes.White;
                    style.SpeakerBadgeBrush = new SolidColorBrush(Color.FromRgb(250, 204, 21));
                    style.SpeakerTextBrush = Brushes.Black;
                    style.DebugTextBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    style.ShadowColor = Colors.Black;
                    style.ShadowOpacity = 0.9;
                    style.BorderThickness = new Thickness(2.0);
                    style.FontWeight = FontWeights.Bold;
                    style.CardOpacity = 0.96;
                    break;

                case OverlayTheme.Dark:
                default:
                    style.BackgroundBrush = new SolidColorBrush(Color.FromArgb(235, 17, 24, 39)); // Gray 900
                    style.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 75, 85, 99)); // Gray 600
                    style.TextBrush = new SolidColorBrush(Color.FromRgb(249, 250, 251));
                    style.SecondaryTextBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));
                    style.SpeakerBadgeBrush = new SolidColorBrush(Color.FromRgb(139, 92, 246)); // Purple 500
                    style.SpeakerTextBrush = Brushes.White;
                    style.DebugTextBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    style.ShadowColor = Colors.Black;
                    style.ShadowOpacity = 0.6;
                    style.CardOpacity = 0.88;
                    break;
            }

            // 2. Tinh chỉnh theo Mode (Overlay vs Layer)
            if (mode == OverlayMode.Layer)
            {
                // Chế độ Layer: HUD trong suốt tối giản, phụ đề hòa vào game không viền
                style.CardOpacity = 0.50;
                style.BorderThickness = new Thickness(0);
                style.CornerRadius = new CornerRadius(6);
                style.Padding = new Thickness(12, 8, 12, 8);
                style.HasTextShadow = true;
                style.ShadowBlurRadius = 10.0;
                style.ShadowOpacity = 0.9;
                style.ShadowColor = Colors.Black;

                if (theme != OverlayTheme.HighContrast)
                {
                    // Nền mờ khói đen để nổi chữ trên nền game
                    style.BackgroundBrush = new SolidColorBrush(Color.FromArgb(140, 10, 10, 15));
                }
            }

            return style;
        }

        public DropShadowEffect GetTextDropShadow()
        {
            return new DropShadowEffect
            {
                Color = ShadowColor,
                BlurRadius = ShadowBlurRadius,
                Opacity = ShadowOpacity,
                Direction = 270,
                ShadowDepth = 1.5
            };
        }
    }
}
