using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Dialogue;
using TranslateBot.Translation;

namespace TranslateBot.Tests
{
    [TestClass]
    public class SpeakerResolverAndContextTests
    {
        private CharacterProfileManager _profileManager = null!;
        private GlossaryManager _glossaryManager = null!;
        private SpeakerResolver _resolver = null!;

        [TestInitialize]
        public void Setup()
        {
            _profileManager = new CharacterProfileManager();
            // Nạp default profiles (Mash, Gilgamesh, Artoria, etc.)
            _profileManager.Add(new CharacterProfile
            {
                Name = "Mash Kyrielight",
                Aliases = new List<string> { "Mash", "Mashu", "マシュ", "Shielder" },
                SpeakingStyle = "Lịch sự, nhẹ nhàng",
                PreferredPronouns = "em / Senpai",
                AddressingRules = "Gọi Ritsuka là Senpai"
            });
            _profileManager.Add(new CharacterProfile
            {
                Name = "Gilgamesh",
                Aliases = new List<string> { "Gil", "King of Heroes", "ギルガメッシュ" },
                SpeakingStyle = "Ngạo nghễ, kiêu căng",
                PreferredPronouns = "ta / ngươi"
            });

            _glossaryManager = new GlossaryManager();
            _glossaryManager.Add(new GlossaryEntry
            {
                Source = "Servant",
                Target = "Servant",
                Level = GlossaryLevel.Locked,
                Tier = GlossaryTier.Game
            });
            _glossaryManager.Add(new GlossaryEntry
            {
                Source = "Noble Phantasm",
                Target = "Bảo Khí",
                Aliases = new List<string> { "NP", "宝具" },
                Level = GlossaryLevel.Locked,
                Tier = GlossaryTier.Game
            });
            _glossaryManager.Add(new GlossaryEntry
            {
                Source = "Senpai",
                Target = "tiền bối",
                Level = GlossaryLevel.Locked,
                Tier = GlossaryTier.Game
            });

            _resolver = new SpeakerResolver(_profileManager);
        }

        [TestMethod]
        public void SpeakerResolver_PrioritizesAreaOcr_OverInTextSyntax()
        {
            // Màn hình có Area OCR nhận diện là "Mash", nhưng trong text có "Gilgamesh: ..."
            string areaOcr = "Mash";
            string text = "Gilgamesh: Stay back!";

            var result = _resolver.Resolve(text, areaOcrSpeaker: areaOcr);

            Assert.IsTrue(result.HasSpeaker);
            Assert.AreEqual("Mash Kyrielight", result.Speaker);
            Assert.AreEqual("AreaOcr", result.Source);
            Assert.IsTrue(result.Confidence >= 0.95);
            Assert.IsNotNull(result.Profile);
            Assert.AreEqual("Stay back!", result.CleanedDialogue);
        }

        [TestMethod]
        public void SpeakerResolver_ResolvesBrackets_AndNormalizesProfile()
        {
            string text = "【Gilgamesh】Hmph, you mongrel.";

            var result = _resolver.Resolve(text);

            Assert.IsTrue(result.HasSpeaker);
            Assert.AreEqual("Gilgamesh", result.Speaker);
            Assert.AreEqual("InTextSyntax", result.Source);
            Assert.IsNotNull(result.Profile);
            Assert.AreEqual("Hmph, you mongrel.", result.CleanedDialogue);
        }

        [TestMethod]
        public void SpeakerResolver_ResolvesJapaneseQuotes_AndAliases()
        {
            string text = "マシュ「先輩、危ない！」";

            var result = _resolver.Resolve(text);

            Assert.IsTrue(result.HasSpeaker);
            Assert.AreEqual("Mash Kyrielight", result.Speaker, "Alias tiếng Nhật 'マシュ' phải chuẩn hóa về 'Mash Kyrielight'");
            Assert.AreEqual("先輩、危ない！", result.CleanedDialogue);
            Assert.IsNotNull(result.Profile);
        }

        [TestMethod]
        public void SpeakerResolver_ResolvesContinuity_WhenStandaloneQuoteFollows()
        {
            // Câu 1: Xác định Mash
            _resolver.Resolve("Mash: Senpai, are you ready?");

            // Câu 2: Chỉ có ngoặc thoại 「...」 không có tên người nói đứng trước
            var followUp = _resolver.Resolve("「We need to rayshift now!」");

            Assert.IsTrue(followUp.HasSpeaker);
            Assert.AreEqual("Mash Kyrielight", followUp.Speaker);
            Assert.AreEqual("HistoryContinuity", followUp.Source);
            Assert.IsTrue(followUp.Confidence >= 0.6);
            Assert.AreEqual("We need to rayshift now!", followUp.CleanedDialogue);
        }

        [TestMethod]
        public void GlossaryManager_MatchesAliases_AndCharacterOverrides()
        {
            // Văn bản có viết tắt "NP"
            string text = "Kích hoạt NP để kết liễu kẻ địch!";

            var characterOverrides = new List<GlossaryEntry>
            {
                new GlossaryEntry
                {
                    Source = "Senpai",
                    Target = "tiền bối đáng kính", // override cho nhân vật
                    Level = GlossaryLevel.Locked,
                    Tier = GlossaryTier.Character
                }
            };

            var matches = _glossaryManager.GetMatchingEntries(text, "Mash", characterOverrides);
            
            // "Noble Phantasm" có alias "NP" nên phải match
            var npMatch = matches.FirstOrDefault(m => m.Source == "Noble Phantasm");
            Assert.IsNotNull(npMatch, "Phải match thuật ngữ 'Noble Phantasm' thông qua alias 'NP'");
            Assert.AreEqual("Bảo Khí", npMatch.Target);
        }

        [TestMethod]
        public void FgoContextEngine_BuildContext_AssemblesHierarchicalContext()
        {
            var engine = new FgoContextEngine(_glossaryManager, _profileManager, _resolver)
            {
                GameName = "Fate/Grand Order",
                SceneContext = "Lostbelt 6"
            };

            var history = new DialogueHistory();
            history.Add(new DialogueEntry
            {
                OriginalText = "Where are we?",
                TranslatedText = "Chúng ta đang ở đâu?",
                Speaker = "Ritsuka"
            });

            string currentDialogue = "【Mash】Senpai, we have arrived at Fairy Kingdom.";

            var ctx = engine.BuildContext(currentDialogue, history: history);

            Assert.AreEqual("Fate/Grand Order", ctx.GameName);
            Assert.AreEqual("Lostbelt 6", ctx.SceneContext);
            Assert.AreEqual("Mash Kyrielight", ctx.SpeakerName);
            Assert.IsNotNull(ctx.SpeakerProfile);
            Assert.AreEqual("em / Senpai", ctx.SpeakerProfile.PreferredPronouns);

            // History sliding window
            Assert.AreEqual(1, ctx.PreviousLines.Count);
            Assert.AreEqual("Where are we?", ctx.PreviousLines[0].Original);
            Assert.AreEqual("Chúng ta đang ở đâu?", ctx.PreviousLines[0].Translated);

            // Active glossary has terms
            Assert.IsTrue(ctx.ActiveGlossary.Any(g => g.Source == "Senpai"));
        }
    }
}
