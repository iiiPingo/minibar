using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using MinibarSetup;

static class Test
{
    static int fails, checks;
    static void Ok(bool c, string what) { checks++; if (!c) { fails++; Console.WriteLine("FAIL " + what); } else Console.WriteLine("ok   " + what); }
    static byte[] Res(string n) { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(n)) using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); } }
    static bool SameBytes(string f, byte[] b) => File.Exists(f) && File.ReadAllBytes(f).SequenceEqual(b);

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "render") return Render(args[1]);
        string root = Path.Combine(Path.GetTempPath(), "mbsetup-test-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(root);
        Inst.Dir = Path.Combine(root, "Local", "Programs", "Minibar");
        Inst.StartLink = Path.Combine(root, "Start", "Minibar.lnk");
        Inst.DesktopLink = Path.Combine(root, "Desktop", "Minibar.lnk");
        Inst.DataDir = Path.Combine(root, "Roaming", "Minibar");
        Inst.LocalDataDir = Path.Combine(root, "Local", "Minibar");
        string fakeSetup = Path.Combine(root, "Downloads", "Minibar-Setup-1.7.0.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeSetup));
        File.WriteAllBytes(fakeSetup, new byte[] { 0x4D, 0x5A, 1, 2, 3 });
        Inst.SelfPath = fakeSetup;
        try { Registry.CurrentUser.DeleteSubKeyTree(Inst.RegKey, false); } catch { }

        // his settings + backups that must survive
        Directory.CreateDirectory(Inst.DataDir);
        File.WriteAllText(Path.Combine(Inst.DataDir, "settings.ini"), "accent=#FF5C9E\npath.Pulp=C:\\x\\Pulp.exe\n");
        Directory.CreateDirectory(Path.Combine(Inst.LocalDataDir, "backup", "Pulp"));
        File.WriteAllText(Path.Combine(Inst.LocalDataDir, "backup", "Pulp", "keep.txt"), "x");
        string settingsBefore = File.ReadAllText(Path.Combine(Inst.DataDir, "settings.ini"));

        byte[] exe = Res("payload/Minibar.exe");
        Ok(Inst.PayloadVersion == "1.8", "payload version 1.7 (" + Inst.PayloadVersion + ")");
        Ok(Inst.InstalledVersion == null, "nothing installed at first");

        // 1. fresh install
        var res = Inst.Install(true, null, m => { });
        Ok(SameBytes(Inst.Exe, exe), "fresh: Minibar.exe written");
        Ok(File.Exists(Path.Combine(Inst.Dir, "Minibar.exe.config")) && File.Exists(Path.Combine(Inst.Dir, "README.txt")), "fresh: config + README");
        Ok(SameBytes(Inst.Uninstaller, File.ReadAllBytes(fakeSetup)), "fresh: uninstaller is a copy of the setup");
        Ok(!Directory.GetFiles(Inst.Dir).Any(f => f.EndsWith(".setup-new") || f.Contains(".old")), "fresh: no temp/old files left");
        Ok(Inst.InstalledVersion == "1.8", "fresh: installed version reads 1.7.0 (" + Inst.InstalledVersion + ")");
        using (var k = Registry.CurrentUser.OpenSubKey(Inst.RegKey))
        {
            Ok(k != null, "registry: uninstall key exists");
            Ok(k?.GetValue("DisplayName") as string == "Minibar", "registry: DisplayName");
            Ok(k?.GetValue("DisplayVersion") as string == "1.8", "registry: DisplayVersion");
            Ok(k?.GetValue("InstallLocation") as string == Inst.Dir, "registry: InstallLocation");
            Ok((k?.GetValue("UninstallString") as string ?? "").Contains("Uninstall Minibar.exe"), "registry: UninstallString");
            Ok(k?.GetValue("DisplayIcon") as string == Inst.Exe + ",0", "registry: DisplayIcon");
        }
        Ok(File.ReadAllText(Path.Combine(Inst.DataDir, "settings.ini")) == settingsBefore, "settings.ini untouched");
        Ok(File.Exists(Path.Combine(Inst.LocalDataDir, "backup", "Pulp", "keep.txt")), "backups untouched");

        // 2. update over an older exe (+ a self-update leftover in the way)
        File.WriteAllBytes(Inst.Exe, new byte[] { 0x4D, 0x5A, 9, 9 });
        Directory.CreateDirectory(Path.Combine(Inst.Dir, "Minibar.old-setup.exe"));   // something sits on the old-exe name
        res = Inst.Install(false, null, m => { });
        Ok(SameBytes(Inst.Exe, exe), "update: new Minibar.exe in place even with Minibar.old-setup.exe blocked");
        Ok(!Directory.GetFiles(Inst.Dir).Any(f => f.EndsWith(".setup-new")), "update: no temp files");
        Directory.Delete(Path.Combine(Inst.Dir, "Minibar.old-setup.exe"));
        foreach (var f in Directory.GetFiles(Inst.Dir, "Minibar.old-setup-*.exe")) File.Delete(f);

        // 3. failure before anything changes: README can't be written -> old files stay, no leftovers
        File.WriteAllBytes(Inst.Exe, new byte[] { 0x4D, 0x5A, 7 });
        Directory.CreateDirectory(Path.Combine(Inst.Dir, "README.txt.setup-new"));
        bool threw = false;
        try { Inst.Install(false, null, m => { }); } catch { threw = true; }
        Ok(threw, "failure: setup reports the error");
        Ok(File.ReadAllBytes(Inst.Exe).SequenceEqual(new byte[] { 0x4D, 0x5A, 7 }), "failure: old Minibar.exe untouched");
        Ok(!Directory.GetFiles(Inst.Dir).Any(f => f.EndsWith(".setup-new")), "failure: staged files removed");
        Directory.Delete(Path.Combine(Inst.Dir, "README.txt.setup-new"));

        // 4. old copies: a closed copy elsewhere is reported, one inside the install folder isn't
        string old = Path.Combine(root, "Desktop", "Minibar", "Minibar (1).exe");
        Directory.CreateDirectory(Path.GetDirectoryName(old));
        File.WriteAllBytes(old, exe);
        res = Inst.Install(false, new[] { old, Inst.Exe }, m => { });
        Ok(res.OldCopies.Count == 1 && res.OldCopies[0] == old, "old copy reported (" + string.Join(",", res.OldCopies) + ")");
        string notMinibar = Path.Combine(root, "Desktop", "Other.exe"); File.WriteAllBytes(notMinibar, new byte[] { 0x4D, 0x5A });
        res = Inst.Install(false, new[] { notMinibar }, m => { });
        Ok(res.OldCopies.Count == 0, "a non-Minibar exe is never called an old copy");

        // 5. registry dir is trusted only when it's a Minibar folder
        string keep = Inst.Dir;
        using (var k = Registry.CurrentUser.CreateSubKey(Inst.RegKey)) k.SetValue("InstallLocation", root);
        Inst.UseRegisteredDir();
        Ok(Inst.Dir == keep, "registry pointing at a non-Minibar folder is ignored");
        Inst.Register();

        // 6. uninstall keeps settings by default; the running uninstaller is left for the cleanup
        Inst.SelfPath = Inst.Uninstaller;
        var left = Inst.Uninstall(false, m => { });
        Ok(!File.Exists(Inst.Exe) && !File.Exists(Path.Combine(Inst.Dir, "README.txt")), "uninstall: Minibar files removed");
        Ok(File.Exists(Inst.Uninstaller) && left.Count == 0, "uninstall: running uninstaller skipped (cleanup deletes it after)");
        Ok(!Inst.Registered, "uninstall: Installed apps entry removed");
        Ok(File.ReadAllText(Path.Combine(Inst.DataDir, "settings.ini")) == settingsBefore && File.Exists(Path.Combine(Inst.LocalDataDir, "backup", "Pulp", "keep.txt")), "uninstall: settings + backups kept");
        File.Delete(Inst.Uninstaller);

        // 7. reinstall + uninstall with "also delete"
        Inst.SelfPath = fakeSetup;
        Inst.Install(false, null, m => { });
        Inst.SelfPath = Path.Combine(root, "tmp-uninstaller.exe");
        left = Inst.Uninstall(true, m => { });
        Ok(!Directory.Exists(Inst.Dir), "uninstall all: folder gone");
        Ok(!Directory.Exists(Inst.DataDir) && !Directory.Exists(Inst.LocalDataDir), "uninstall all: settings + backups gone");
        Ok(Directory.Exists(Path.Combine(root, "Desktop", "Minibar")), "uninstall: other Minibar copies untouched");

        // 8. a folder not named Minibar is never wiped
        Inst.Dir = Path.Combine(root, "Desktop");
        Inst.Uninstall(false, m => { });
        Ok(File.Exists(old) && File.Exists(notMinibar), "uninstall never empties a folder that isn't Minibar's");

        Ok(Inst.FindRunning() != null, "process scan doesn't throw");
        try { Registry.CurrentUser.DeleteSubKeyTree(Inst.RegKey, false); } catch { }
        try { Directory.Delete(root, true); } catch { }
        Console.WriteLine(checks + " checks, " + fails + " failed");
        return fails == 0 ? 0 : 1;
    }

    // offscreen renders of each screen for a look
    static int Render(string outDir)
    {
        // Mono doesn't draw memory-loaded fonts in controls: preview with the installed Lexend (~/.fonts) instead
        Minibar.AppFonts.UseInstalled("Lexend Medium", "Lexend");
        Directory.CreateDirectory(outDir);
        string root = Path.Combine(Path.GetTempPath(), "mbsetup-render");
        Inst.Dir = Path.Combine(root, "Programs", "Minibar"); Inst.DesktopLink = Path.Combine(root, "d.lnk");
        Inst.StartLink = Path.Combine(root, "s.lnk");
        void Shot(string name, bool un, Action<SetupWindow> prep)
        {
            var w = new SetupWindow(un);
            w.StartPosition = FormStartPosition.Manual; w.Location = new Point(-3000, -3000);
            w.Show(); Application.DoEvents();
            prep?.Invoke(w);
            Application.DoEvents();
            using (var bmp = new Bitmap(w.ClientSize.Width, w.ClientSize.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    var m = typeof(SetupWindow).GetMethod("OnPaint", BindingFlags.NonPublic | BindingFlags.Instance);
                    m.Invoke(w, new object[] { new PaintEventArgs(g, new Rectangle(Point.Empty, bmp.Size)) });
                    foreach (Control c in w.Controls)
                    {
                        if (!c.Visible) continue;
                        using (var cb = new Bitmap(c.Width, c.Height)) { c.DrawToBitmap(cb, new Rectangle(Point.Empty, c.Size)); g.DrawImage(cb, c.Location); }
                    }
                }
                bmp.Save(Path.Combine(outDir, name + ".png"));
            }
            w.Dispose();
        }
        void Set(SetupWindow w, string f, object v) => typeof(SetupWindow).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(w, v);
        void Call(SetupWindow w, string m, params object[] a) => typeof(SetupWindow).GetMethod(m, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, a);
        try { Directory.Delete(root, true); } catch { }
        Shot("1-fresh", false, null);
        Shot("2-running", false, w => Call(w, "SetStatus", "Minibar is open. Setup closes it first and opens it again when it's done.", Minibar.Theme.Sub));
        Shot("3-busy", false, w => { Set(w, "progress", 0.45f); Call(w, "SetStatus", "Copying files…", Minibar.Theme.Sub); });
        Shot("4-stuck", false, w => Call(w, "Finish", null, true, false));
        Shot("5-done", false, w => { Set(w, "result", new Inst.Result { OldCopies = { @"C:\Users\Sdook\Downloads\Minibar.exe" } }); Call(w, "Finish", null, false, false); });
        Shot("6-uninstall", true, w => { });
        Directory.CreateDirectory(Inst.Dir); File.WriteAllBytes(Path.Combine(Inst.Dir, "Minibar.exe"), Res("payload/Minibar.exe"));
        Shot("7-same", false, null);
        Shot("8-uninstall-ready", true, null);
        Shot("9-uninstall-done", true, w => { Set(w, "phase", Enum.Parse(typeof(SetupWindow).GetNestedType("Phase", BindingFlags.NonPublic), "Busy")); Call(w, "Finish", null, false, false); });
        try { Directory.Delete(root, true); } catch { }
        return 0;
    }
}
