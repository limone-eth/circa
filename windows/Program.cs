namespace Circa;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "CircaSingleInstance", out bool isFirst);
        if (!isFirst) return; // already running

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
