namespace Circa;

public enum DayPhase { Day, Twilight, Night }

/// <summary>
/// The whole engine, ported from the macOS version: solar clock → target
/// temperature → gamma ramps, on a 5 s tick with sunset-speed slewing,
/// night-scaled dimming, and battery-aware flicker-free mode.
/// </summary>
public sealed class Engine
{
    public readonly Settings Settings;
    public readonly LocationProvider Location;
    private readonly GammaController _gamma = new();

    public DayPhase Phase { get; private set; } = DayPhase.Day;
    public double AppliedKelvin { get; private set; } = 6500;
    /// <summary>1 = full day, 0 = full night.</summary>
    public double NightBlend { get; private set; } = 1;
    public DateTime? PausedUntilUtc { get; private set; }
    /// <summary>When the day phase next flips (null in polar day/night), and to what.</summary>
    public DateTime? NextTransitionUtc { get; private set; }
    public DayPhase NextPhase { get; private set; } = DayPhase.Night;
    public bool FlickerSuspended { get; private set; }
    public bool FlickerFreeAvailable { get; }

    public event Action? Changed;

    private bool _outputSuspended;
    private double _lastEffectiveDim = 1;
    private DateTime _lastLocationRefresh = DateTime.UtcNow;

    public Engine()
    {
        Settings = Settings.Load();
        Location = new LocationProvider(Settings);
        FlickerFreeAvailable = Brightness.Available;
        Location.Updated += () => Tick(slew: false);
    }

    public void Start()
    {
        _ = Location.RefreshAsync();
        if (Settings.FlickerFree) EngageFlickerFree();
        Tick(slew: false);
    }

    // ------------------------------------------------------------- controls

    public void SetEnabled(bool enabled)
    {
        Settings.Enabled = enabled; Settings.Save();
        if (enabled) Tick(slew: false); else SuspendOutput();
        Changed?.Invoke();
    }

    public void SetDayTemp(double v) { Settings.DayTemp = v; Settings.Save(); Tick(slew: false); }
    public void SetNightTemp(double v) { Settings.NightTemp = v; Settings.Save(); Tick(slew: false); }
    public void SetDimPercent(double v) { Settings.DimPercent = v; Settings.Save(); Tick(slew: false); }

    public void SetFlickerFree(bool on)
    {
        Settings.FlickerFree = on; Settings.Save();
        if (on) EngageFlickerFree();
        else
        {
            if (!FlickerSuspended) DisengageFlickerFree();
            else { Settings.FlickerComp = 1.0; Settings.Save(); }
            FlickerSuspended = false;
        }
        Tick(slew: false);
        Changed?.Invoke();
    }

    public void SetFlickerBrightness(double value01)
    {
        Settings.FlickerComp = Math.Clamp(value01, 0.2, 1.0); Settings.Save();
        Tick(slew: false);
    }

    public void SetFlickerOnlyOnPower(bool on)
    {
        Settings.FlickerOnlyOnPower = on; Settings.Save();
        Tick(slew: false);
        Changed?.Invoke();
    }

    public void Pause(TimeSpan duration)
    {
        PausedUntilUtc = DateTime.UtcNow + duration;
        SuspendOutput();
        Changed?.Invoke();
    }

    public void PauseUntilSunrise()
    {
        PausedUntilUtc = Solar.NextSunriseUtc(DateTime.UtcNow, Location.Latitude, Location.Longitude);
        SuspendOutput();
        Changed?.Invoke();
    }

    public void Resume()
    {
        PausedUntilUtc = null;
        Tick(slew: false);
        Changed?.Invoke();
    }

    public const double RecommendedDay = 6500, RecommendedNight = 2300, RecommendedDim = 25;

    public bool IsCustomized =>
        Settings.DayTemp != RecommendedDay || Settings.NightTemp != RecommendedNight
        || Settings.DimPercent != RecommendedDim;

    public void ResetToRecommended()
    {
        Settings.DayTemp = RecommendedDay;
        Settings.NightTemp = RecommendedNight;
        Settings.DimPercent = RecommendedDim;
        Settings.Save();
        PausedUntilUtc = null;
        if (!Settings.Enabled) { Settings.Enabled = true; Settings.Save(); }
        Tick(slew: false);
        Changed?.Invoke();
    }

    // --------------------------------------------------------------- engine

    /// <summary>Called every 5 s by the tray timer (slew) and on user input (no slew).</summary>
    public void Tick(bool slew)
    {
        if ((DateTime.UtcNow - _lastLocationRefresh).TotalHours > 3)
        {
            _lastLocationRefresh = DateTime.UtcNow;
            _ = Location.RefreshAsync();
        }

        if (PausedUntilUtc is DateTime until && DateTime.UtcNow >= until) PausedUntilUtc = null;

        double elevation = Solar.Elevation(DateTime.UtcNow, Location.Latitude, Location.Longitude);
        Phase = elevation > 6 ? DayPhase.Day : elevation < -6 ? DayPhase.Night : DayPhase.Twilight;
        UpdateForecast();

        if (!Settings.Enabled || PausedUntilUtc != null)
        {
            SuspendOutput();
            Changed?.Invoke();
            return;
        }
        _outputSuspended = false;

        NightBlend = SolarBlend(elevation);
        double target = Settings.NightTemp + (Settings.DayTemp - Settings.NightTemp) * NightBlend;

        if (slew)
        {
            double delta = Math.Clamp(target - AppliedKelvin, -150, 150);
            AppliedKelvin += delta;
        }
        else
        {
            AppliedKelvin = target;
        }

        if (Settings.FlickerFree)
        {
            if (Settings.FlickerOnlyOnPower && Brightness.OnBattery)
            {
                if (!FlickerSuspended)
                {
                    FlickerSuspended = true;
                    Brightness.Set(Settings.FlickerComp); // hand back at perceived level
                }
            }
            else
            {
                if (FlickerSuspended)
                {
                    FlickerSuspended = false;
                    EngageFlickerFree();
                }
                FlickerWatchdog();
            }
        }

        _lastEffectiveDim = EffectiveDim();
        _gamma.Apply(AppliedKelvin, _lastEffectiveDim);
        Changed?.Invoke();
    }

    /// <summary>
    /// The scan restarts from "now" every tick, so the crossing minute can
    /// jitter by a step; only adopt moves larger than that so Changed
    /// subscribers stay quiet at steady state.
    /// </summary>
    private void UpdateForecast()
    {
        DateTime? next = Solar.NextPhaseChangeUtc(DateTime.UtcNow, Location.Latitude, Location.Longitude);
        bool changed = (next, NextTransitionUtc) switch
        {
            (null, null) => false,
            (DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) > 90,
            _ => true,
        };
        if (!changed) return;
        NextTransitionUtc = next;
        if (next is DateTime crossing)
        {
            double e = Solar.Elevation(crossing.AddMinutes(2), Location.Latitude, Location.Longitude);
            NextPhase = e > 6 ? DayPhase.Day : e < -6 ? DayPhase.Night : DayPhase.Twilight;
        }
    }

    /// <summary>
    /// The day's shape: civil twilight (+6..-6) carries the main transition,
    /// and above it a gentle daylight slope keeps the screen drifting with
    /// the sun from sunrise to peak to sunset. 1 = full day, 0 = full night.
    /// </summary>
    private static double SolarBlend(double elevation)
    {
        static double Smooth(double v)
        {
            double x = Math.Clamp(v, 0, 1);
            return x * x * (3 - 2 * x);
        }
        if (elevation <= 6) return 0.85 * Smooth((elevation + 6) / 12);
        return 0.85 + 0.15 * Smooth((elevation - 6) / 54);
    }

    private double EffectiveDim()
    {
        double comp = Settings.FlickerFree && !FlickerSuspended ? Settings.FlickerComp : 1.0;
        double nightDim = Settings.DimPercent * (1 - NightBlend);
        return Math.Max(0.12, (1 - nightDim / 100) * comp);
    }

    private void SuspendOutput()
    {
        if (_outputSuspended) return;
        _outputSuspended = true;
        _gamma.Restore();
    }

    public void Shutdown()
    {
        if (Settings.FlickerFree && !FlickerSuspended)
            Brightness.Set(_outputSuspended ? Settings.FlickerComp : _lastEffectiveDim);
        _gamma.Restore();
        Settings.Save();
    }

    // -------------------------------------------------- flicker-free (PWM)

    private void EngageFlickerFree()
    {
        double? hw = Brightness.Get();
        if (hw is null) return;
        if (hw < 0.995)
        {
            Settings.FlickerComp = Math.Clamp(hw.Value, 0.2, 1.0);
            Settings.Save();
            Brightness.Set(1.0);
        }
    }

    private void DisengageFlickerFree()
    {
        Brightness.Set(Settings.FlickerComp);
        Settings.FlickerComp = 1.0;
        Settings.Save();
    }

    /// <summary>Re-pin only; never absorb (the macOS ratchet lesson).</summary>
    private void FlickerWatchdog()
    {
        double? hw = Brightness.Get();
        if (hw is not null && hw < 0.985) Brightness.Set(1.0);
    }
}
