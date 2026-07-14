using System.Diagnostics;

namespace Circa;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Relaunched by the updater: let the old instance release the
        // single-instance mutex before trying to take it.
        int waitArg = Array.IndexOf(args, "--wait-pid");
        if (waitArg >= 0 && waitArg + 1 < args.Length && int.TryParse(args[waitArg + 1], out int oldPid))
        {
            try { Process.GetProcessById(oldPid).WaitForExit(15000); }
            catch { /* already gone */ }
        }

        using var mutex = new Mutex(true, "CircaSingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "Circa is already running. Look for the ring icon in the system tray " +
                "(click the ^ arrow near the clock if it's hidden).",
                "Circa", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // A tray app with a silent startup crash looks like "nothing happens".
        // Make every failure visible.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.ToString(), "Circa error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MessageBox.Show(e.ExceptionObject?.ToString() ?? "unknown error", "Circa error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new TrayContext());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Circa failed to start",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
