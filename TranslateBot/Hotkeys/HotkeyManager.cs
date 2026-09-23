using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using TranslateBot.Capture;
using TranslateBot.Infrastructure;

namespace TranslateBot.Hotkeys
{
    public class HotkeyManager : IDisposable
    {
        private IntPtr _hWnd = IntPtr.Zero;
        private HwndSource? _hwndSource;
        private readonly Dictionary<int, HotkeyBinding> _registeredById = new();
        private readonly List<HotkeyBinding> _bindings = new();
        private int _nextId = 1000;
        private bool _isDisposed;

        public event Action<HotkeyCommand>? OnCommandTriggered;

        public IReadOnlyList<HotkeyBinding> Bindings => _bindings.AsReadOnly();

        public HotkeyManager()
        {
            LoadDefaultBindings();
        }

        public void LoadDefaultBindings()
        {
            _bindings.Clear();

            // Capture
            AddBinding(HotkeyCommand.ToggleCapture, Key.F6, ModifierKeys.None, "Bật/Tắt chế độ quét liên tục (Hook loop)");
            AddBinding(HotkeyCommand.CaptureOnce, Key.F7, ModifierKeys.None, "Chụp và dịch 1 khung hình");
            AddBinding(HotkeyCommand.Snapshot, Key.F8, ModifierKeys.None, "Chụp nhanh lưu ảnh & dịch tức thì (Snapshot)");
            AddBinding(HotkeyCommand.SelectRegion, Key.F8, ModifierKeys.Control, "Chọn vùng chụp OCR");
            AddBinding(HotkeyCommand.LockOverlay, Key.F9, ModifierKeys.None, "Khóa/Mở khóa xuyên chuột phụ đề (Click-through)");

            // Overlay & Subtitles
            AddBinding(HotkeyCommand.ToggleOverlay, Key.F10, ModifierKeys.None, "Ẩn/Hiện thanh phụ đề nổi (Overlay HUD)");
            AddBinding(HotkeyCommand.DetachSubtitle, Key.F11, ModifierKeys.None, "Tách / Gắn lại cửa sổ phụ đề nổi độc lập");
            AddBinding(HotkeyCommand.ToggleClickThrough, Key.H, ModifierKeys.Control | ModifierKeys.Shift, "Bật/Tắt xuyên chuột nâng cao");

            // Dialogue & Translation
            AddBinding(HotkeyCommand.Retranslate, Key.T, ModifierKeys.Control, "Dịch lại câu thoại gần nhất");
            AddBinding(HotkeyCommand.ToggleStabilizer, Key.D, ModifierKeys.Control, "Bật/Tắt bộ ổn định thoại");
            AddBinding(HotkeyCommand.FinalizeDialogue, Key.D, ModifierKeys.Control | ModifierKeys.Shift, "Chốt câu thoại ngay lập tức");
            AddBinding(HotkeyCommand.SkipDialogue, Key.D, ModifierKeys.Alt, "Bỏ qua câu thoại hiện tại");

            // API, Audio & Diagnostics
            AddBinding(HotkeyCommand.OpenApiKeyManager, Key.K, ModifierKeys.Control, "Mở quản lý API Key Pool");
            AddBinding(HotkeyCommand.ToggleTTS, Key.Y, ModifierKeys.Control, "Đọc to câu thoại bằng giọng nói (TTS)");
            AddBinding(HotkeyCommand.ToggleLocalAI, Key.L, ModifierKeys.Control, "Bật/Tắt Local AI (Ollama)");
            AddBinding(HotkeyCommand.OpenHistory, Key.J, ModifierKeys.Control, "Mở lịch sử dịch thuật");
            AddBinding(HotkeyCommand.OpenDiagnostics, Key.F12, ModifierKeys.Control, "Mở bảng chẩn đoán hiệu năng");

            // Cheatsheet Help
            AddBinding(HotkeyCommand.OpenHotkeyHelp, Key.F1, ModifierKeys.None, "Mở bảng tra cứu phím tắt");
        }

        private void AddBinding(HotkeyCommand command, Key key, ModifierKeys modifiers, string description)
        {
            _bindings.Add(new HotkeyBinding(command, key, modifiers, description));
        }

        public void Initialize(IntPtr hWnd)
        {
            if (_hWnd != IntPtr.Zero && _hWnd != hWnd)
            {
                UnregisterAll();
            }

            _hWnd = hWnd;
            _hwndSource = HwndSource.FromHwnd(_hWnd);
            _hwndSource?.AddHook(WndProc);

            RegisterAll();
        }

        public bool Rebind(HotkeyCommand command, Key newKey, ModifierKeys newModifiers)
        {
            var binding = _bindings.FirstOrDefault(b => b.Command == command);
            if (binding == null)
            {
                binding = new HotkeyBinding(command, newKey, newModifiers);
                _bindings.Add(binding);
            }
            else
            {
                if (_hWnd != IntPtr.Zero && binding.RegistrationId > 0)
                {
                    Unregister(binding);
                }
                binding.Key = newKey;
                binding.Modifiers = newModifiers;
            }

            // Kiểm tra xung đột
            var conflicts = HotkeyConflictDetector.DetectConflicts(_bindings);
            if (conflicts.Count > 0)
            {
                AppLogger.Warn($"[HOTKEY_CONFLICT] Phát hiện xung đột khi gán {binding.DisplayText}: {conflicts[0].Reason}");
            }

            if (_hWnd != IntPtr.Zero && binding.IsEnabled)
            {
                return Register(binding);
            }

            return true;
        }

        public void RegisterAll()
        {
            if (_hWnd == IntPtr.Zero) return;

            UnregisterAll();

            foreach (var binding in _bindings.Where(b => b.IsEnabled && b.Key != Key.None))
            {
                Register(binding);
            }
        }

        public bool Register(HotkeyBinding binding)
        {
            if (_hWnd == IntPtr.Zero || !binding.IsEnabled || binding.Key == Key.None) return false;

            if (binding.RegistrationId == 0)
            {
                binding.RegistrationId = ++_nextId;
            }

            uint mod = binding.GetWin32Modifiers() | 0x4000; // MOD_NOREPEAT (Windows 7+)
            uint vk = binding.GetWin32VirtualKey();

            if (vk == 0) return false;

            bool success = NativeMethods.RegisterHotKey(_hWnd, binding.RegistrationId, mod, vk);
            if (success)
            {
                _registeredById[binding.RegistrationId] = binding;
                AppLogger.Info($"[HOTKEY_REGISTERED] [{binding.Command}] -> {binding.DisplayText} (ID: {binding.RegistrationId})");
            }
            else
            {
                AppLogger.Warn($"[HOTKEY_FAILED] Không thể đăng ký phím {binding.DisplayText} cho lệnh {binding.Command} (có thể bị ứng dụng khác chiếm giữ).");
            }
            return success;
        }

        public void Unregister(HotkeyBinding binding)
        {
            if (_hWnd == IntPtr.Zero || binding.RegistrationId == 0) return;

            NativeMethods.UnregisterHotKey(_hWnd, binding.RegistrationId);
            _registeredById.Remove(binding.RegistrationId);
        }

        public void UnregisterAll()
        {
            if (_hWnd == IntPtr.Zero) return;

            foreach (var kvp in _registeredById)
            {
                NativeMethods.UnregisterHotKey(_hWnd, kvp.Key);
            }
            _registeredById.Clear();
        }

        public IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_registeredById.TryGetValue(id, out var binding))
                {
                    AppLogger.Info($"[HOTKEY_TRIGGERED] {binding.Command} ({binding.DisplayText})");
                    OnCommandTriggered?.Invoke(binding.Command);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            UnregisterAll();
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }
            _hWnd = IntPtr.Zero;
        }
    }
}
