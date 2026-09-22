using System;
using System.Text;
using System.Windows.Input;

namespace TranslateBot.Hotkeys
{
    public class HotkeyBinding
    {
        public HotkeyCommand Command { get; set; }
        public Key Key { get; set; }
        public ModifierKeys Modifiers { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string Description { get; set; } = string.Empty;

        // Định danh duy nhất để đăng ký với Win32 RegisterHotKey
        public int RegistrationId { get; set; }

        public HotkeyBinding() { }

        public HotkeyBinding(HotkeyCommand command, Key key, ModifierKeys modifiers = ModifierKeys.None, string description = "", bool isEnabled = true)
        {
            Command = command;
            Key = key;
            Modifiers = modifiers;
            Description = description;
            IsEnabled = isEnabled;
        }

        public string DisplayText
        {
            get
            {
                if (Key == Key.None) return "None";

                var sb = new StringBuilder();
                if ((Modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
                if ((Modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt+");
                if ((Modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift+");
                if ((Modifiers & ModifierKeys.Windows) != 0) sb.Append("Win+");
                sb.Append(Key.ToString());
                return sb.ToString();
            }
        }

        public uint GetWin32Modifiers()
        {
            uint mod = 0;
            if ((Modifiers & ModifierKeys.Alt) != 0) mod |= 0x0001;     // MOD_ALT
            if ((Modifiers & ModifierKeys.Control) != 0) mod |= 0x0002; // MOD_CONTROL
            if ((Modifiers & ModifierKeys.Shift) != 0) mod |= 0x0004;   // MOD_SHIFT
            if ((Modifiers & ModifierKeys.Windows) != 0) mod |= 0x0008; // MOD_WIN
            return mod;
        }

        public uint GetWin32VirtualKey()
        {
            return (uint)KeyInterop.VirtualKeyFromKey(Key);
        }

        public override bool Equals(object? obj)
        {
            if (obj is HotkeyBinding other)
            {
                return Key == other.Key && Modifiers == other.Modifiers;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Key, Modifiers);
        }

        public override string ToString() => $"{Command}: {DisplayText}";
    }
}
