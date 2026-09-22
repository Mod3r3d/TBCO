using System;
using System.IO;
using System.Windows;

namespace TranslateBot;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                File.AppendAllText("crash.log", $"[{DateTime.Now}] DispatcherException: {args.Exception}\n");
                MessageBox.Show($"Đã xảy ra lỗi khởi động:\n{args.Exception.Message}\n\nChi tiết xem trong crash.log", 
                    "TBCO Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try
            {
                File.AppendAllText("crash.log", $"[{DateTime.Now}] UnhandledException: {args.ExceptionObject}\n");
            }
            catch { }
        };

        try
        {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        }
        catch { }
    }
}

