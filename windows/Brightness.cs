using System.Management;

namespace Circa;

/// <summary>
/// Internal-panel backlight via WMI (laptop displays). External monitors
/// (DDC/CI) are out of scope for v1, mirroring the macOS version which only
/// drives the built-in display. All calls degrade to "unavailable" on
/// desktops without a controllable backlight.
/// </summary>
public static class Brightness
{
    public static bool Available
    {
        get
        {
            try { return Get() != null; }
            catch { return false; }
        }
    }

    /// <summary>Current backlight level 0..1, or null if uncontrollable.</summary>
    public static double? Get()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject o in searcher.Get())
                return Convert.ToDouble(o["CurrentBrightness"]) / 100.0;
        }
        catch { }
        return null;
    }

    public static bool Set(double value)
    {
        try
        {
            byte level = (byte)Math.Clamp(Math.Round(value * 100), 0, 100);
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in searcher.Get())
            {
                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, level });
                return true;
            }
        }
        catch { }
        return false;
    }

    public static bool OnBattery =>
        System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus
            == System.Windows.Forms.PowerLineStatus.Offline;
}
