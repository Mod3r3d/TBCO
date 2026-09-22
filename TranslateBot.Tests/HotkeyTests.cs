using System.Linq;
using System.Windows.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Hotkeys;

namespace TranslateBot.Tests
{
    [TestClass]
    public class HotkeyTests
    {
        [TestMethod]
        public void HotkeyBinding_DisplayText_FormatsCorrectly()
        {
            var binding1 = new HotkeyBinding(HotkeyCommand.FinalizeDialogue, Key.D, ModifierKeys.Control | ModifierKeys.Shift);
            Assert.AreEqual("Ctrl+Shift+D", binding1.DisplayText);

            var binding2 = new HotkeyBinding(HotkeyCommand.Snapshot, Key.F8);
            Assert.AreEqual("F8", binding2.DisplayText);

            var binding3 = new HotkeyBinding(HotkeyCommand.EmergencyStop, Key.Escape, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
            Assert.AreEqual("Ctrl+Alt+Shift+Escape", binding3.DisplayText);
        }

        [TestMethod]
        public void HotkeyBinding_Win32ModifiersAndKeys_ConvertAccurately()
        {
            var binding = new HotkeyBinding(HotkeyCommand.ToggleCapture, Key.F6, ModifierKeys.Control | ModifierKeys.Alt);
            uint mod = binding.GetWin32Modifiers();
            // MOD_ALT = 0x01, MOD_CONTROL = 0x02 -> 0x03
            Assert.AreEqual(0x03u, mod);

            uint vk = binding.GetWin32VirtualKey();
            Assert.IsTrue(vk > 0, "Virtual key code phải hợp lệ (> 0)");
        }

        [TestMethod]
        public void HotkeyConflictDetector_DuplicateHotkeys_ReportsConflict()
        {
            var bindings = new[]
            {
                new HotkeyBinding(HotkeyCommand.Snapshot, Key.F8, ModifierKeys.None),
                new HotkeyBinding(HotkeyCommand.SelectRegion, Key.F8, ModifierKeys.None)
            };

            var conflicts = HotkeyConflictDetector.DetectConflicts(bindings);
            Assert.AreEqual(1, conflicts.Count);
            Assert.IsTrue(conflicts[0].Reason.Contains("Xung đột phím"));
        }

        [TestMethod]
        public void HotkeyConflictDetector_SystemReservedHotkeys_ReportsWarning()
        {
            var bindings = new[]
            {
                new HotkeyBinding(HotkeyCommand.ToggleOverlay, Key.F4, ModifierKeys.Alt), // Alt+F4
                new HotkeyBinding(HotkeyCommand.LockOverlay, Key.L, ModifierKeys.Windows)  // Win+L
            };

            var conflicts = HotkeyConflictDetector.DetectConflicts(bindings);
            Assert.AreEqual(2, conflicts.Count);
            Assert.IsTrue(conflicts.All(c => c.Reason.Contains("hệ điều hành Windows")));
        }

        [TestMethod]
        public void HotkeyManager_DefaultBindings_ContainsEssentialCommands()
        {
            var manager = new HotkeyManager();
            var bindings = manager.Bindings;

            Assert.IsTrue(bindings.Any(b => b.Command == HotkeyCommand.Snapshot && b.Key == Key.F8));
            Assert.IsTrue(bindings.Any(b => b.Command == HotkeyCommand.LockOverlay && b.Key == Key.F9));
            Assert.IsTrue(bindings.Any(b => b.Command == HotkeyCommand.ToggleCapture && b.Key == Key.F6));
            Assert.IsTrue(bindings.Any(b => b.Command == HotkeyCommand.FinalizeDialogue && b.Key == Key.D));
            Assert.IsTrue(bindings.Any(b => b.Command == HotkeyCommand.OpenApiKeyManager && b.Key == Key.K));
        }

        [TestMethod]
        public void HotkeyManager_Rebind_UpdatesBindingCorrectly()
        {
            var manager = new HotkeyManager();
            bool success = manager.Rebind(HotkeyCommand.Snapshot, Key.F11, ModifierKeys.Control);

            Assert.IsTrue(success);
            var snapshotBinding = manager.Bindings.First(b => b.Command == HotkeyCommand.Snapshot);
            Assert.AreEqual(Key.F11, snapshotBinding.Key);
            Assert.AreEqual(ModifierKeys.Control, snapshotBinding.Modifiers);
            Assert.AreEqual("Ctrl+F11", snapshotBinding.DisplayText);
        }
    }
}
