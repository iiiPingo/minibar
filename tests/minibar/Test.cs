using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Minibar;

static class Test
{
    static int fails, checks;
    static void Ok(bool c, string what) { checks++; if (!c) { fails++; Console.WriteLine("FAIL " + what); } else Console.WriteLine("ok   " + what); }
    // a running app gets closed, updated and started again (Linux stand-ins: a "Pulp" process, taskkill.exe + xdg-open shims)
    static int E2E(string root)
    {
        var fake = Process.Start(new ProcessStartInfo(Path.Combine(root, "shim", "Pulp"), "600") { UseShellExecute = false });
        Thread.Sleep(400);
        var a = new AppInfo(Catalog.All.First(x => x.Name == "Pulp")) { Exe = Path.Combine(root, "app", "Pulp.exe") };
        a.Pids = Apps.PidsNow(a.Def); a.Running = a.Pids.Count > 0;
        Ok(a.Running, "fake Pulp is running (pid " + string.Join(",", a.Pids) + ")");
        Ok(!Apps.BusyWithWork(a), "Pulp doesn't wait: the update goes ahead");
        a.Rel = new Release { Repo = "pulp", Tag = "v1.1", AssetUrl = "https://example/Pulp-1.1.zip", AssetName = "Pulp-1.1.zip" };
        Updater.DownloadOverride = (u, dir, pct) => { var f = Path.Combine(dir, "Pulp-1.1.zip"); File.Copy(Path.Combine(root, "Pulp-1.1.zip"), f); return f; };
        Updater.ExtractOverride = (zip, dir) => { using (var p = Process.Start("unzip", "-q -o \"" + zip + "\" -d \"" + dir + "\"")) { p.WaitForExit(); return p.ExitCode == 0; } };
        var msgs = new List<string>();
        Updater.Install(a, m => msgs.Add(m));
        Ok(fake.WaitForExit(3000), "the running Pulp was closed");
        Ok(msgs.Contains("Closing Pulp…"), "it said it was closing Pulp (" + string.Join(" / ", msgs) + ")");
        Ok(Apps.VersionText(a.Exe) == "1.1", "Pulp.exe is now 1.1 (" + Apps.VersionText(a.Exe) + ")");
        Ok(File.ReadAllText(Path.Combine(root, "app", "settings.ini")).Trim() == "keep=1", "its settings.ini was kept");
        Thread.Sleep(2500);   // the restarted app writes its args on its own time
        string log = File.Exists(Path.Combine(root, "log.txt")) ? File.ReadAllText(Path.Combine(root, "log.txt")) : "";
        Ok(log.Contains("taskkill") && log.IndexOf("taskkill") < log.IndexOf("started"), "closed before it was started again");
        Ok(log.Contains("started") && log.Contains("--tray"), "started again the way it runs (--tray): " + log.Replace("\n", " | "));
        Ok(Updater.LatestBackup(a) != null, "the old version was kept for Restore");

        // a second update while it's not running: no close, no restart
        File.WriteAllText(Path.Combine(root, "log.txt"), "");
        a.Pids = Apps.PidsNow(a.Def); a.Running = false;
        msgs.Clear();
        File.Copy(Path.Combine(root, "v1.0", "Pulp.exe"), a.Exe, true);
        Updater.Install(a, m => msgs.Add(m));
        Ok(!msgs.Contains("Closing Pulp…") && File.ReadAllText(Path.Combine(root, "log.txt")).Length == 0, "a closed app is updated without starting it");
        Console.WriteLine(checks + " checks, " + fails + " failed");
        return fails == 0 ? 0 : 1;
    }

    static string Rel(params string[] names) =>
        "{\"tag_name\":\"v1.6.4\",\"published_at\":\"2026-11-22T16:00:00Z\",\"assets\":[" +
        string.Join(",", names.Select(n => "{\"name\":\"" + n + "\",\"browser_download_url\":\"https://github.com/iiiPingo/minibar/releases/download/v1.6.4/" + n + "\"}")) + "]}";

    static int Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "e2e") return E2E(args[1]);

        // Pour waits only while something it started is still running (console hosts don't count)
        var procs = new List<Tuple<int, int, string>> {
            Tuple.Create(10, 1, "Pour.exe"), Tuple.Create(11, 10, "conhost.exe"), Tuple.Create(12, 10, "yt-dlp.exe"),
            Tuple.Create(13, 12, "ffmpeg.exe"), Tuple.Create(14, 12, "conhost.exe"), Tuple.Create(20, 1, "Pulp.exe") };
        var d = Native.Descendants(procs, new[] { 10 });
        Ok(d.Count == 2 && d.Contains("yt-dlp.exe") && d.Contains("ffmpeg.exe"), "Pour's downloads found (" + string.Join(",", d) + ")");
        Ok(Native.Descendants(procs.Where(x => x.Item3 != "yt-dlp.exe" && x.Item3 != "ffmpeg.exe").ToList(), new[] { 10 }).Count == 0, "an idle Pour (console host only) doesn't wait");
        Ok(Native.Descendants(procs, new[] { 20 }).Count == 0, "other apps have nothing to wait for");
        Ok(Native.Descendants(new List<Tuple<int, int, string>> { Tuple.Create(5, 5, "x.exe") }, new[] { 5 }).Count == 0, "a self-parented entry can't loop");
        var pulpDef = Catalog.All.First(x => x.Name == "Pulp");
        Ok(!pulpDef.WaitForWork && Catalog.All.Count(x => x.WaitForWork) == 1 && Catalog.All.First(x => x.WaitForWork).Name == "Pour", "only Pour waits for its work");

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
