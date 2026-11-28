using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Minibar
{
    // One zip with every app's settings (and Minibar's), and putting them back.
    // Settings only: Pulp's clipboard history, logs and downloaded tools are left out (big, and rebuilt by themselves).
    static class SettingsBackup
    {
#pragma warning disable CS0649
        public static Func<string, string, bool> ZipOverride, UnzipOverride;   // test hooks
#pragma warning restore CS0649

        static readonly string[] KeepExt = { ".ini", ".txt", ".json", ".cfg", ".xml", ".csv" };
        static readonly string[] SkipNames = { "crash.log", "status.txt", "sensors.txt", "juice.log", "diagnostics.txt", "snapshot.txt", "left.txt" };   // Refill's snapshot / left-alone list belong to this PC

        public static List<string> SettingsFiles(string dir)
        {
            if (dir == null || !Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir)
                .Where(f => KeepExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !SkipNames.Contains(Path.GetFileName(f).ToLowerInvariant()))
                .Where(f => { try { return new FileInfo(f).Length < 4 * 1024 * 1024; } catch { return false; } })
                .ToList();
        }

        // returns how many apps were saved
        public static int Create(IEnumerable<AppInfo> apps, string zipPath)
        {
            string stage = Path.Combine(Updater.Root, "backup-stage-" + DateTime.Now.Ticks);
            try
            {
                Directory.CreateDirectory(stage);
                var manifest = new StringBuilder();
                manifest.AppendLine("Minibar settings backup");
                manifest.AppendLine("made=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                manifest.AppendLine("pc=" + Environment.MachineName);
                int n = 0;
                foreach (var a in apps)
                {
                    var files = SettingsFiles(a.DataDir);
                    if (files.Count == 0) continue;
                    string to = Path.Combine(stage, a.Def.Name);
                    Directory.CreateDirectory(to);
                    foreach (var f in files) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
                    manifest.AppendLine("app=" + a.Def.Name + "|" + (a.Version ?? "") + "|" + string.Join(",", files.Select(Path.GetFileName)));
                    n++;
                }
                File.WriteAllText(Path.Combine(stage, "minibar-backup.txt"), manifest.ToString(), Encoding.UTF8);
                if (File.Exists(zipPath)) File.Delete(zipPath);
                if (!Zip(stage, zipPath)) throw new Exception("Couldn't make the zip file.");
                return n;
            }
            finally { try { Directory.Delete(stage, true); } catch { } }
        }

        static bool Zip(string dir, string zip)
        {
            if (ZipOverride != null) return ZipOverride(dir, zip);
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            if (File.Exists(tar) && Run(tar, "-a -c -f \"" + zip + "\" -C \"" + dir + "\" .") == 0 && File.Exists(zip)) return true;
            string ps = "Compress-Archive -Path '" + dir.Replace("'", "''") + "\\*' -DestinationPath '" + zip.Replace("'", "''") + "' -Force";
            return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + ps + "\"") == 0 && File.Exists(zip);
        }

        static bool Unzip(string zip, string dir)
        {
            if (UnzipOverride != null) return UnzipOverride(zip, dir);
            Directory.CreateDirectory(dir);
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            if (File.Exists(tar) && Run(tar, "-xf \"" + zip + "\" -C \"" + dir + "\"") == 0) return true;
            string ps = "Expand-Archive -LiteralPath '" + zip.Replace("'", "''") + "' -DestinationPath '" + dir.Replace("'", "''") + "' -Force";
            return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + ps + "\"") == 0;
        }

        static int Run(string exe, string args)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    p.OutputDataReceived += (o, e) => { }; p.ErrorDataReceived += (o, e) => { };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } return -1; }
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        // Root = the folder Open unpacked into (delete exactly that afterwards); Dir = where minibar-backup.txt is inside it
        public class Contents { public string Root, Dir, Made; public List<string> Apps = new List<string>(); }

        public static void Discard(Contents c)
        {
            try
            {
                if (c?.Root == null) return;
                // only ever a restore-<ticks> folder this Minibar made
                if (!Path.GetFileName(c.Root).StartsWith("restore-", StringComparison.Ordinal)) return;
                if (!Apps.SameFile(Path.GetDirectoryName(c.Root), Updater.Root)) return;
                if (Directory.Exists(c.Root)) Directory.Delete(c.Root, true);
            }
            catch { }
        }

        // unpacks and reads what's in a backup (caller deletes Dir afterwards)
        public static Contents Open(string zipPath)
        {
            string dir = Path.Combine(Updater.Root, "restore-" + DateTime.Now.Ticks);
            if (!Unzip(zipPath, dir)) throw new Exception("Couldn't open that zip.");
            string m = Directory.GetFiles(dir, "minibar-backup.txt", SearchOption.AllDirectories).FirstOrDefault();
            if (m == null) { try { Directory.Delete(dir, true); } catch { } throw new Exception("That zip isn't a Minibar settings backup."); }
            var c = new Contents { Root = dir, Dir = Path.GetDirectoryName(m) };
            foreach (var l in File.ReadAllLines(m))
            {
                if (l.StartsWith("made=")) c.Made = l.Substring(5);
                else if (l.StartsWith("app=")) c.Apps.Add(l.Substring(4).Split('|')[0]);
            }
            return c;
        }

        // puts the settings back: each app is closed while its files change and started again if it was running.
        // The settings being replaced are kept in %LOCALAPPDATA%\Minibar\settings-before-restore\<time>.
        public static List<string> Restore(Contents c, IEnumerable<AppInfo> apps, Action<string> progress)
        {
            var done = new List<string>();
            string keep = Path.Combine(Updater.Root, "settings-before-restore", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            foreach (var a in apps)
            {
                string from = Path.Combine(c.Dir, a.Def.Name);
                if (!c.Apps.Contains(a.Def.Name) || !Directory.Exists(from) || a.Def.Self) continue;
                string to = a.DataDir;
                var pids = Apps.PidsNow(a);
                bool wasRunning = pids.Count > 0 && a.Found;
                string args = a.Found ? Apps.RestartArgs(a) : null;
                if (wasRunning) { progress("Closing " + a.Def.Name + "…"); a.Pids = pids; Apps.Stop(a); }
                try
                {
                    progress("Restoring " + a.Def.Name + "…");
                    Directory.CreateDirectory(to);
                    foreach (var f in SettingsFiles(to))
                    {
                        Directory.CreateDirectory(Path.Combine(keep, a.Def.Name));
                        File.Copy(f, Path.Combine(keep, a.Def.Name, Path.GetFileName(f)), true);
                    }
                    foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
                    done.Add(a.Def.Name);
                }
                finally
                {
                    if (wasRunning) { progress("Starting " + a.Def.Name + "…"); try { Apps.Start(a, args); } catch (Exception ex) { Log.Crash(ex); } }
                }
            }
            return done;
        }
    }

    // a small dark yes/no box (no white native MessageBox)
    class DarkConfirm : Form
    {
        public DarkConfirm(string title, string text, string yes, float s)
        {
            int S(float v) => (int)Math.Round(v * s);
            Text = title;
            Icon = Theme.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ClientSize = new Size(S(460), S(200));
            var f = Theme.Ui(10f);
            var t = new Label { Text = title, Font = Theme.UiTitle(13f), ForeColor = Theme.Text, Location = new Point(S(24), S(20)), AutoSize = true, BackColor = Theme.Bg };
            var l = new Label { Text = text, Font = f, ForeColor = Theme.Sub, Location = new Point(S(24), S(58)), Size = new Size(S(412), S(80)), BackColor = Theme.Bg, UseMnemonic = false };
            var ok = new FlatBtn(yes, s, true) { Location = new Point(S(460 - 24 - 130), S(148)), Width = S(130) };
            var no = new FlatBtn("Cancel", s) { Location = new Point(S(460 - 24 - 130 - 10 - 110), S(148)), Width = S(110) };
            ok.Click += (o, e) => { DialogResult = DialogResult.OK; Close(); };
            no.Click += (o, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.AddRange(new Control[] { t, l, ok, no });
            KeyPreview = true;
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(Handle, Theme.Bg);
        }
    }
}
