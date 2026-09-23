using System.Windows;
using System.Windows.Input;

namespace TranslateBot.UI
{
    public partial class HotkeyCheatsheetWindow : Window
    {
        public HotkeyCheatsheetWindow()
        {
            InitializeComponent();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.F1)
            {
                Close();
            }
        }
    }
}
