using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Minibar;

// Minibar 1.9.0 side menu: Devices (Juice's status.txt), Refill (Refill's status.txt), the update card and the Updates badge.
// Everything lives in a temp folder: MINIBAR_DATA points Minibar's settings and crash.log there, which also keeps
// the real Start menu alone. Fake Juice / Refill files only; nothing is started.
static class SideTest
{
    static int pass, fail;
    static string home;

    static void Ok(bool ok, string name, string detail = "")
    {
        if (ok) pass++; else fail++;
        Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + name + (ok || detail.Length == 0 ? "" : "   (" + detail + ")"));
    }

    [STAThread]
    static int Main(string[] args)
    {
        home = Path.Combine(Path.GetTempPath(), "mbside-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Environment.SetEnvironmentVariable("MINIBAR_DATA", Path.Combine(home, "data"));
        Directory.CreateDirectory(Settings.Dir);
        File.WriteAllText(Settings.FilePath, "checkupdates=0\r\nstartmenu=0\r\n");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        int code;
        try
        {
            if (args.Length > 1 && args[0] == "render") code = Render(args[1]);
            else
            {
                Console.WriteLine("Minibar side menu tests in " + home);
                Logic();
                Window();
                Console.WriteLine();
                Console.WriteLine(fail == 0 ? "ALL " + pass + " PASSED" : fail + " FAILED, " + pass + " passed");
                code = fail == 0 ? 0 : 1;
            }
        }
        finally { try { Directory.Delete(home, true); } catch { } }
        return code;
    }

    // ---------------------------------------------------------------- plain logic
    static void Logic()
    {
        Ok(Settings.Dir.StartsWith(home), "settings are in the temp folder (" + Settings.Dir + ")");

        // Juice's file
        var d = SideMenu.ParseJuice("# Juice\r\nupdated=2026-11-27 14:03:05\r\ndevice=headset|82|0|Headset\r\ndevice=mouse|46|1|Mouse\r\ndevice=controller|14|0|Pad | two\r\n");
        Ok(d.Count == 3 && d[0].Name == "Headset" && d[0].Percent == 82 && !d[0].Charging && d[0].Type == "headset", "Juice: device lines read");
        Ok(d[1].Charging && d[2].Name == "Pad | two" && d[2].Low && !d[1].Low, "Juice: charging, '|' in a name, 14% is low");
        Ok(SideMenu.ParseJuice("device=mouse|20|0|M").Single().Low && !SideMenu.ParseJuice("device=mouse|21|0|M").Single().Low, "red at 20% or lower");
        Ok(SideMenu.ParseJuice("device=mouse|-1|0|Unknown\ndevice=mouse|x|0|Bad\ndevice=mouse|50|0|\ndevice=mouse|50\ndevice=\n=\nnot a line\n").Count == 0, "Juice: unknown level, bad numbers, empty names and short lines are skipped");
        Ok(SideMenu.ParseJuice(null).Count == 0 && SideMenu.ParseJuice("").Count == 0 && SideMenu.ParseJuice("\0\0\u00ff\u00fe garbage").Count == 0, "Juice: empty or garbage text = no devices");
        Ok(SideMenu.ParseJuice("device=mouse|250|0|M").Single().Percent == 100, "Juice: percent capped at 100");
        Ok(SideMenu.ParseJuice("\uFEFFdevice=mouse|5|0|M").Count == 1, "Juice: a BOM doesn't hide the first line");

        // which devices fit
        var four = new List<Device> {
            new Device { Name = "Headset", Percent = 82 }, new Device { Name = "Mouse", Percent = 46 },
            new Device { Name = "Controller", Percent = 14 }, new Device { Name = "Keyboard", Percent = 60 } };
        var two = SideMenu.Pick(four, 2);
        Ok(two.Select(x => x.Name).SequenceEqual(new[] { "Mouse", "Controller" }), "room for 2: the two lowest, in Juice's order", string.Join(",", two.Select(x => x.Name)));
        Ok(SideMenu.Pick(four, 1).Single().Name == "Controller", "room for 1: the lowest");
        Ok(SideMenu.Pick(four, 0).Count == 0 && SideMenu.Pick(four, 9).Count == 4 && SideMenu.Pick(null, 3).Count == 0, "room for none / all");

        // Refill's file
        var r = SideMenu.ParseRefill("exe=C:\\Apps\\Refill (2).exe\r\nsorted_today=12\r\nlast_file=logo.png\r\nlast_folder=Logos\r\npaused_until=\r\n");
        Ok(r != null && r.Exe == "C:\\Apps\\Refill (2).exe" && r.SortedToday == 12 && r.LastFile == "logo.png" && r.LastFolder == "Logos" && r.Paused == "", "Refill: keys read");
        var now = new DateTime(2026, 11, 27, 15, 0, 0, DateTimeKind.Utc);
        Ok(SideMenu.RefillBadge(r, now) == "12 today" && SideMenu.RefillLine(r, now) == "logo.png to Logos" && !SideMenu.IsPaused(r, now), "Refill: '12 today', 'logo.png to Logos'");
        Ok(SideMenu.ParseRefill("sorted_today=3\nlast_file=a") == null && SideMenu.ParseRefill(null) == null && SideMenu.ParseRefill("\0garbage\0") == null, "Refill: no exe line = hidden");
        var bad = SideMenu.ParseRefill("exe=C:\\R.exe\nsorted_today=lots\nunknown=1");
        Ok(bad != null && bad.SortedToday == 0 && SideMenu.RefillLine(bad, now) == "Nothing sorted yet", "Refill: odd numbers read as 0, no last file");
        r.Paused = "2026-11-27T15:42:00Z";
        Ok(SideMenu.IsPaused(r, now) && SideMenu.RefillBadge(r, now) == "Paused" && SideMenu.RefillLine(r, now) == "Resumes in 42 min", "Refill: paused, 'Resumes in 42 min'", SideMenu.RefillLine(r, now));
        r.Paused = "2026-11-27T16:30:00Z";
        Ok(SideMenu.RefillLine(r, now) == "Resumes in 1 h 30 min", "Refill: longer pauses in hours", SideMenu.RefillLine(r, now));
        r.Paused = "2026-11-27T14:00:00Z";
        Ok(!SideMenu.IsPaused(r, now) && SideMenu.RefillBadge(r, now) == "12 today", "Refill: an ended pause reads as running");
        r.Paused = "resume";
        Ok(SideMenu.IsPaused(r, now) && SideMenu.RefillLine(r, now) == "Until you resume", "Refill: paused until resumed");
        r.Paused = "off";
        Ok(SideMenu.IsPaused(r, now) && SideMenu.RefillBadge(r, now) == "Off" && SideMenu.RefillLine(r, now) == "Sorting is off", "Refill: switched off");
        r.Paused = "nonsense";
        Ok(!SideMenu.IsPaused(r, now), "Refill: an unreadable pause time doesn't pause");

        // update card words
        Ok(SideMenu.UpdateTitle(2, false, false, true) == "2 updates ready" && SideMenu.UpdateTitle(1, false, false, true) == "1 update ready", "card: 'N updates ready'");
        Ok(SideMenu.UpdateTitle(0, false, false, true) == "All up to date" && SideMenu.UpdateTitle(0, false, false, false) == "Not checked yet", "card: 'All up to date' only after a check");
        Ok(SideMenu.UpdateTitle(2, false, true, true) == "Updating…" && SideMenu.UpdateTitle(0, true, false, true) == "Checking…", "card: updating / checking");
        Ok(SideMenu.Ago(TimeSpan.FromMinutes(5)) == "5 min ago" && SideMenu.Ago(TimeSpan.FromSeconds(10)) == "just now" && SideMenu.Ago(TimeSpan.FromHours(3)) == "3 h ago", "card: 'Checked 5 min ago'");

        // reading files never throws
        string dir = Path.Combine(home, "files"); Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "status.txt");
        Ok(SideMenu.ReadSmall(f) == null && SideMenu.ReadSmall(null) == null && SideMenu.ReadSmall("") == null && SideMenu.ReadSmall("C:\\no\\such\\dir\\status.txt") == null, "missing file = null");
        File.WriteAllText(f, "device=mouse|5|0|M");
        using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Ok(SideMenu.ReadSmall(f) == null, "a file locked while it's written = null, no exception");
        File.WriteAllBytes(f, new byte[200 * 1024]);
        Ok(SideMenu.ReadSmall(f) == null, "a huge file isn't read");
        Ok(SideMenu.ReadSmall(dir) == null, "a folder named like the file = null");
        File.WriteAllBytes(f, new byte[] { 0xFF, 0xFE, 0x00, 0xD8, 0x80 });
        Ok(SideMenu.ParseJuice(SideMenu.ReadSmall(f)).Count == 0 && SideMenu.ParseRefill(SideMenu.ReadSmall(f)) == null, "binary junk = nothing shown");

        // layout: nothing overlaps at any height or DPI, the update card always shows, Devices gives way first
        bool allOk = true; string why = "";
        foreach (float sc in new[] { 1f, 1.25f, 1.5f, 2f })
            for (int h = 520; h <= 1100; h += 7)
                for (int n = 0; n <= 6; n++)
                    foreach (bool rf in new[] { false, true })
                        foreach (bool btn in new[] { false, true })
                        {
                            int H = (int)(h * sc);
                            var L = SideMenu.Arrange(H, sc, n, rf, btn);
                            string e = Overlaps(L, H, sc);
                            if (L.DevicesShown > Math.Min(n, SideMenu.MaxDevices)) e = "too many devices";
                            if (e != null) { allOk = false; why = "s=" + sc + " h=" + h + " n=" + n + " refill=" + rf + " btn=" + btn + ": " + e; }
                        }
        Ok(allOk, "no block overlaps another, the nav or the version at any height from the smallest window up (DPI 100-200%)", why);
        var small = SideMenu.Arrange(520, 1, 4, true, true);
        Ok(small.RefillTop >= 0 && small.DevicesShown >= 1 && small.DevicesShown < 4, "smallest window, everything on: Refill stays, Devices shrinks (" + small.DevicesShown + " shown)");
        var tall = SideMenu.Arrange(900, 1, 6, true, true);
        Ok(tall.DevicesShown == 4, "tall window: up to 4 devices");
        var mid = SideMenu.Arrange(660, 1, 4, true, true);
        Ok(mid.DevicesShown >= 1, "default window size: " + mid.DevicesShown + " devices beside Refill and the update card");
        var noRefill = SideMenu.Arrange(520, 1, 4, false, true);
        Ok(noRefill.RefillTop < 0 && noRefill.DevicesShown > small.DevicesShown, "without Refill the devices get its room");
        var none = SideMenu.Arrange(660, 1, 0, false, false);
        Ok(none.DevicesTop < 0 && none.RefillTop < 0 && none.DividerY < 0, "nothing to show: only the card");
    }

    static string Overlaps(SideLayout L, int H, float s)
    {
        int S(float v) => (int)Math.Round(v * s);
        int devBottom = L.DevicesTop < 0 ? -1 : L.DevicesTop + S(SideMenu.DevicesHeight(L.DevicesShown));
        int refBottom = L.RefillTop < 0 ? -1 : L.RefillTop + S(SideMenu.RefillH);
        if (L.DevicesTop >= 0 && L.DevicesTop < S(SideMenu.NavItemsBottom)) return "devices under the nav items";
        if (L.RefillTop >= 0 && L.RefillTop < S(SideMenu.NavItemsBottom)) return "refill under the nav items";
        if (L.DevicesTop >= 0 && L.RefillTop >= 0 && devBottom > L.RefillTop) return "devices run into refill";
        if (devBottom > L.CardTop || refBottom > L.CardTop) return "a block runs into the update card";
        if (L.CardTop < S(SideMenu.NavItemsBottom)) return "card under the nav items";
        if (L.CardTop + L.CardHeight > L.VersionY) return "card covers the version";
        if (L.VersionY + S(20) > H) return "version below the window";
        if (L.DevicesTop >= 0 && L.DevicesShown == 0) return "empty devices block";
        return null;
    }

    // ---------------------------------------------------------------- the real window (offscreen, temp files)
    class Rig
    {
        public MainWindow W;
        public AppInfo Juice;
        public string JuiceStatus, RefillStatus, RefillExe;
        public bool RefillOn = true;
    }

    static Rig Make()
    {
        var rig = new Rig();
        var st = Settings.Load();
        var w = new MainWindow(st) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
        w.Show(); Application.DoEvents();
        // keep the live timer from rescanning the real PC under the test
        ((Timer)typeof(MainWindow).GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w)).Stop();
        rig.W = w;

        // Juice: a fake folder with juice.log (so Minibar looks for status.txt there), running
        string jd = Path.Combine(home, "Juice"); Directory.CreateDirectory(jd);
        File.WriteAllText(Path.Combine(jd, "juice.log"), "");
        File.WriteAllBytes(Path.Combine(jd, "Juice.exe"), new byte[] { 0x4D, 0x5A });
        rig.Juice = w.AppsForTest.First(a => a.Def.Name == "Juice");
        rig.Juice.Exe = Path.Combine(jd, "Juice.exe"); rig.Juice.Running = true;
        rig.JuiceStatus = Path.Combine(jd, "status.txt");

        // Refill: a fake exe and status file
        string rd = Path.Combine(home, "Refill"); Directory.CreateDirectory(rd);
        rig.RefillExe = Path.Combine(rd, "Refill.exe"); File.WriteAllBytes(rig.RefillExe, new byte[] { 0x4D, 0x5A });
        rig.RefillStatus = Path.Combine(rd, "status.txt");
        SideMenu.RefillPathHook = rig.RefillStatus;
        SideMenu.RunningHook = () => rig.RefillOn;

        // made-up app states for the update card (every other app: as found on this PC, no release known)
        foreach (var a in w.AppsForTest) { a.Rel = null; a.Queued = false; }
        w.SelfForTest.Rel = new Release { Repo = "minibar", Tag = "v1.9.0", Published = "2026-11-27" };
        return rig;
    }

    static void Updates(Rig rig, int ready)
    {
        int k = 0;
        foreach (var a in rig.W.AppsForTest)
        {
            if (a.Def.Name == "Juice") continue;
            if (a.Exe == null) { a.Exe = Path.Combine(home, a.Def.Name + ".exe"); }
            a.Version = "1.3";
            a.Rel = k++ < ready ? new Release { Repo = a.Def.Name.ToLowerInvariant(), Tag = "v1.4.0", AssetUrl = "https://x/" + a.Def.Name + ".zip", AssetName = a.Def.Name + "-1.4.0.zip", Published = "2026-11-27" }
                                : new Release { Repo = a.Def.Name.ToLowerInvariant(), Tag = "v1.3", Published = "2026-11-01" };
        }
    }

    static void Checked(Rig rig, TimeSpan ago)
    {
        var st = (Settings)typeof(MainWindow).GetField("st", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rig.W);
        st.LastCheck = ago == TimeSpan.MaxValue ? DateTime.MinValue : DateTime.UtcNow - ago;
    }

    static void Refresh(Rig rig) { rig.Juice.Running = rig.Juice.Exe != null && rig.Juice.Running; rig.W.RefreshSideForTest(); }

    const string ThreeDevices = "updated=x\r\ndevice=headset|82|0|Headset\r\ndevice=mouse|46|0|Mouse\r\ndevice=controller|14|0|Controller\r\n";
    const string FourDevices = ThreeDevices + "device=keyboard|67|1|Keyboard\r\n";
    static string RefillText(string exe, string paused = "", int today = 12) =>
        "exe=" + exe + "\r\nsorted_today=" + today + "\r\nlast_file=logo.png\r\nlast_folder=Logos\r\npaused_until=" + paused + "\r\n";

    static void Window()
    {
        var rig = Make();
        var w = rig.W;
        int navW = (int)Math.Round(176 * Scale(w));
        long crashBefore = File.Exists(Log.Path) ? new FileInfo(Log.Path).Length : 0;
        try
        {
            File.WriteAllText(rig.JuiceStatus, ThreeDevices);
            File.WriteAllText(rig.RefillStatus, RefillText(rig.RefillExe));
            Updates(rig, 2); Checked(rig, TimeSpan.FromMinutes(5));
            Refresh(rig);
            Ok(w.SideForTest.DevicesTop >= 0 && w.ShownDevicesForTest.Count >= 1, "Devices block shows Juice's devices (" + w.ShownDevicesForTest.Count + ")");
            Ok(w.SideForTest.RefillTop >= 0, "Refill block shows");
            var btns = w.SideButtonsForTest.ToList();   // Undo, Pause, Update all
            Ok(btns.All(b => b.Visible), "Undo, Pause and Update all are shown");
            Ok(btns[1].Text == "Pause" && btns[0].Enabled, "Refill running: Pause, Undo enabled");

            // nothing overlaps in the real window, from the smallest size up
            string problem = null;
            int minH = w.MinimumSize.Height - (w.Height - w.ClientSize.Height);
            for (int h = minH; h <= minH + 700; h += 20)
            {
                w.ClientSize = new Size(w.ClientSize.Width, h); Application.DoEvents();
                if (w.ClientSize.Height != h) { problem = "couldn't size the window to " + h; break; }
                foreach (var txt in new[] { ThreeDevices, FourDevices, "" })
                {
                    File.WriteAllText(rig.JuiceStatus, txt);
                    Refresh(rig);
                    var e = Overlaps(w.SideForTest, h, Scale(w));
                    var vis = btns.Where(b => b.Visible).ToList();
                    for (int i = 0; i < vis.Count; i++)
                    {
                        if (vis[i].Right > navW || vis[i].Left < 0 || vis[i].Bottom > w.SideForTest.VersionY) e = vis[i].Text + " outside the side menu";
                        for (int j = i + 1; j < vis.Count; j++) if (vis[i].Bounds.IntersectsWith(vis[j].Bounds)) e = vis[i].Text + " covers " + vis[j].Text;
                    }
                    if (btns[0].Visible && w.SideForTest.RefillTop >= 0 && btns[0].Top < w.SideForTest.RefillTop) e = "Undo above its block";
                    if (btns[2].Visible && !new Rectangle(0, w.SideForTest.CardTop, navW, w.SideForTest.CardHeight).Contains(btns[2].Bounds)) e = "Update all outside the card";
                    if (e != null) problem = "client height " + h + ": " + e;
                }
            }
            Ok(problem == null, "real window: buttons and blocks never overlap at any height", problem ?? "");
            w.ClientSize = new Size(w.ClientSize.Width, minH); Application.DoEvents();
            File.WriteAllText(rig.JuiceStatus, FourDevices);
            Refresh(rig);
            Ok(w.SideForTest.RefillTop >= 0 && w.ShownDevicesForTest.Count >= 1 && w.ShownDevicesForTest.Count < 4 && w.ShownDevicesForTest.Any(d => d.Name == "Controller"),
                "smallest window: Refill kept, Devices shrinks and keeps the lowest battery (" + string.Join(", ", w.ShownDevicesForTest.Select(d => d.Name)) + ")");

            w.ClientSize = new Size(w.ClientSize.Width, 900); Application.DoEvents();
            // live: a new reading shows on the next refresh
            File.WriteAllText(rig.JuiceStatus, "device=mouse|9|0|Mouse\r\n");
            Refresh(rig);
            Ok(w.ShownDevicesForTest.Count == 1 && w.ShownDevicesForTest[0].Percent == 9, "a new Juice reading shows on the next refresh");

            // hidden when Juice isn't running or has no devices
            rig.Juice.Running = false; w.RefreshSideForTest();
            Ok(w.SideForTest.DevicesTop < 0, "Juice not running: Devices hidden");
            rig.Juice.Running = true;
            File.WriteAllText(rig.JuiceStatus, "updated=x\r\n"); Refresh(rig);
            Ok(w.SideForTest.DevicesTop < 0, "Juice with no devices: Devices hidden");
            File.Delete(rig.JuiceStatus); Refresh(rig);
            Ok(w.SideForTest.DevicesTop < 0, "no status.txt (older Juice): Devices hidden");

            // Refill hidden when the file or exe is missing or it isn't running
            rig.RefillOn = false; Refresh(rig);
            Ok(w.SideForTest.RefillTop < 0 && !btns[0].Visible && !btns[1].Visible, "Refill not running: block and buttons hidden");
            rig.RefillOn = true;
            File.WriteAllText(rig.RefillStatus, RefillText(Path.Combine(home, "gone", "Refill.exe"))); Refresh(rig);
            Ok(w.SideForTest.RefillTop < 0, "Refill's exe missing: hidden");
            File.Delete(rig.RefillStatus); Refresh(rig);
            Ok(w.SideForTest.RefillTop < 0, "Refill's status.txt missing: hidden");
            File.WriteAllText(rig.RefillStatus, RefillText(rig.RefillExe, DateTime.UtcNow.AddMinutes(42).ToString("yyyy-MM-ddTHH:mm:ssZ"))); Refresh(rig);
            Ok(w.SideForTest.RefillTop >= 0 && btns[1].Text == "Resume", "paused: the button says Resume");
            File.WriteAllText(rig.RefillStatus, RefillText(rig.RefillExe, "off")); Refresh(rig);
            Ok(btns[1].Text == "Turn on", "switched off: the button says Turn on");
            File.WriteAllText(rig.RefillStatus, "exe=" + rig.RefillExe + "\r\nsorted_today=0\r\nlast_file=\r\n"); Refresh(rig);
            Ok(!btns[0].Enabled && btns[1].Text == "Pause", "nothing sorted yet: Undo is greyed out");

            // broken files never crash anything
            File.WriteAllBytes(rig.JuiceStatus, new byte[] { 0, 1, 2, 0xFF, 0xFE, (byte)'=', (byte)'|' });
            File.WriteAllBytes(rig.RefillStatus, new byte[300 * 1024]);
            Refresh(rig);
            Ok(w.SideForTest.DevicesTop < 0 && w.SideForTest.RefillTop < 0, "garbage status files: both blocks hidden");
            Directory.CreateDirectory(rig.JuiceStatus + ".d");
            File.Delete(rig.JuiceStatus); Directory.Move(rig.JuiceStatus + ".d", rig.JuiceStatus);
            Refresh(rig);
            Ok(w.SideForTest.DevicesTop < 0, "a folder where status.txt should be: hidden");
            Directory.Delete(rig.JuiceStatus);

            // update card and badge
            File.WriteAllText(rig.RefillStatus, RefillText(rig.RefillExe));
            Updates(rig, 2); Refresh(rig);
            Ok(btns[2].Visible && w.SideForTest.CardButton, "2 updates ready: Update all shows");
            Ok(Badge(w), "Updates tab has a badge");
            Updates(rig, 0); Refresh(rig);
            Ok(!btns[2].Visible && !w.SideForTest.CardButton, "all up to date: no button");
            Ok(!Badge(w), "no badge at zero");
            Checked(rig, TimeSpan.MaxValue); Refresh(rig);
            Ok(!btns[2].Visible, "never checked: no button");

            long crashAfter = File.Exists(Log.Path) ? new FileInfo(Log.Path).Length : 0;
            Ok(crashAfter == crashBefore, "nothing went to crash.log", crashAfter > crashBefore ? File.ReadAllText(Log.Path) : "");
        }
        catch (Exception ex) { Ok(false, "window tests crashed", ex.ToString()); }
        finally { w.Dispose(); SideMenu.RunningHook = null; SideMenu.RefillPathHook = null; }
    }

    // the client area as a picture (Form.DrawToBitmap draws the title bar too)
    static Bitmap Snap(MainWindow w)
    {
        using (var all = new Bitmap(w.Width, w.Height))
        {
            w.DrawToBitmap(all, new Rectangle(Point.Empty, all.Size));
            var o = w.PointToScreen(Point.Empty);
            return all.Clone(new Rectangle(o.X - w.Left, o.Y - w.Top, w.ClientSize.Width, w.ClientSize.Height), all.PixelFormat);
        }
    }

    static float Scale(MainWindow w) => (float)typeof(MainWindow).GetField("s", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w);

    // the badge's middle pixel is the accent colour
    static bool Badge(MainWindow w)
    {
        float s = Scale(w);
        int S(float v) => (int)Math.Round(v * s);
        var item = new Rectangle(S(10), S(70) + 1 * S(40), S(176) - S(20), S(36));
        int cx = item.Right - S(10) - S(10), cy = item.Y + S(18) - S(6);   // above the number's glyph
        using (var bmp = Snap(w))
        {
            var c = bmp.GetPixel(cx, cy);
            return Math.Abs(c.R - Theme.Accent.R) < 12 && Math.Abs(c.G - Theme.Accent.G) < 12 && Math.Abs(c.B - Theme.Accent.B) < 12;
        }
    }

    // ---------------------------------------------------------------- pictures of the side menu (and the whole window)
    static int Render(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var rig = Make();
        var w = rig.W;
        float s = Scale(w);
        int navW = (int)Math.Round(176 * s);
        int normalH = (int)Math.Round(660 * s);
        int minH = w.MinimumSize.Height - (w.Height - w.ClientSize.Height);

        void Shot(string name, int h, bool whole = false)
        {
            w.ClientSize = new Size((int)Math.Round(880 * s), h); Application.DoEvents();
            Refresh(rig);
            using (var bmp = Snap(w))
            {
                if (whole) bmp.Save(Path.Combine(outDir, name + ".png"));
                else using (var crop = bmp.Clone(new Rectangle(0, 0, navW, bmp.Height), bmp.PixelFormat)) crop.Save(Path.Combine(outDir, name + ".png"));
            }
        }

        // like the mockup: devices, Refill sorting, 2 updates ready
        File.WriteAllText(rig.JuiceStatus, ThreeDevices);
        File.WriteAllText(rig.RefillStatus, RefillText(rig.RefillExe));
        Updates(rig, 2); Checked(rig, TimeSpan.FromMinutes(5));
        Shot("1-side-full", normalH);
        Shot("1-window-full", normalH, true);
        // smallest window, 4 devices: Devices shrinks to the lowest batteries
        File.WriteAllText(rig.JuiceStatus, FourDevices);
        Shot("2-side-smallest-window", minH);
        Shot("2-window-smallest", minH, true);
        // tall window: all 4 devices
        Shot("3-side-tall", (int)Math.Round(900 * s));
        // like the second mockup: Juice closed, Refill paused, all up to date
        rig.Juice.Running = false;
        File.WriteAllText(rig.RefillStatus, RefillText(rig.RefillExe, DateTime.UtcNow.AddMinutes(42).AddSeconds(30).ToString("yyyy-MM-ddTHH:mm:ssZ")));
        Updates(rig, 0);
        Shot("4-side-paused-up-to-date", normalH);
        // nothing else running, never checked
        rig.RefillOn = false;
        Checked(rig, TimeSpan.MaxValue);
        Shot("5-side-nothing", normalH);
        // updating: Pour waits for its downloads
        rig.Juice.Running = true; rig.RefillOn = true;
        File.WriteAllText(rig.JuiceStatus, "device=headset|100|1|WH-1000XM5\r\ndevice=mouse|20|0|Attack Shark X1 with a very long name\r\n");
        File.WriteAllText(rig.RefillStatus, RefillText(rig.RefillExe, "resume"));
        Updates(rig, 2); Checked(rig, TimeSpan.FromHours(2));
        var pour = w.AppsForTest.First(a => a.Def.Name == "Pour"); pour.Queued = true;
        Shot("6-side-updating-charging-long-name", normalH);
        pour.Queued = false;
        w.Dispose();
        SideMenu.RunningHook = null; SideMenu.RefillPathHook = null;
        Console.WriteLine("Pictures in " + outDir);
        return 0;
    }
}
