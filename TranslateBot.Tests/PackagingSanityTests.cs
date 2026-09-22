using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.GameProfiles;
using TranslateBot.Infrastructure;

namespace TranslateBot.Tests
{
    [TestClass]
    public class PackagingSanityTests
    {
        private static string GetProjectRoot()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            // Di chuyển lên từ bin/Debug/net8.0-... để tìm thư mục gốc TranslateBot
            var dir = new DirectoryInfo(current);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TranslateBot.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? current;
        }

        [TestMethod]
        public void AssemblyMetadata_HasVersion3()
        {
            var asm = typeof(TranslationEngine).Assembly;
            var ver = asm.GetName().Version;
            Assert.IsNotNull(ver);
            Assert.AreEqual(3, ver.Major, "Major version must be 3 for TBCO 3.0");

            var prodAttr = asm.GetCustomAttribute<AssemblyProductAttribute>();
            Assert.IsNotNull(prodAttr);
            Assert.IsTrue(prodAttr.Product.Contains("TBCO"), $"Product name should contain 'TBCO', got: {prodAttr.Product}");
            Assert.IsTrue(prodAttr.Product.Contains("Translate Bot Conversation by OCR"), $"Product name should contain 'Translate Bot Conversation by OCR', got: {prodAttr.Product}");
        }

        [TestMethod]
        public void DefaultProfiles_AreValidJson_AndMatchSchema()
        {
            string root = GetProjectRoot();
            string profilesDir = Path.Combine(root, "TranslateBot", "profiles");

            Assert.IsTrue(Directory.Exists(profilesDir), $"Profiles directory must exist at {profilesDir}");

            string[] expectedProfiles = { "fgo-jp.json", "fgo-na.json", "blue-archive.json", "vn-generic.json" };
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };

            foreach (var profFile in expectedProfiles)
            {
                string fullPath = Path.Combine(profilesDir, profFile);
                Assert.IsTrue(File.Exists(fullPath), $"Profile file {profFile} must exist");

                string json = File.ReadAllText(fullPath);
                var profile = JsonSerializer.Deserialize<GameProfile>(json, opts);

                Assert.IsNotNull(profile, $"Failed to deserialize {profFile}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(profile.Id));
                Assert.IsFalse(string.IsNullOrWhiteSpace(profile.Name));
                Assert.IsFalse(string.IsNullOrWhiteSpace(profile.SourceLanguage));
            }

            // Kiểm tra chi tiết FGO-JP
            string fgoJson = File.ReadAllText(Path.Combine(profilesDir, "fgo-jp.json"));
            var fgo = JsonSerializer.Deserialize<GameProfile>(fgoJson, opts)!;
            Assert.AreEqual("fgo-jp", fgo.Id);
            Assert.AreEqual("Fate/Grand Order", fgo.GameName);
            Assert.IsTrue(fgo.OcrAreas.Count >= 2, "FGO JP must have dialogue and speaker areas");
            Assert.IsTrue(fgo.Characters.Count >= 4, "FGO JP must have main Chaldea characters");

            // Kiểm tra chi tiết Blue Archive
            string baJson = File.ReadAllText(Path.Combine(profilesDir, "blue-archive.json"));
            var ba = JsonSerializer.Deserialize<GameProfile>(baJson, opts)!;
            Assert.AreEqual("blue-archive", ba.Id);
            Assert.AreEqual("Blue Archive", ba.GameName);
            Assert.IsTrue(ba.Glossary.Exists(g => g.Source == "Sensei"), "Blue Archive must have Sensei in glossary");
        }

        [TestMethod]
        public void ConfigTemplate_IsValidJson_AndHasRequiredKeys()
        {
            string root = GetProjectRoot();
            string templatePath = Path.Combine(root, "TranslateBot", "config.template.json");

            Assert.IsTrue(File.Exists(templatePath), $"config.template.json must exist at {templatePath}");

            string json = File.ReadAllText(templatePath);
            using var doc = JsonDocument.Parse(json);
            var rootEl = doc.RootElement;

            Assert.IsTrue(rootEl.TryGetProperty("Version", out var verEl));
            Assert.AreEqual("3.0.0", verEl.GetString());

            Assert.IsTrue(rootEl.TryGetProperty("AppConfig", out _));
            Assert.IsTrue(rootEl.TryGetProperty("TranslationProviders", out var provEl));
            Assert.IsTrue(provEl.TryGetProperty("Gemini", out _));
            Assert.IsTrue(provEl.TryGetProperty("DeepL", out _));
            Assert.IsTrue(provEl.TryGetProperty("CustomApi", out _));
            Assert.IsTrue(provEl.TryGetProperty("Ollama", out _));

            Assert.IsTrue(rootEl.TryGetProperty("Hotkeys", out _));
        }

        [TestMethod]
        public void TessdataReadme_Exists_AndProvidesGuidance()
        {
            string root = GetProjectRoot();
            string readmePath = Path.Combine(root, "TranslateBot", "tessdata", "README.txt");

            Assert.IsTrue(File.Exists(readmePath), $"tessdata/README.txt must exist at {readmePath}");
            string content = File.ReadAllText(readmePath);

            Assert.IsTrue(content.Contains("tessdata", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(content.Contains("traineddata", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void PublishScript_Exists_AndIsConfigured()
        {
            string root = GetProjectRoot();
            string scriptPath = Path.Combine(root, "publish.ps1");

            Assert.IsTrue(File.Exists(scriptPath), $"publish.ps1 must exist at {scriptPath}");
            string content = File.ReadAllText(scriptPath);

            Assert.IsTrue(content.Contains("TBCO-3.0-win-x64", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(content.Contains("dotnet publish", StringComparison.OrdinalIgnoreCase));
        }
    }
}
