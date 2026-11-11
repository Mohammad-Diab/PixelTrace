using System.Runtime.InteropServices;

namespace PixelTrace.Desktop.Diagnostics;

internal static class CrashLog
{
    private static readonly object Gate = new();

    public static void Write(Exception exception, string source)
    {
        // Fatal failures must leave the managed stack, not just Windows' generic CLR exception code.
        try
        {
            string folder = Environment.GetEnvironmentVariable("PIXELTRACE_LOG_DIRECTORY")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelTrace", "Logs");
            lock (Gate)
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "crash.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} | {source} | PID {Environment.ProcessId} | Version {typeof(CrashLog).Assembly.GetName().Version}\n" +
                    $"{RuntimeInformation.FrameworkDescription} | {RuntimeInformation.OSDescription}\n{exception}\n\n");
            }
        }
        catch { /* Diagnostics must never replace the original failure with a logging exception. */ }
    }
}
