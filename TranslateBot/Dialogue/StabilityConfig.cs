using System;

namespace TranslateBot.Dialogue
{
    public enum StabilityPreset
    {
        Fast,
        Balanced,
        Safe,
        Custom
    }

    // Backward-compatibility alias
    public enum StabilizationPreset
    {
        Fast,
        Balanced,
        Safe,
        Custom
    }

    /// <summary>
    /// Cấu hình ổn định thoại Progressive Dialogue 2.0 (TBCO Master Plan Section 7).
    /// </summary>
    public class StabilityConfig
    {
        public StabilityPreset Preset { get; set; } = StabilityPreset.Balanced;
        public double StableDurationMs { get; set; } = 300;
        public double SimilarityThreshold { get; set; } = 0.90;
        public double MaxWaitMs { get; set; } = 2000;
        public int MinTextLength { get; set; } = 3;
        public double EmptyGapMs { get; set; } = 1000;
        public bool EnableAdaptive { get; set; } = true;

        public static StabilityConfig Fast => new()
        {
            Preset = StabilityPreset.Fast,
            StableDurationMs = 150,
            SimilarityThreshold = 0.88,
            MaxWaitMs = 1200,
            MinTextLength = 2,
            EmptyGapMs = 800,
            EnableAdaptive = true
        };

        public static StabilityConfig Balanced => new()
        {
            Preset = StabilityPreset.Balanced,
            StableDurationMs = 300,
            SimilarityThreshold = 0.90,
            MaxWaitMs = 2000,
            MinTextLength = 4,
            EmptyGapMs = 1000,
            EnableAdaptive = true
        };

        public static StabilityConfig Safe => new()
        {
            Preset = StabilityPreset.Safe,
            StableDurationMs = 600,
            SimilarityThreshold = 0.92,
            MaxWaitMs = 3000,
            MinTextLength = 6,
            EmptyGapMs = 1200,
            EnableAdaptive = true
        };

        public static StabilityConfig CreatePreset(StabilityPreset preset) => preset switch
        {
            StabilityPreset.Fast => Fast,
            StabilityPreset.Safe => Safe,
            _ => Balanced
        };

        public static StabilityConfig CreatePreset(StabilizationPreset preset) => preset switch
        {
            StabilizationPreset.Fast => Fast,
            StabilizationPreset.Safe => Safe,
            _ => Balanced
        };

        public static StabilityConfig FromPreset(StabilizationPreset preset) => CreatePreset(preset);
        public static StabilityConfig FromPreset(StabilityPreset preset) => CreatePreset(preset);

        public int GetEffectiveStableDuration(string? text)
        {
            if (!EnableAdaptive || string.IsNullOrEmpty(text))
                return (int)StableDurationMs;

            double duration = StableDurationMs;
            int len = text.Length;

            // Typewriter compensation: văn bản dài cần thêm thời gian để máy gõ hết chữ
            if (len > 50) duration += 80;
            else if (len > 25) duration += 40;

            // Nếu câu kết thúc bằng dấu phẩy hoặc ba chấm, khả năng cao câu chưa dứt
            string trimmed = text.TrimEnd();
            if (trimmed.EndsWith("...") || trimmed.EndsWith("…") || trimmed.EndsWith(","))
            {
                duration += 60;
            }

            return (int)Math.Min(duration, MaxWaitMs);
        }
    }
}
