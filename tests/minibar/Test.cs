using System;
using System.IO;
using System.Linq;
using Minibar;

static class Test
{
    static int fails, checks;
    static void Ok(bool c, string what) { checks++; if (!c) { fails++; Console.WriteLine("FAIL " + what); } else Console.WriteLine("ok   " + what); }
    static string Rel(params string[] names) =>
        "{\"tag_name\":\"v1.6.4\",\"published_at\":\"2026-11-22T16:00:00Z\",\"assets\":[" +
        string.Join(",", names.Select(n => "{\"name\":\"" + n + "\",\"browser_download_url\":\"https://github.com/iiiPingo/minibar/releases/download/v1.6.4/" + n + "\"}")) + "]}";

    static int Main(string[] args)
    {
        // which file Minibar's updater downloads, now that releases also carry a setup exe
        var r = new Release(); Updater.Parse(Rel("Minibar-Setup-1.6.4.exe", "Minibar-1.6.4.zip", "Minibar.exe"), "Minibar", r);
        Ok(r.AssetName == "Minibar-1.6.4.zip", "self-update takes the zip, not the setup (" + r.AssetName + ")");
        r = new Release(); Updater.Parse(Rel("Minibar.exe", "Minibar-Setup-1.6.4.exe"), "Minibar", r);
        Ok(r.AssetName == "Minibar.exe", "without a zip: the plain exe, never the setup (" + r.AssetName + ")");
        r = new Release(); Updater.Parse(Rel("Minibar-Setup-1.6.4.exe"), "Minibar", r);
        Ok(r.AssetUrl == null, "a setup-only release is never downloaded as an update");

        // seen.<App>= round trip + used to find an app again
        var home = Path.Combine(Path.GetTempPath(), "mbtest-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(home, "cfg"));
        Ok(Settings.Dir.StartsWith(home), "settings dir redirected for the test (" + Settings.Dir + ")");
        string pulp = Path.Combine(home, "Desktop", "Pulp", "Pulp.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(pulp));
        File.WriteAllBytes(pulp, new byte[] { 0x4D, 0x5A });
        var s = new Settings();
        s.Seen["Pulp"] = pulp;
        s.Located["Fizz"] = @"C:\x\Fizz.exe";
        s.Save();
        var t = Settings.Load();
        Ok(t.Seen.TryGetValue("Pulp", out var p) && p == pulp, "seen.Pulp survives save/load");
        Ok(t.Located["Fizz"] == @"C:\x\Fizz.exe", "other settings still saved");
        Ok(File.ReadAllText(Settings.FilePath).Contains("seen.Pulp="), "settings.ini has the seen line");

        var a = new AppInfo(Catalog.All.First(d => d.Name == "Pulp"));
        Apps.Locate(a, t, null);
        Ok(a.Exe == pulp, "Pulp found where Minibar saw it last (" + a.Exe + ")");

        var t2 = new Settings();
        var b = new AppInfo(Catalog.All.First(d => d.Name == "Pulp"));
        Apps.Locate(b, t2, pulp);   // found as the running copy
        Ok(t2.Seen.TryGetValue("Pulp", out var p2) && p2 == pulp, "a found app is remembered");
        var c = new AppInfo(Catalog.All.First(d => d.Name == "Pulp"));
        var t3 = new Settings(); t3.Located["Pulp"] = pulp;
        Apps.Locate(c, t3, null);
        Ok(!t3.Seen.ContainsKey("Pulp"), "a Located pick isn't duplicated into seen");
        File.Delete(pulp);
        var d2 = new AppInfo(Catalog.All.First(d => d.Name == "Pulp"));
        Apps.Locate(d2, t, null);
        Ok(d2.Exe == null, "a deleted copy isn't returned");
        var old = Path.Combine(home, "Desktop", "Pulp", "Pulp.old.exe"); File.WriteAllBytes(old, new byte[] { 0x4D, 0x5A });
        t.Seen["Pulp"] = old; Apps.Locate(d2, t, null);
        Ok(d2.Exe == null, "an .old exe is never picked");

        InstallInfo.Sync();   // no key: nothing happens, no throw
        Ok(true, "InstallInfo.Sync without a setup install is a no-op");
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(InstallInfo.Key))
        { k.SetValue("InstallLocation", Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\"); k.SetValue("DisplayVersion", "0.1"); }
        InstallInfo.Sync();
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(InstallInfo.Key))
            Ok(k.GetValue("DisplayVersion") as string == Apps.VersionText(System.Windows.Forms.Application.ExecutablePath), "installed copy updates DisplayVersion (" + k.GetValue("DisplayVersion") + ")");
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(InstallInfo.Key)) { k.SetValue("InstallLocation", "/somewhere/else"); k.SetValue("DisplayVersion", "0.1"); }
        InstallInfo.Sync();
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(InstallInfo.Key))
            Ok(k.GetValue("DisplayVersion") as string == "0.1", "a portable copy elsewhere leaves the installed entry alone");
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(InstallInfo.Key, false);
        try { Directory.Delete(home, true); } catch { }
        Console.WriteLine(checks + " checks, " + fails + " failed");
        return fails == 0 ? 0 : 1;
    }
}
