using Microsoft.Win32;

namespace Circa;

/// <summary>
/// The tray popover: a small dark warm panel echoing the macOS popover's
/// vocabulary (status line, Day/Night/Night-dim sliders, flicker-free with
/// brightness + only-on-power, launch at login, pause, reset).
/// Function-first WinForms; the design-polish pass happens on real hardware.
/// </summary>
public sealed class PopoverForm : Form
{
    private static readonly Color Bg = Color.FromArgb(23, 18, 16);
    private static readonly Color Card = Color.FromArgb(34, 27, 22);
    private static readonly Color Ink = Color.FromArgb(240, 224, 195);
    private static readonly Color Muted = Color.FromArgb(176, 152, 122);
    private static readonly Color Accent = Color.FromArgb(235, 172, 92);

    private readonly Engine _engine;

    private readonly Label _status = new();
    private readonly Label _place = new();
    private readonly CheckBox _enabled = new();
    private readonly TrackBar _day = new();
    private readonly Label _dayVal = new();
    private readonly TrackBar _night = new();
    private readonly Label _nightVal = new();
    private readonly TrackBar _dim = new();
    private readonly Label _dimVal = new();
    private readonly CheckBox _flicker = new();
    private readonly TrackBar _flickerBrightness = new();
    private readonly Label _flickerVal = new();
    private readonly CheckBox _onlyOnPower = new();
    private readonly CheckBox _launchAtLogin = new();
    private readonly Button _pauseHour = new();
    private readonly Button _pauseSunrise = new();
    private readonly Button _resume = new();
    private readonly LinkLabel _reset = new();

    private bool _updatingUi;

    public PopoverForm(Engine engine)
    {
        _engine = engine;

        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Text = "Circa";
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Bg;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(320, 520);
        Deactivate += (_, _) => Hide();

        int y = 14;
        y = AddHeader(y);
        y = AddSlider("Day", _day, _dayVal, 4800, 6500, y);
        y = AddSlider("Night", _night, _nightVal, 1900, 4500, y);
        y = AddSlider("Night dim", _dim, _dimVal, 0, 70, y);
        y = AddFlickerSection(y);
        y = AddToggles(y);
        y = AddPauseRow(y);
        AddFooter(y);

        _engine.Changed += () => { if (IsHandleCreated) BeginInvoke(SyncFromEngine); };
        SyncFromEngine();
    }

    // ------------------------------------------------------------ layout

    private int AddHeader(int y)
    {
        _status.SetBounds(16, y, 240, 22);
        _status.ForeColor = Ink;
        _status.Font = new Font("Segoe UI Semibold", 10.5f);
        Controls.Add(_status);

        _enabled.SetBounds(262, y, 44, 22);
        _enabled.Text = "on";
        _enabled.ForeColor = Muted;
        _enabled.CheckedChanged += (_, _) => { if (!_updatingUi) _engine.SetEnabled(_enabled.Checked); };
        Controls.Add(_enabled);

        _place.SetBounds(16, y + 24, 288, 18);
        _place.ForeColor = Muted;
        _place.Font = new Font("Segoe UI", 8.5f);
        Controls.Add(_place);
        return y + 52;
    }

    private int AddSlider(string title, TrackBar bar, Label value, int min, int max, int y)
    {
        var label = new Label { Text = title, ForeColor = Muted };
        label.SetBounds(16, y, 80, 18);
        Controls.Add(label);

        value.SetBounds(230, y, 76, 18);
        value.TextAlign = ContentAlignment.TopRight;
        value.ForeColor = Ink;
        Controls.Add(value);

        bar.SetBounds(10, y + 18, 300, 30);
        bar.Minimum = min;
        bar.Maximum = max;
        bar.TickStyle = TickStyle.None;
        bar.BackColor = Bg;
        bar.ValueChanged += (_, _) => { if (!_updatingUi) OnSliderChanged(bar); };
        Controls.Add(bar);
        return y + 52;
    }

    private void OnSliderChanged(TrackBar bar)
    {
        if (bar == _day) _engine.SetDayTemp(bar.Value);
        else if (bar == _night) _engine.SetNightTemp(bar.Value);
        else if (bar == _dim) _engine.SetDimPercent(bar.Value);
        else if (bar == _flickerBrightness) _engine.SetFlickerBrightness(bar.Value / 100.0);
    }

    private int AddFlickerSection(int y)
    {
        _flicker.Text = "Flicker-free dimming (PWM safe)";
        _flicker.SetBounds(16, y, 290, 22);
        _flicker.ForeColor = Ink;
        _flicker.Enabled = _engine.FlickerFreeAvailable;
        _flicker.CheckedChanged += (_, _) => { if (!_updatingUi) _engine.SetFlickerFree(_flicker.Checked); };
        Controls.Add(_flicker);

        var hint = new Label
        {
            Text = _engine.FlickerFreeAvailable
                ? "Pins the backlight at 100% and dims in software. Use the slider below; brightness keys are overridden."
                : "No controllable backlight found on this machine.",
            ForeColor = Muted,
        };
        hint.Font = new Font("Segoe UI", 8f);
        hint.SetBounds(32, y + 22, 276, 30);
        Controls.Add(hint);

        var bLabel = new Label { Text = "Brightness", ForeColor = Muted };
        bLabel.SetBounds(32, y + 56, 80, 18);
        Controls.Add(bLabel);

        _flickerVal.SetBounds(230, y + 56, 76, 18);
        _flickerVal.TextAlign = ContentAlignment.TopRight;
        _flickerVal.ForeColor = Ink;
        Controls.Add(_flickerVal);

        _flickerBrightness.SetBounds(26, y + 74, 284, 30);
        _flickerBrightness.Minimum = 20;
        _flickerBrightness.Maximum = 100;
        _flickerBrightness.TickStyle = TickStyle.None;
        _flickerBrightness.BackColor = Bg;
        _flickerBrightness.ValueChanged += (_, _) => { if (!_updatingUi) OnSliderChanged(_flickerBrightness); };
        Controls.Add(_flickerBrightness);

        _onlyOnPower.Text = "Only on power adapter (saves battery)";
        _onlyOnPower.SetBounds(32, y + 106, 276, 22);
        _onlyOnPower.ForeColor = Ink;
        _onlyOnPower.CheckedChanged += (_, _) => { if (!_updatingUi) _engine.SetFlickerOnlyOnPower(_onlyOnPower.Checked); };
        Controls.Add(_onlyOnPower);
        return y + 136;
    }

    private int AddToggles(int y)
    {
        _launchAtLogin.Text = "Launch at login";
        _launchAtLogin.SetBounds(16, y, 290, 22);
        _launchAtLogin.ForeColor = Ink;
        _launchAtLogin.CheckedChanged += (_, _) => { if (!_updatingUi) SetLaunchAtLogin(_launchAtLogin.Checked); };
        Controls.Add(_launchAtLogin);
        return y + 30;
    }

    private int AddPauseRow(int y)
    {
        StyleButton(_pauseHour, "Pause 1 h");
        _pauseHour.SetBounds(16, y, 90, 28);
        _pauseHour.Click += (_, _) => _engine.Pause(TimeSpan.FromHours(1));
        Controls.Add(_pauseHour);

        StyleButton(_pauseSunrise, "Until sunrise");
        _pauseSunrise.SetBounds(112, y, 100, 28);
        _pauseSunrise.Click += (_, _) => _engine.PauseUntilSunrise();
        Controls.Add(_pauseSunrise);

        StyleButton(_resume, "Resume");
        _resume.SetBounds(218, y, 88, 28);
        _resume.Click += (_, _) => _engine.Resume();
        Controls.Add(_resume);
        return y + 38;
    }

    private void AddFooter(int y)
    {
        _reset.Text = "Reset to ideal";
        _reset.LinkColor = Muted;
        _reset.ActiveLinkColor = Accent;
        _reset.LinkBehavior = LinkBehavior.HoverUnderline;
        _reset.SetBounds(16, y + 4, 120, 18);
        _reset.LinkClicked += (_, _) => _engine.ResetToRecommended();
        Controls.Add(_reset);

        var quit = new LinkLabel
        {
            Text = "Quit",
            LinkColor = Muted,
            ActiveLinkColor = Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        quit.SetBounds(266, y + 4, 40, 18);
        quit.LinkClicked += (_, _) => Application.Exit();
        Controls.Add(quit);
    }

    private static void StyleButton(Button b, string text)
    {
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 56, 44);
        b.BackColor = Card;
        b.ForeColor = Ink;
    }

    // -------------------------------------------------------------- state

    public void SyncFromEngine()
    {
        _updatingUi = true;
        try
        {
            var s = _engine.Settings;
            string phase = !s.Enabled ? "Off"
                : _engine.PausedUntilUtc != null ? "Paused"
                : _engine.Phase switch
                {
                    DayPhase.Day => "Daylight",
                    DayPhase.Twilight => "Golden hour",
                    _ => "Night",
                };
            string detail = s.Enabled && _engine.PausedUntilUtc == null
                ? $" · {Math.Round(_engine.AppliedKelvin / 50) * 50:0} K"
                : "";
            string suspended = _engine.FlickerSuspended ? " · flicker-free paused (battery)" : "";
            _status.Text = phase + detail + suspended;
            _place.Text = $"{_engine.Location.PlaceName} · {_engine.Location.SourceName}";

            _enabled.Checked = s.Enabled;
            _day.Value = (int)Math.Clamp(s.DayTemp, _day.Minimum, _day.Maximum);
            _dayVal.Text = $"{s.DayTemp:0} K";
            _night.Value = (int)Math.Clamp(s.NightTemp, _night.Minimum, _night.Maximum);
            _nightVal.Text = $"{s.NightTemp:0} K";
            _dim.Value = (int)Math.Clamp(s.DimPercent, _dim.Minimum, _dim.Maximum);
            _dimVal.Text = s.DimPercent < 1 ? "Off" : $"{s.DimPercent:0} %";

            _flicker.Checked = s.FlickerFree;
            _flickerBrightness.Value = (int)Math.Clamp(s.FlickerComp * 100, 20, 100);
            _flickerBrightness.Enabled = s.FlickerFree && _engine.FlickerFreeAvailable;
            _flickerVal.Text = $"{s.FlickerComp * 100:0} %";
            _onlyOnPower.Checked = s.FlickerOnlyOnPower;
            _onlyOnPower.Enabled = s.FlickerFree;

            _launchAtLogin.Checked = GetLaunchAtLogin();
            _resume.Enabled = _engine.PausedUntilUtc != null;
            _reset.Visible = _engine.IsCustomized;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static bool GetLaunchAtLogin()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("Circa") != null;
    }

    private static void SetLaunchAtLogin(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue("Circa", $"\"{Application.ExecutablePath}\"");
        else key.DeleteValue("Circa", throwOnMissingValue: false);
    }
}
