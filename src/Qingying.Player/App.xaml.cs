using System.IO;
using System.Windows;
using LibVLCSharp.Shared;

namespace Qingying;

public partial class App : Application
{
    private InstanceBroker? _broker;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            WriteError(args.Exception);
            MessageBox.Show(args.Exception.Message, "轻影播放器", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _broker = new InstanceBroker(e.Args);
        if (!_broker.IsPrimary)
        {
            _broker.Dispose();
            Shutdown(0);
            return;
        }

        try
        {
            LibVLCSharp.Shared.Core.Initialize();
        }
        catch (Exception ex)
        {
            WriteError(ex);
            MessageBox.Show("播放引擎初始化失败。" + Environment.NewLine + ex.Message, "轻影播放器", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var settings = new SettingsStore().Load();
        var window = new MainWindow(settings, e.Args, _broker);
        MainWindow = window;
        window.Show();
        _broker.StartListening();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _broker?.Dispose();
        base.OnExit(e);
    }

    private static void WriteError(Exception exception)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Qingying");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
