using System;
using System.Collections.Generic;
using TranslateBot.Translation;

namespace TranslateBot.GameProfiles
{
    /// <summary>
    /// Hồ sơ phân cảnh / chương hồi trong game (Section 15 & 10 của Master Plan)
    /// Hỗ trợ bối cảnh: Lostbelt, Chaldea, Singularity, Event, Battle, v.v.
    /// </summary>
    public class SceneProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SpeakingStyleContext { get; set; } = string.Empty;
        public List<string> ActiveCharacters { get; set; } = new();
        public List<GlossaryEntry> GlossaryOverrides { get; set; } = new();
    }
}
