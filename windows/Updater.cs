using System.Diagnostics;
using System.IO.Compression;

namespace Circa;

/// <summary>
/// Self-update from GitHub releases. The latest version comes from the
/// /releases/latest redirect (no API calls, no rate limits). When a newer
/// release exists the popover shows a banner (click to update), or — with
/// "Update automatically" on — the exe swaps itself and relaunches quietly
/// (Windows allows renaming a running exe). Dev builds (version 0.x) never
/// self-update; the Release workflow stamps the real version from the git tag.
/// </summary>
internal static class Updater
{
    private const string Repo = "https://github.com/limone-eth/circa";
    private const string AssetName = "Circa-windows-x64.zip";

    /// <summary>Latest release tag (e.g. "v1.0.3") once a newer one is known.</summary>
    public static string? AvailableTag { get; private set; }

    private static readonly HttpClient NoRedirect = new(new HttpClientHandler { AllowAutoRedirect = false });
    private static readonly HttpClient Http = new();

    static Updater()
    {
        NoRedirect.DefaultRequestHeaders.UserAgent.ParseAdd("Circa-updater");
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Circa-updater");
    }

    private static Version? CurrentVersion()
    {
        string raw = Application.ProductVersion.Split('+', '-')[0];
        return Version.TryParse(raw, out var v) && v.Major > 0 ? v : null;
    }

    /// <summary>Sets AvailableTag when a newer release exists; returns it.</summary>
    public static async Task<string?> CheckAsync()
    {
        try
        {
            var current = CurrentVersion();
            if (current == null || Environment.ProcessPath == null) return AvailableTag;

            // GitHub redirects /releases/latest to /releases/tag/<tag>.
            using var response = await NoRedirect.GetAsync(
                $"{Repo}/releases/latest", HttpCompletionOption.ResponseHeadersRead);
            string tag = response.Headers.Location?.ToString().Split('/')[^1] ?? "";
            if (tag.Length < 2 || tag[0] != 'v' || !Version.TryParse(tag[1..], out var latest))
                return AvailableTag;

            if (latest > current) AvailableTag = tag;
            return AvailableTag;
        }
        catch { return AvailableTag; }
    }

    /// <summary>
    /// Download the new exe, swap it in place and relaunch. Returns false on
    /// failure; on success the app is already exiting.
    /// </summary>
    public static async Task<bool> DownloadAndApplyAsync()
    {
        try
        {
            string? tag = AvailableTag;
            string? exe = Environment.ProcessPath;
            var current = CurrentVersion();
            if (tag == null || exe == null || current == null) return false;

            string dir = Path.Combine(Path.GetTempPath(), "circa-update-" + tag);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);

            string zip = Path.Combine(dir, AssetName);
            await using (var src = await Http.GetStreamAsync($"{Repo}/releases/download/{tag}/{AssetName}"))
            await using (var dst = File.Create(zip))
                await src.CopyToAsync(dst);
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, dir));

            string newExe = Path.Combine(dir, "Circa.exe");
            if (!File.Exists(newExe)) return false;

            // A stale asset must not cause an update loop: only swap when the
            // downloaded exe really is newer than the running one.
            string? product = FileVersionInfo.GetVersionInfo(newExe).ProductVersion?.Split('+', '-')[0];
            if (!Version.TryParse(product, out var downloaded) || downloaded <= current)
            {
                AvailableTag = null;
                return false;
            }

            string old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(newExe, exe); }
            catch { File.Move(old, exe); throw; }

            Process.Start(new ProcessStartInfo(exe, $"--wait-pid {Environment.ProcessId}")
            {
                UseShellExecute = true,
            });
            Application.Exit();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Remove the previous binary left behind by the last update.</summary>
    public static void CleanupOldBinary()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe != null && File.Exists(exe + ".old")) File.Delete(exe + ".old");
        }
        catch { /* the old process may still be exiting; next launch gets it */ }
    }
}
