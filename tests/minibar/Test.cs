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
        a.Pids = Apps.PidsNow(a); a.Running = a.Pids.Count > 0;
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
        a.Pids = Apps.PidsNow(a); a.Running = false;
        msgs.Clear();
        File.Copy(Path.Combine(root, "v1.0", "Pulp.exe"), a.Exe, true);
        Updater.Install(a, m => msgs.Add(m));
        Ok(!msgs.Contains("Closing Pulp…") && File.ReadAllText(Path.Combine(root, "log.txt")).Length == 0, "a closed app is updated without starting it");
        Console.WriteLine(checks + " checks, " + fails + " failed");
        return fails == 0 ? 0 : 1;
    }

    static byte[] FakeExe(byte fill) { var b = new byte[4096]; for (int i = 0; i < b.Length; i++) b[i] = fill; b[0] = 0x4D; b[1] = 0x5A; return b; }
    static string Ls(string dir) => string.Join(", ", Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x));

    // 1.7.2: renamed exes, self-update undo, loose folders, zip temp folders, rollback skip, restore cleanup, startup
    static void Fixes172(string tmpHome)
    {
        // not under Temp: Minibar now (rightly) treats anything in Temp as a zip's throwaway copy
        string home = Path.Combine(Path.GetDirectoryName(typeof(Test).Assembly.Location), "fixes-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(home);
        Ok(!Apps.InTemp(home), "test folder is outside Temp (" + home + ")");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(home, "local"));
        string real = typeof(Test).Assembly.Location;   // a real exe with a version resource (ProductName MinibarTests)
        string minibar = Path.Combine(Path.GetDirectoryName(real), "Minibar.exe");
        if (!File.Exists(minibar)) minibar = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(real), "..", "..", "..", "..", "..", "src", "bin", "Release", "net48", "Minibar.exe"));
        Ok(File.Exists(minibar), "a built Minibar.exe to test with (" + minibar + ")");

        // --- self-update keeps the real name and never touches a loose folder's other files
        string dl = Path.Combine(home, "Downloads"); Directory.CreateDirectory(dl);
        string mine = Path.Combine(dl, "Minibar (1).exe"); File.Copy(minibar, mine, true);
        File.WriteAllText(Path.Combine(dl, "README.txt"), "his own readme");
        Updater.DownloadOverride = (u, dir, pct) => { var f = Path.Combine(dir, "Minibar-9.9.zip"); File.WriteAllText(f, "PK"); return f; };
        Updater.ExtractOverride = (zip, dir) =>
        {
            var d = Path.Combine(dir, "Minibar"); Directory.CreateDirectory(d);
            File.WriteAllBytes(Path.Combine(d, "Minibar.exe"), FakeExe(7));
            File.WriteAllText(Path.Combine(d, "Minibar.exe.config"), "<configuration/>");
            File.WriteAllText(Path.Combine(d, "README.txt"), "MINIBAR README");
            return true;
        };
        string started = null;
        Updater.SelfStartOverride = (exe, args) => started = exe;
        Release R(string app) => new Release { Repo = app.ToLowerInvariant(), Tag = "v9.9", AssetUrl = "https://example/x.zip", AssetName = app + "-9.9.zip" };
        var self = new AppInfo(Catalog.Minibar) { Exe = mine, Rel = R("Minibar") };
        try { Updater.SelfUpdate(self, m => { }); } catch (Exception ex) { Ok(false, "self-update threw: " + ex.Message); }
        Ok(started == mine && File.ReadAllBytes(mine).SequenceEqual(FakeExe(7)), "renamed Minibar (1).exe: new version written under its own name and started (" + Path.GetFileName(started) + ")");
        Ok(File.ReadAllText(Path.Combine(dl, "README.txt")) == "his own readme", "a loose Minibar never overwrites README.txt beside it");
        Ok(!File.Exists(Path.Combine(dl, "Minibar.exe")) && !File.Exists(mine + ".config"), "no extra Minibar.exe / .config dropped in Downloads (" + Ls(dl) + ")");

        // a start that fails puts everything back
        File.Copy(minibar, mine, true);
        foreach (var f in Directory.GetFiles(dl, "Minibar.old*.exe")) File.Delete(f);
        byte[] before = File.ReadAllBytes(mine);
        Updater.SelfStartOverride = (exe, args) => { throw new System.ComponentModel.Win32Exception(2); };
        bool threw = false;
        try { Updater.SelfUpdate(self, m => { }); } catch { threw = true; }
        Ok(threw && File.ReadAllBytes(mine).SequenceEqual(before) && !Directory.GetFiles(dl, "Minibar.old*.exe").Any(), "failed start: the old Minibar is back under its name, no .old left (" + Ls(dl) + ")");

        // in Minibar's own folder the README comes along
        string own = Path.Combine(home, "Apps", "Minibar"); Directory.CreateDirectory(own);
        string ownExe = Path.Combine(own, "Minibar.exe"); File.Copy(minibar, ownExe, true);
        Updater.SelfStartOverride = (exe, args) => started = exe;
        try { Updater.SelfUpdate(new AppInfo(Catalog.Minibar) { Exe = ownExe, Rel = R("Minibar") }, m => { }); } catch (Exception ex) { Ok(false, "own-folder self-update threw: " + ex.Message); }
        Ok(File.Exists(Path.Combine(own, "README.txt")) && File.Exists(ownExe + ".config"), "Minibar's own folder gets README + config (" + Ls(own) + ")");

        // --- which files are the app
        var pulpDef = Catalog.All.First(x => x.Name == "Pulp");
        var juiceDef = Catalog.All.First(x => x.Name == "Juice");
        string ren = Path.Combine(home, "ren"); Directory.CreateDirectory(ren);
        string mb2 = Path.Combine(ren, "Minibar (2).exe"); File.Copy(minibar, mb2, true);
        Ok(Apps.IsAppExe(mb2, Catalog.Minibar), "a renamed exe is recognised by its version resource");
        string notPulp = Path.Combine(ren, "Pulp (2).exe"); File.Copy(minibar, notPulp, true);
        Ok(!Apps.IsAppExe(notPulp, pulpDef), "an exe that says it's something else isn't taken for Pulp");
        string juice2 = Path.Combine(ren, "Juice (2).exe"); File.WriteAllBytes(juice2, FakeExe(1));
        Ok(Apps.IsAppExe(juice2, juiceDef), "no version resource (Juice): a download-style name is enough");
        string juiceOther = Path.Combine(ren, "Juicer.exe"); File.WriteAllBytes(juiceOther, FakeExe(1));
        Ok(!Apps.IsAppExe(juiceOther, juiceDef), "but not any name that starts with Juice");
        string oldOne = Path.Combine(ren, "Juice.old.exe"); File.WriteAllBytes(oldOne, FakeExe(1));
        Ok(!Apps.IsAppExe(oldOne, juiceDef), "an .old exe still never counts");
        var js = new Settings(); js.Seen["Juice"] = juice2;
        var ja = new AppInfo(juiceDef); Apps.Locate(ja, js, null);
        Ok(ja.Exe == juice2, "a renamed Juice (2).exe is found (" + ja.Exe + ")");

        // --- an update of a renamed app replaces that file, and Restore puts it back
        string pd = Path.Combine(home, "PulpApp"); Directory.CreateDirectory(pd);
        string pulp2 = Path.Combine(pd, "Pulp (2).exe"); File.WriteAllBytes(pulp2, FakeExe(2));
        Updater.ExtractOverride = (zip, dir) => { var d = Path.Combine(dir, "Pulp"); Directory.CreateDirectory(d); File.WriteAllBytes(Path.Combine(d, "Pulp.exe"), FakeExe(3)); return true; };
        Updater.DownloadOverride = (u, dir, pct) => { var f = Path.Combine(dir, "Pulp-9.9.zip"); File.WriteAllText(f, "PK"); return f; };
        var pa = new AppInfo(pulpDef) { Exe = pulp2, Rel = R("Pulp") };
        try { Updater.Install(pa, m => { }); } catch (Exception ex) { Ok(false, "renamed update threw: " + ex.Message); }
        Ok(File.ReadAllBytes(pulp2).SequenceEqual(FakeExe(3)) && !File.Exists(Path.Combine(pd, "Pulp.exe")), "Pulp (2).exe itself is updated, no second Pulp.exe (" + Ls(pd) + ")");
        try { Updater.Rollback(pa, m => { }); } catch (Exception ex) { Ok(false, "rollback threw: " + ex.Message); }
        Ok(File.ReadAllBytes(pulp2).SequenceEqual(FakeExe(2)) && !File.Exists(Path.Combine(pd, "Pulp.exe")), "Restore puts the old Pulp (2).exe back (" + Ls(pd) + ")");

        // --- opened inside a zip (Explorer's temp copy): apps go to %LOCALAPPDATA%\Programs, never Temp
        string zipped = Path.Combine(Path.GetTempPath(), "Temp1_Minibar-1.7.2.zip", "Minibar", "Minibar.exe");
        Ok(Apps.InTemp(zipped) && !Apps.InTemp(ownExe), "a copy in Temp is noticed, a normal folder isn't");
        Ok(Apps.AppsHome(zipped) == Apps.ProgramsDir, "apps from a zipped Minibar go to Programs (" + Apps.AppsHome(zipped) + ")");
        bool zipThrew = false;
        try { Updater.SelfUpdate(new AppInfo(Catalog.Minibar) { Exe = zipped, Rel = R("Minibar") }, m => { }); } catch { zipThrew = true; }
        Ok(zipThrew, "a zipped Minibar doesn't update itself into Temp");

        // --- skip.<App>= survives save/load
        var sk = new Settings(); sk.Skip["Pulp"] = "v9.9"; sk.Save();
        Ok(Settings.Load().Skip.TryGetValue("Pulp", out var sv) && sv == "v9.9", "skip.Pulp survives save/load");

        // --- a restore only ever deletes its own unpack folder
        string precious = Path.Combine(Updater.Root, "backup"); Directory.CreateDirectory(precious);
        File.WriteAllText(Path.Combine(precious, "keep.txt"), "x");
        SettingsBackup.Discard(new SettingsBackup.Contents { Root = precious, Dir = precious });
        Ok(File.Exists(Path.Combine(precious, "keep.txt")), "restore cleanup never deletes the update backups");
        string unpack = Path.Combine(Updater.Root, "restore-123"); Directory.CreateDirectory(Path.Combine(unpack, "backup"));
        SettingsBackup.Discard(new SettingsBackup.Contents { Root = unpack, Dir = Path.Combine(unpack, "backup") });
        Ok(!Directory.Exists(unpack), "its own restore- folder is removed");

        // --- start with Windows: registry work returns what settings should remember
        var pon = new AppInfo(pulpDef) { Exe = pulp2 };
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(runKey)) k.SetValue("Pulp", "\"" + pulp2 + "\" --tray --custom");
        var off = Apps.SetStartup(pon, null, false);
        Ok(off != null && off.Item1 && off.Item2.Contains("--custom") && Apps.RunValue("Pulp") == null, "off: the app's own command is handed back to keep");
        var on = Apps.SetStartup(pon, off.Item2, true);
        Ok(on != null && !on.Item1 && (Apps.RunValue("Pulp") ?? "").Contains("--custom"), "on: the kept command goes back exactly");
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(runKey)) k.DeleteValue("Pulp", false);

        Updater.DownloadOverride = null; Updater.ExtractOverride = null; Updater.SelfStartOverride = null;
        try { Directory.Delete(home, true); } catch { }
    }

    // offscreen renders of every page (xvfb-run mono MinibarTests.exe render <dir>), with made-up app states.
    // Mono doesn't draw memory-loaded fonts in controls, so this uses the installed Lexend (copy the TTFs to ~/.fonts).
    static int Render(string outDir)
    {
        Directory.CreateDirectory(outDir);
        string home = Path.Combine(Path.GetTempPath(), "mbrender-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(home, "cfg"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(home, "local"));
        Directory.CreateDirectory(Settings.Dir);
        File.WriteAllText(Settings.FilePath, "checkupdates=0\nstartmenu=0\n");
        AppFonts.UseInstalled("Lexend Medium", "Lexend");
        Native.IconSource = (path, size) => Native.BuiltInIcon(Path.GetFileNameWithoutExtension(path).Split(' ')[0], size);

        var st = Settings.Load();
        var w = new MainWindow(st) { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-4000, -4000) };
        w.Show(); System.Windows.Forms.Application.DoEvents();

        // made-up states: every kind of card and row
        foreach (var a in w.AppsForTest)
        {
            if (a.Def.Name == "Juice") { a.Exe = null; a.Version = null; continue; }   // not installed
            string exe = Path.Combine(home, "apps", a.Def.Name, a.Def.Name + ".exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllBytes(exe, FakeExe(1));
            a.Exe = exe; a.Version = "1.3"; a.Startup = true;
        }
        Func<string, AppInfo> A = n => w.AppsForTest.First(x => x.Def.Name == n);
        var rel = new Func<string, Release>(n => new Release { Repo = n.ToLowerInvariant(), Tag = "v1.4.2", AssetUrl = "https://x/" + n + ".zip", AssetName = n + ".zip", Published = "2026-11-20" });
        var pulp = A("Pulp"); pulp.Running = true; pulp.Since = DateTime.Now.AddHours(-3).AddMinutes(-12); pulp.Pids = new List<int> { 1 }; pulp.Rel = rel("Pulp");
        var echoa = A("Echoa"); echoa.Running = true; echoa.Since = DateTime.Now.AddMinutes(-40); echoa.Pids = new List<int> { 2 };
        echoa.Rel = new Release { Repo = "echoa", Tag = "v1.3", Published = "2026-11-01" }; echoa.Backup = home; echoa.BackupLabel = "v1.2.5";
        var pour = A("Pour"); pour.Running = true; pour.Since = DateTime.Now.AddDays(-1).AddHours(-2); pour.Pids = new List<int> { 3 }; pour.Rel = rel("Pour"); pour.Queued = true;
        var fizz = A("Fizz"); fizz.Running = false; fizz.PawnIO = false; fizz.PawnSetup = "PawnIO_setup.exe"; fizz.Startup = null; fizz.StartupNote = "Set up from Fizz itself the first time.";
        fizz.Error = "Fizz didn't close."; fizz.ErrorUntil = DateTime.Now.AddMinutes(5);
        w.SelfForTest.Rel = new Release { Repo = "minibar", Tag = "v1.7.2", Published = "2026-11-23" };
        foreach (var a in w.AppsForTest) a.Icon = null;

        void Shot(string name, int page, int width)
        {
            w.ShowPageForTest(page);
            w.ClientSize = new System.Drawing.Size(width, w.ClientSize.Height);
            System.Windows.Forms.Application.DoEvents();
            foreach (var a in w.AppsForTest) { if (a.Def.Name == "Pulp" || a.Def.Name == "Echoa" || a.Def.Name == "Pour") a.Running = true; }
            using (var bmp = new System.Drawing.Bitmap(w.ClientSize.Width, w.ClientSize.Height))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    // Mono's Form.DrawToBitmap leaves the page panels out: paint the form, then each visible child
                    typeof(MainWindow).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(w, new object[] { new System.Windows.Forms.PaintEventArgs(g, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bmp.Size)) });
                    // and Mono's Control.DrawToBitmap leaves children out too: draw every visible control, back to front
                    void Draw(System.Windows.Forms.Control c, System.Drawing.Point at)
                    {
                        if (!c.Visible || c.Width <= 0 || c.Height <= 0) return;
                        using (var cb = new System.Drawing.Bitmap(c.Width, c.Height)) { c.DrawToBitmap(cb, new System.Drawing.Rectangle(System.Drawing.Point.Empty, c.Size)); g.DrawImage(cb, at); }
                        for (int i = c.Controls.Count - 1; i >= 0; i--)
                        {
                            var k = c.Controls[i];
                            Draw(k, new System.Drawing.Point(at.X + k.Left, at.Y + k.Top));
                        }
                    }
                    for (int i = w.Controls.Count - 1; i >= 0; i--) Draw(w.Controls[i], w.Controls[i].Location);
                }
                bmp.Save(Path.Combine(outDir, name + ".png"));
            }
        }
        // the whole card list at once (the Fizz card with its notice is below the fold)
        void Cards(string name, int width)
        {
            var l = w.ListForTest;
            var old = l.Size;
            l.Size = new System.Drawing.Size(width, 1400);
            foreach (var a in w.AppsForTest) { if (a.Def.Name == "Pulp" || a.Def.Name == "Echoa" || a.Def.Name == "Pour") a.Running = true; }
            using (var bmp = new System.Drawing.Bitmap(l.Width, l.Height))
            {
                l.DrawToBitmap(bmp, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bmp.Size));
                bmp.Save(Path.Combine(outDir, name + ".png"));
            }
            l.Size = old;
        }
        int normal = w.ClientSize.Width, narrow = w.MinimumSize.Width - (w.Width - w.ClientSize.Width);
        Shot("1-apps", 0, normal);
        Shot("2-updates", 1, normal);
        Shot("3-settings", 2, normal);
        Cards("4-cards", w.ListForTest.Width);
        Shot("5-apps-narrow", 0, narrow);
        Shot("6-updates-narrow", 1, narrow);
        Shot("7-settings-narrow", 2, narrow);
        Cards("8-cards-narrow", w.ListForTest.Width);
        fizz.PawnIO = true; fizz.Error = null;
        Cards("9-cards-pawnio-installed", w.ListForTest.Width);
        w.Dispose();
        try { Directory.Delete(home, true); } catch { }
        return 0;
    }

    static string Rel(params string[] names) =>
        "{\"tag_name\":\"v1.6.4\",\"published_at\":\"2026-11-22T16:00:00Z\",\"assets\":[" +
        string.Join(",", names.Select(n => "{\"name\":\"" + n + "\",\"browser_download_url\":\"https://github.com/iiiPingo/minibar/releases/download/v1.6.4/" + n + "\"}")) + "]}";

    static int Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "e2e") return E2E(args[1]);
        if (args.Length > 1 && args[0] == "render") return Render(args[1]);

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

        Fixes172(home);

        // 1.8.0: Lexend built in (Medium for text, Bold for names/headings/buttons; the Bold file has only a Bold face)
        Ok(AppFonts.Medium != null && AppFonts.Bold != null, "Lexend loads from the exe's resources");
        // (Windows names them "Lexend Medium" and "Lexend"; Mono's fontconfig calls both "Lexend")
        Ok((AppFonts.Medium?.Name ?? "").StartsWith("Lexend") && AppFonts.Bold?.Name == "Lexend", "families: " + AppFonts.Medium?.Name + " / " + AppFonts.Bold?.Name);
        using (var f1 = Theme.Ui(10f)) Ok(f1.FontFamily.Name.StartsWith("Lexend") && !f1.Bold, "Theme.Ui is Lexend Medium, regular style (" + f1.FontFamily.Name + ", " + f1.Style + ")");
        using (var f2 = Theme.UiSemi(10f)) Ok(f2.FontFamily.Name == "Lexend" && f2.Bold, "Theme.UiSemi is Lexend Bold (" + f2.FontFamily.Name + ", " + f2.Style + ")");
        using (var f3 = Theme.UiTitle(15f)) Ok(f3.FontFamily.Name == "Lexend" && f3.Bold, "Theme.UiTitle is Lexend Bold");
        Ok(Catalog.All.Where(x => x.Openable).Select(x => x.Name).OrderBy(x => x).SequenceEqual(new[] { "Fizz", "Pulp" }), "only Fizz and Pulp get Start-opens + Open");
        try { Directory.Delete(home, true); } catch { }
        Console.WriteLine(checks + " checks, " + fails + " failed");
        return fails == 0 ? 0 : 1;
    }
}
