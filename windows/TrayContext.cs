using Microsoft.Win32;

namespace Circa;

/// <summary>Tray icon + popover + the 5 s engine timer.</summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly Engine _engine = new();
    private readonly NotifyIcon _tray = new();
    private readonly PopoverForm _popover;
    private readonly System.Windows.Forms.Timer _timer = new();

    public TrayContext()
    {
        bool firstRun = !File.Exists(Settings.Path);
        _popover = new PopoverForm(_engine);

        _tray.Icon = LoadIcon();
        _tray.Text = "Circa";
        _tray.Visible = true;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) TogglePopover();
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Circa", null, (_, _) => TogglePopover());
        menu.Items.Add("Quit Circa", null, (_, _) => Application.Exit());
        _tray.ContextMenuStrip = menu;

        _timer.Interval = 5000;
        _timer.Tick += (_, _) => _engine.Tick(slew: true);
        _timer.Start();

        Application.ApplicationExit += (_, _) =>
        {
            _tray.Visible = false;
            _engine.Shutdown();
        };

        SystemEvents.PowerModeChanged += (_, _) => _engine.Tick(slew: false);
        SystemEvents.DisplaySettingsChanged += (_, _) => _engine.Tick(slew: false);

        _engine.Start();

        if (firstRun)
        {
            // A tray app that opens nothing on first launch reads as broken.
            _engine.Settings.Save();
            TogglePopover();
            _tray.BalloonTipTitle = "Circa is running";
            _tray.BalloonTipText = "It lives here in the tray. Click the ring icon anytime. " +
                                   "Your screen will warm automatically at sunset.";
            _tray.ShowBalloonTip(6000);
        }
    }

    private void TogglePopover()
    {
        if (_popover.Visible)
        {
            _popover.Hide();
            return;
        }
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 800);
        _popover.Location = new Point(
            Math.Max(0, screen.Right - _popover.Width - 12),
            Math.Max(0, screen.Bottom - _popover.Height - 12));
        _popover.SyncFromEngine();
        _popover.Show();
        _popover.Activate();
    }

    private static Icon LoadIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "circa.ico");
            if (File.Exists(path)) return new Icon(path);
            // Single-file publish: fall back to the icon embedded in the exe.
            var associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (associated != null) return associated;
        }
        catch { }
        return SystemIcons.Application;
    }
}
