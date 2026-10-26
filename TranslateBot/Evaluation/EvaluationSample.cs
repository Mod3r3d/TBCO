using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TranslateBot.Dialogue;
using TranslateBot.GameProfiles;
using TranslateBot.Infrastructure;
using TranslateBot.Translation;

namespace TranslateBot.Evaluation
{
    public class EvaluationSample
    {
        [JsonProperty("sampleId")]
        public string SampleId { get; set; } = "";

        [JsonProperty("gameId")]
        public string GameId { get; set; } = "";

        [JsonProperty("sceneId")]
        public string SceneId { get; set; } = "";

        [JsonProperty("speakerId")]
        public string SpeakerId { get; set; } = "";

        [JsonProperty("sourceLanguage")]
        public string SourceLanguage { get; set; } = "ja";

        [JsonProperty("targetLanguage")]
        public string TargetLanguage { get; set; } = "vi";

        [JsonProperty("ocrRaw")]
        public string OcrRaw { get; set; } = "";

        [JsonProperty("sourceCleaned")]
        public string SourceCleaned { get; set; } = "";

        [JsonProperty("speakerProfile")]
        public EvaluationSpeakerProfile? SpeakerProfile { get; set; }

        [JsonProperty("previousLines")]
        public List<EvaluationPreviousLine> PreviousLines { get; set; } = new();

        [JsonProperty("glossary")]
        public List<EvaluationGlossaryItem> Glossary { get; set; } = new();

        [JsonProperty("lockedTerms")]
        public List<string> LockedTerms { get; set; } = new();

        [JsonProperty("forbiddenTerms")]
        public List<string> ForbiddenTerms { get; set; } = new();

        [JsonProperty("expectedPronounPairs")]
        public List<List<string>> ExpectedPronounPairs { get; set; } = new();

        [JsonProperty("referenceTranslations")]
        public List<string> ReferenceTranslations { get; set; } = new();

        [JsonProperty("category")]
        public string Category { get; set; } = "";

        [JsonProperty("isOfficialReference")]
        public bool IsOfficialReference { get; set; }

        [JsonProperty("notes")]
        public string Notes { get; set; } = "";

        /// <summary>
        /// Chuyển đổi mẫu đánh giá thành TranslationContext chính thống của app TBCO (Section 25 & Stage 1)
        /// để đảm bảo runner dùng chung 100% logic xây dựng prompt như ứng dụng thật.
        /// </summary>
        public TranslationContext ToTranslationContext()
        {
            var context = new TranslationContext
            {
                GameName = GameId,
                SceneContext = SceneId,
                SpeakerName = SpeakerProfile?.Name ?? SpeakerId
            };

            if (SpeakerProfile != null)
            {
                context.SpeakerProfile = new CharacterProfile
                {
                    Name = SpeakerProfile.Name,
                    SpeakingStyle = SpeakerProfile.SpeakingStyle,
                    PreferredPronouns = SpeakerProfile.PreferredPronouns,
                    AddressingRules = SpeakerProfile.AddressingRules
                };
            }

            foreach (var line in PreviousLines)
            {
                context.PreviousLines.Add(new ContextLine
                {
                    Speaker = line.Speaker,
                    Original = line.Original,
                    Translated = line.Translated
                });
            }

            foreach (var g in Glossary)
            {
                var level = string.Equals(g.Level, "Locked", StringComparison.OrdinalIgnoreCase)
                    ? GlossaryLevel.Locked
                    : GlossaryLevel.Preferred;

                context.ActiveGlossary.Add(new GlossaryEntry
                {
                    Source = g.Source,
                    Target = g.Target,
                    Level = level
                });
            }

            return context;
        }
    }

    public class EvaluationSpeakerProfile
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("speakingStyle")]
        public string SpeakingStyle { get; set; } = "";

        [JsonProperty("preferredPronouns")]
        public string PreferredPronouns { get; set; } = "";

        [JsonProperty("addressingRules")]
        public string AddressingRules { get; set; } = "";
    }

    public class EvaluationPreviousLine
    {
        [JsonProperty("speaker")]
        public string Speaker { get; set; } = "";

        [JsonProperty("original")]
        public string Original { get; set; } = "";

        [JsonProperty("translated")]
        public string Translated { get; set; } = "";
    }

    public class EvaluationGlossaryItem
    {
        [JsonProperty("source")]
        public string Source { get; set; } = "";

        [JsonProperty("target")]
        public string Target { get; set; } = "";

        [JsonProperty("level")]
        public string Level { get; set; } = "Locked";
    }
}
