using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace TranslateBot.Hotkeys
{
    public class HotkeyConflict
    {
        public HotkeyBinding PrimaryBinding { get; set; } = null!;
        public HotkeyBinding ConflictingBinding { get; set; } = null!;
        public string Reason { get; set; } = string.Empty;
    }

    public static class HotkeyConflictDetector
    {
        public static List<HotkeyConflict> DetectConflicts(IEnumerable<HotkeyBinding> bindings)
        {
            var conflicts = new List<HotkeyConflict>();
            var activeBindings = bindings.Where(b => b.IsEnabled && b.Key != Key.None).ToList();

            for (int i = 0; i < activeBindings.Count; i++)
            {
                var a = activeBindings[i];

                // Kiểm tra phím cấm hệ thống
                if (IsSystemReserved(a))
                {
                    conflicts.Add(new HotkeyConflict
                    {
                        PrimaryBinding = a,
                        ConflictingBinding = a,
                        Reason = $"Tổ hợp phím {a.DisplayText} là phím tắt mặc định bị hệ điều hành Windows giữ riêng."
                    });
                }

                for (int j = i + 1; j < activeBindings.Count; j++)
                {
                    var b = activeBindings[j];
                    if (a.Key == b.Key && a.Modifiers == b.Modifiers)
                    {
                        conflicts.Add(new HotkeyConflict
                        {
                            PrimaryBinding = a,
                            ConflictingBinding = b,
                            Reason = $"Xung đột phím: Cả [{a.Command}] và [{b.Command}] đều dùng tổ hợp {a.DisplayText}."
                        });
                    }
                }
            }

            return conflicts;
        }

        private static bool IsSystemReserved(HotkeyBinding binding)
        {
            // Windows Lock (Win+L)
            if (binding.Key == Key.L && (binding.Modifiers & ModifierKeys.Windows) != 0) return true;

            // Alt+Tab
            if (binding.Key == Key.Tab && (binding.Modifiers & ModifierKeys.Alt) != 0) return true;

            // Alt+F4
            if (binding.Key == Key.F4 && (binding.Modifiers & ModifierKeys.Alt) != 0) return true;

            return false;
        }
    }
}
