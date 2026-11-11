using Avalonia;
using Avalonia.Threading;
using PixelTrace.Desktop.Diagnostics;

namespace PixelTrace.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), "unhandled runtime exception");
        Dispatcher.UIThread.UnhandledException += (_, e) => CrashLog.Write(e.Exception, "unhandled UI exception");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
