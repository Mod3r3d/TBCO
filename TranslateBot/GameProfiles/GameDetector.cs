using System;
using System.Collections.Generic;
using System.Linq;
using TranslateBot.Capture;

namespace TranslateBot.GameProfiles
{
    /// <summary>
    /// Bộ tự động phát hiện cửa sổ và tiến trình game (Section 15 của Master Plan)
    /// </summary>
    public static class GameDetector
    {
        // Quét tất cả cửa sổ desktop và tự động chọn hồ sơ game trùng khớp nhất
        public static (GameProfile? Profile, WindowInfo? Window) DetectActiveGame(IEnumerable<GameProfile> profiles)
        {
            var windows = WindowEnumerator.GetVisibleWindows();
            return DetectFromWindows(profiles, windows);
        }

        // Phương thức cho phép truyền danh sách cửa sổ (thuận tiện cho unit test)
        public static (GameProfile? Profile, WindowInfo? Window) DetectFromWindows(
            IEnumerable<GameProfile> profiles,
            IEnumerable<WindowInfo> windows)
        {
            if (profiles == null || windows == null) return (null, null);

            GameProfile? bestProfile = null;
            WindowInfo? bestWindow = null;
            int highestScore = 0;

            foreach (var profile in profiles)
            {
                foreach (var win in windows)
                {
                    int score = CalculateMatchScore(profile, win);
                    if (score > highestScore)
                    {
                        highestScore = score;
                        bestProfile = profile;
                        bestWindow = win;
                    }
                }
            }

            if (highestScore > 0)
            {
                return (bestProfile, bestWindow);
            }

            return (null, null);
        }

        public static int CalculateMatchScore(GameProfile profile, WindowInfo window)
        {
            if (profile == null || window == null) return 0;

            int score = 0;

            // 1. So khớp Title
            if (!string.IsNullOrWhiteSpace(profile.TitleMatch) && !string.IsNullOrWhiteSpace(window.Title))
            {
                var titleTokens = profile.TitleMatch.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in titleTokens)
                {
                    if (window.Title.Contains(token.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        score += 15;
                        break;
                    }
                }
            }

            // 2. So khớp ProcessName
            if (!string.IsNullOrWhiteSpace(profile.ProcessMatch) && !string.IsNullOrWhiteSpace(window.ProcessName))
            {
                var procTokens = profile.ProcessMatch.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in procTokens)
                {
                    if (window.ProcessName.Contains(token.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        score += 10;
                        break;
                    }
                }
            }

            return score;
        }
    }
}
