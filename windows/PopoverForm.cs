using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Circa;

/// <summary>
/// The tray popover, autopilot-first: a status card (phase, current output,
/// sun track, what happens next), the flicker-free control, pause row — and
/// the tuning levers tucked into a collapsible Advanced section. WinForms has
/// no disclosure control, so Advanced (and the update banner) are authored
/// below the visible fold and revealed by growing ClientSize.
/// </summary>
public sealed class PopoverForm : Form
{
    private static readonly Color Bg = Color.FromArgb(23, 18, 16);
    private static readonly Color Card = Color.FromArgb(34, 27, 22);
    private static readonly Color Ink = Color.FromArgb(240, 224, 195);
    private static readonly Color Muted = Color.FromArgb(176, 152, 122);
    private static readonly Color Accent = Color.FromArgb(235, 172, 92);

    private readonly Engine _engine;
    private readonly ToolTip _tips = new();

    private readonly Label _status = new();
    private readonly Label _now = new();
    private readonly Panel _track = new();
    private readonly Label _forecast = new();
    private readonly CheckBox _enabled = new();
    private readonly CheckBox _flicker = new();
    private readonly TrackBar _flickerBrightness = new();
    private readonly Label _flickerVal = new();
    private readonly Button _pauseHour = new();
    private readonly Button _pauseSunrise = new();
    private readonly Button _resume = new();
    private readonly LinkLabel _advToggle = new();
    private readonly Label _place = new();

    // Advanced region (below the fold until expanded).
    private readonly TrackBar _day = new();
    private readonly Label _dayVal = new();
    private readonly TrackBar _night = new();
    private readonly Label _nightVal = new();
    private readonly TrackBar _dim = new();
    private readonly Label _dimVal = new();
    private readonly LinkLabel _reset = new();
    private readonly CheckBox _onlyOnPower = new();
    private readonly CheckBox _launchAtLogin = new();
    private readonly CheckBox _autoUpdate = new();

    private readonly Button _update = new();
    private bool _updateRunning;
    private bool _advancedOpen;
    private bool _updatingUi;

    public PopoverForm(Engine engine)
    {
        _engine = engine;

        SuspendLayout();

        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Text = "Circa";
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Bg;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 9.5f);
        Deactivate += (_, _) => Hide();
        // Closing via the title-bar X disposes the form; the next tray click
        // would then Show() a disposed object. Hide instead — the tray owns
        // this form's lifetime. Application.Exit (other close reasons) still works.
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };

        int y = AddStatusCard(14);
        y = AddFlickerSection(y);
        y = AddPauseRow(y);
        y = AddAdvancedToggle(y);
        AddFooter(y);
        y = AddAdvancedRegion(y + 34);
        AddUpdateBanner(y);

        // Every bound above is authored in 96-DPI pixels; on a scaled display
        // (125–200%, i.e. most laptops) the whole layout must scale with it.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(320, 300); // height is owned by ApplyLayout

        ResumeLayout(false);
        PerformLayout();

        // Force handle creation so DPI autoscaling runs now; ApplyLayout (via
        // SyncFromEngine below) then computes heights in device units.
        _ = Handle;

        _engine.Changed += () => { if (IsHandleCreated) BeginInvoke(SyncFromEngine); };
        SyncFromEngine();
    }

    // ------------------------------------------------------------ layout

    private int AddStatusCard(int y)
    {
        _status.SetBounds(16, y, 236, 24);
        _status.ForeColor = Ink;
        _status.Font = new Font("Segoe UI Semibold", 10.5f);
        Controls.Add(_status);

        _enabled.SetBounds(262, y, 44, 22);
        _enabled.Text = "on";
        _enabled.ForeColor = Muted;
        _enabled.CheckedChanged += (_, _) => { if (!_updatingUi) _engine.SetEnabled(_enabled.Checked); };
        Controls.Add(_enabled);

        _now.SetBounds(16, y + 26, 288, 16);
        _now.ForeColor = Muted;
        _now.Font = new Font("Segoe UI", 8.5f);
        Controls.Add(_now);

        _track.SetBounds(16, y + 46, 288, 16);
        _track.BackColor = Bg;
        _track.Paint += PaintTrack;
        Controls.Add(_track);

        _forecast.SetBounds(16, y + 66, 288, 16);
        _forecast.ForeColor = Muted;
        _forecast.Font = new Font("Segoe UI", 8.5f);
        Controls.Add(_forecast);
        return y + 94;
    }

    /// <summary>
    /// Slim gradient bar with a sliding dot. The dot maps the engine's own
    /// day/night blend (1 = full day → sun end), so it always agrees with
    /// the screen's warmth.
    /// </summary>
    private void PaintTrack(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle r = _track.ClientRectangle;

        using var glyphFont = new Font("Segoe UI Symbol", 8f);
        Size sun = TextRenderer.MeasureText("☀", glyphFont);
        Size moon = TextRenderer.MeasureText("☾", glyphFont);
        TextRenderer.DrawText(g, "☀", glyphFont,
            new Point(r.Left, r.Top + (r.Height - sun.Height) / 2), Color.FromArgb(222, 186, 110));
        TextRenderer.DrawText(g, "☾", glyphFont,
            new Point(r.Right - moon.Width, r.Top + (r.Height - moon.Height) / 2), Color.FromArgb(148, 140, 205));

        int barH = Math.Max(3, r.Height / 4);
        var bar = Rectangle.FromLTRB(r.Left + sun.Width + 2, r.Top + (r.Height - barH) / 2,
                                     r.Right - moon.Width - 2, r.Top + (r.Height - barH) / 2 + barH);
        if (bar.Width <= 12) return;

        using var gradient = new LinearGradientBrush(bar, Color.Empty, Color.Empty, LinearGradientMode.Horizontal)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(96, 156, 190),
                    Color.FromArgb(212, 140, 70),
                    Color.FromArgb(92, 82, 152),
                },
                Positions = new[] { 0f, 0.55f, 1f },
            },
        };
        g.FillRectangle(gradient, bar);

        int dot = Math.Max(8, r.Height / 2);
        float x = bar.Left + (float)(1 - _engine.NightBlend) * (bar.Width - dot);
        using var dotBrush = new SolidBrush(Ink);
        g.FillEllipse(dotBrush, x, r.Top + (r.Height - dot) / 2f, dot, dot);
    }

    private int AddFlickerSection(int y)
    {
        _flicker.Text = "Flicker-free dimming (PWM safe)";
        _flicker.SetBounds(16, y, 290, 22);
        _flicker.ForeColor = Ink;
        _flicker.Enabled = _engine.FlickerFreeAvailable;
        _flicker.CheckedChanged += (_, _) => { if (!_updatingUi) _engine.SetFlickerFree(_flicker.Checked); };
        _tips.SetToolTip(_flicker, _engine.FlickerFreeAvailable
            ? "Pins the backlight at 100% and dims in software, so the LED panel never strobes (PWM). " +
              "Use the brightness slider below; the keys are overridden."
            : "No controllable backlight found on this machine.");
        Controls.Add(_flicker);

        var bLabel = new Label { Text = "Brightness", ForeColor = Muted };
        bLabel.SetBounds(32, y + 26, 90, 18);
        Controls.Add(bLabel);

        _flickerVal.SetBounds(230, y + 26, 76, 18);
        _flickerVal.TextAlign = ContentAlignment.TopRight;
        _flickerVal.ForeColor = Ink;
        Controls.Add(_flickerVal);

        _flickerBrightness.SetBounds(26, y + 44, 284, 30);
        _flickerBrightness.Minimum = 20;
        _flickerBrightness.Maximum = 100;
        _flickerBrightness.TickStyle = TickStyle.None;
        _flickerBrightness.BackColor = Bg;
        _flickerBrightness.ValueChanged += (_, _) =>
        {
            if (!_updatingUi) _engine.SetFlickerBrightness(_flickerBrightness.Value / 100.0);
        };
        Controls.Add(_flickerBrightness);
        return y + 82;
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

    private int AddAdvancedToggle(int y)
    {
        _advToggle.Text = "Advanced  ▸";
        _advToggle.LinkColor = Muted;
        _advToggle.ActiveLinkColor = Accent;
        _advToggle.LinkBehavior = LinkBehavior.HoverUnderline;
        _advToggle.SetBounds(16, y, 140, 18);
        _advToggle.LinkClicked += (_, _) =>
        {
            _advancedOpen = !_advancedOpen;
            _advToggle.Text = _advancedOpen ? "Advanced  ▾" : "Advanced  ▸";
            ApplyLayout();
        };
        Controls.Add(_advToggle);
        return y + 26;
    }

    private void AddFooter(int y)
    {
        _place.SetBounds(16, y + 4, 230, 18);
        _place.ForeColor = Muted;
        _place.Font = new Font("Segoe UI", 8f);
        Controls.Add(_place);

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

    private int AddAdvancedRegion(int y)
    {
        var caption = new Label
        {
            Text = "Circa follows the sun on its own; these set the endpoints it moves between.",
            ForeColor = Muted,
        };
        caption.Font = new Font("Segoe UI", 8f);
        caption.SetBounds(16, y, 290, 28);
        Controls.Add(caption);

        y = AddSlider("Day", _day, _dayVal, 4800, 6500, y + 32);
        y = AddSlider("Night", _night, _nightVal, 1900, 4500, y);
        y = AddSlider("Night dim", _dim, _dimVal, 0, 70, y);

        _reset.Text = "Reset to ideal";
        _reset.LinkColor = Muted;
        _reset.ActiveLinkColor = Accent;
        _reset.LinkBehavior = LinkBehavior.HoverUnderline;
        _reset.SetBounds(16, y + 2, 150, 18);
        _reset.LinkClicked += (_, _) => _engine.ResetToRecommended();
        Controls.Add(_reset);
        y += 28;

        _onlyOnPower.Text = "Flicker-free only on power adapter";
        _onlyOnPower.SetBounds(16, y, 290, 22);
        _onlyOnPower.ForeColor = Ink;
        _onlyOnPower.CheckedChanged += (_, _) => { if (!_updatingUi) _engine.SetFlickerOnlyOnPower(_onlyOnPower.Checked); };
        _tips.SetToolTip(_onlyOnPower,
            "The pinned backlight draws noticeably more energy, so on battery Circa hands brightness back to Windows.");
        Controls.Add(_onlyOnPower);
        y += 30;

        _launchAtLogin.Text = "Launch at login";
        _launchAtLogin.SetBounds(16, y, 290, 22);
        _launchAtLogin.ForeColor = Ink;
        _launchAtLogin.CheckedChanged += (_, _) => { if (!_updatingUi) SetLaunchAtLogin(_launchAtLogin.Checked); };
        Controls.Add(_launchAtLogin);
        y += 30;

        _autoUpdate.Text = "Update automatically";
        _autoUpdate.SetBounds(16, y, 290, 22);
        _autoUpdate.ForeColor = Ink;
        _autoUpdate.CheckedChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _engine.Settings.AutoUpdate = _autoUpdate.Checked;
            _engine.Settings.Save();
        };
        Controls.Add(_autoUpdate);
        return y + 32;
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
    }

    private void AddUpdateBanner(int y)
    {
        _update.SetBounds(0, y, 320, 40);
        _update.FlatStyle = FlatStyle.Flat;
        _update.FlatAppearance.BorderSize = 0;
        _update.BackColor = Color.FromArgb(54, 40, 26);
        _update.ForeColor = Accent;
        _update.Font = new Font("Segoe UI Semibold", 9.5f);
        _update.Visible = false;
        _update.Click += async (_, _) => await RunUpdateAsync();
        Controls.Add(_update);
    }

    private async Task RunUpdateAsync()
    {
        if (_updateRunning) return;
        _updateRunning = true;
        _update.Enabled = false;
        _update.Text = "Downloading update…";
        bool ok = await Updater.DownloadAndApplyAsync();
        if (!ok)
        {
            // On success the app is already restarting; only failure returns here.
            _update.Text = "Update failed — click to retry";
            _update.Enabled = true;
            _updateRunning = false;
        }
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

    /// <summary>
    /// Size the form for the current fold (Advanced open/closed) and pin the
    /// update banner to the bottom edge. Bounds are consistent within a call
    /// — authored units before DPI scaling, device units after — so the math
    /// holds at any scale.
    /// </summary>
    private void ApplyLayout()
    {
        int pad = LogicalToDeviceUnits(12);
        int fold = (_advancedOpen ? _autoUpdate.Bottom : _place.Bottom) + pad;
        bool banner = Updater.AvailableTag != null;
        if (banner) _update.SetBounds(0, fold, ClientSize.Width, _update.Height);
        _update.Visible = banner;
        int height = fold + (banner ? _update.Height : 0);
        if (ClientSize.Height != height) ClientSize = new Size(ClientSize.Width, height);
        if (Visible) PositionAboveTray();
    }

    public void PositionAboveTray()
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 800);
        Location = new Point(
            Math.Max(0, screen.Right - Width - 12),
            Math.Max(0, screen.Bottom - Height - 12));
    }

    public void SyncFromEngine()
    {
        _updatingUi = true;
        try
        {
            var s = _engine.Settings;
            bool active = s.Enabled && _engine.PausedUntilUtc == null;

            _status.Text = !s.Enabled ? "Off"
                : _engine.PausedUntilUtc != null ? "Paused"
                : _engine.Phase switch
                {
                    DayPhase.Day => "Daylight",
                    DayPhase.Twilight => "Golden hour",
                    _ => "Night",
                };
            _now.Text = NowText(s, active);
            _forecast.Text = ForecastText(s);
            _track.Visible = active;
            _track.Invalidate();

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
            _autoUpdate.Checked = s.AutoUpdate;
            _resume.Enabled = _engine.PausedUntilUtc != null;
            _reset.Visible = _engine.IsCustomized;

            if (Updater.AvailableTag is string tag && !_updateRunning)
                _update.Text = $"Update to Circa {tag.TrimStart('v')} — restart";
            ApplyLayout();
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private string NowText(Settings s, bool active)
    {
        if (!active) return "Screen at system default";
        var parts = new List<string> { $"{Math.Round(_engine.AppliedKelvin / 50) * 50:0} K" };
        double nightDim = s.DimPercent * (1 - _engine.NightBlend);
        if (nightDim >= 1) parts.Add($"dimmed {nightDim:0} %");
        if (s.FlickerFree) parts.Add(_engine.FlickerSuspended ? "flicker-free paused (battery)" : "flicker-free");
        return string.Join(" · ", parts);
    }

    /// <summary>What the autopilot does next; times via the system clock format.</summary>
    private string ForecastText(Settings s)
    {
        if (!s.Enabled) return "";
        if (_engine.PausedUntilUtc is DateTime until)
            return $"Resumes {until.ToLocalTime():t} — screen at system default";
        if (_engine.NextTransitionUtc is not DateTime next) return "";
        string when = next.ToLocalTime().ToString("t");
        string dim = s.DimPercent >= 1 ? $" · dim {s.DimPercent:0} %" : "";
        return (_engine.Phase, _engine.NextPhase) switch
        {
            (DayPhase.Day, _) => $"Tonight: {s.NightTemp:0} K{dim} · from ~{when}",
            (DayPhase.Twilight, DayPhase.Night) => $"Settling to {s.NightTemp:0} K{dim} by ~{when}",
            _ => $"Morning: {s.DayTemp:0} K from ~{when}",
        };
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
