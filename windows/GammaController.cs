using System.Runtime.InteropServices;

namespace Circa;

/// <summary>
/// Applies color temperature + software dimming through each display's
/// gamma ramp (SetDeviceGammaRamp), the Windows equivalent of the macOS
/// gamma-table mechanism. Original ramps are cached on first touch so any
/// ICC calibration is preserved; we only scale them.
///
/// Windows clamps how far a ramp may deviate from identity unless the
/// GdiIcmGammaRange registry unlock is present (the f.lux trick). When a
/// ramp is rejected we blend it back toward the original until the driver
/// accepts it, so deep-amber settings degrade gracefully instead of failing.
/// </summary>
public sealed class GammaController
{
    private const int RampSize = 256 * 3;

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool GetDeviceGammaRamp(IntPtr hdc, ushort[] lpRamp);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] lpRamp);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string? driver, string device, string? output, IntPtr initData);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

    private readonly Dictionary<string, ushort[]> _originals = new();

    public static List<string> OnlineDisplays()
    {
        var result = new List<string>();
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                result.Add(device.DeviceName);
            device.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        }
        return result;
    }

    private ushort[]? OriginalFor(string deviceName, IntPtr hdc)
    {
        if (_originals.TryGetValue(deviceName, out var cached)) return cached;
        var ramp = new ushort[RampSize];
        if (!GetDeviceGammaRamp(hdc, ramp)) return null;
        _originals[deviceName] = ramp;
        return ramp;
    }

    /// <summary>dim is a linear brightness multiplier 0..1 (floored at 0.10).</summary>
    public void Apply(double kelvin, double dim)
    {
        var (mr, mg, mb) = Multipliers(kelvin);
        double scale = Math.Clamp(dim, 0.10, 1.0);

        foreach (string name in OnlineDisplays())
        {
            IntPtr hdc = CreateDC(null, name, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero) continue;
            try
            {
                var original = OriginalFor(name, hdc);
                if (original == null) continue;

                // Blend toward the original ramp if the driver rejects the
                // full-strength ramp (default Windows gamma clamp).
                for (double strength = 1.0; strength >= 0.24; strength *= 0.75)
                {
                    var ramp = BuildRamp(original, mr, mg, mb, scale, strength);
                    if (SetDeviceGammaRamp(hdc, ramp)) break;
                }
            }
            finally
            {
                DeleteDC(hdc);
            }
        }
    }

    private static ushort[] BuildRamp(ushort[] original, double mr, double mg, double mb,
                                      double dim, double strength)
    {
        var ramp = new ushort[RampSize];
        double fr = Lerp(1.0, mr * dim, strength);
        double fg = Lerp(1.0, mg * dim, strength);
        double fb = Lerp(1.0, mb * dim, strength);
        for (int i = 0; i < 256; i++)
        {
            ramp[i] = (ushort)Math.Min(65535.0, original[i] * fr);
            ramp[256 + i] = (ushort)Math.Min(65535.0, original[256 + i] * fg);
            ramp[512 + i] = (ushort)Math.Min(65535.0, original[512 + i] * fb);
        }
        return ramp;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Hand the original ramps back (called on quit/pause).</summary>
    public void Restore()
    {
        foreach (string name in OnlineDisplays())
        {
            if (!_originals.TryGetValue(name, out var original)) continue;
            IntPtr hdc = CreateDC(null, name, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero) continue;
            try { SetDeviceGammaRamp(hdc, original); }
            finally { DeleteDC(hdc); }
        }
    }

    /// <summary>RGB multipliers normalized so 6500 K is exactly (1, 1, 1).</summary>
    public static (double r, double g, double b) Multipliers(double kelvin)
    {
        var c = Raw(kelvin);
        var baseline = Raw(6500);
        return (Math.Min(1, c.r / baseline.r),
                Math.Min(1, c.g / baseline.g),
                Math.Min(1, c.b / baseline.b));
    }

    /// <summary>Tanner Helland's blackbody approximation, 0..1 per channel.</summary>
    private static (double r, double g, double b) Raw(double kelvin)
    {
        double t = Math.Clamp(kelvin, 1000, 12000) / 100.0;
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66
            ? 99.4708025861 * Math.Log(t) - 161.1195681661
            : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b;
        if (t >= 66) b = 255;
        else if (t <= 19) b = 0;
        else b = 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        return (Unit(r), Unit(g), Unit(b));
        static double Unit(double v) => Math.Clamp(v, 0, 255) / 255.0;
    }
}
