using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Minibar
{
    // ---------------------------------------------------------------- what Minibar knows about each app
    class AppDef
    {
        public string Name, Blurb;
        public string RunName;          // value name under HKCU\...\Run (null = starts through a scheduled task)
        public string RunArgs = "";     // args the app itself writes into its Run value
        public string StartArgs = "";   // args when Minibar starts it (quietly, into the tray)
        public string TaskName;         // Fizz: logon task instead of the Run key
        public string LogName = "crash.log";
        public Func<string, string> DataDir;   // exe path -> data folder
        public string[] Repos;          // github.com/iiiPingo/<repo>, tried in order (Echoa: "echoa", then its old name "sip")
        public bool Self;               // Minibar itself (Updates page only)
        public Version ExitSince;        // from this version on, "<App>.exe --exit" asks the running app to close cleanly
        public string Highlights;       // the line under the name: what the app is best at
        public string InfoTitle, Info;   // the (i) note on the card (Juice: which devices work)
    }

    static class Catalog
    {
        static string Roam(string n) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), n);

        public static readonly AppDef[] All =
        {
            new AppDef { Name = "Echoa", Blurb = "Volume mixer", RunName = "Echoa", DataDir = e => Roam("Echoa"), Highlights = "One click sets the default output  \u00b7  remembers each app's volume  \u00b7  stops the SteelSeries taking over", Repos = new[] { "echoa", "sip" }, ExitSince = new Version(1, 6) },
            new AppDef
            {
                Name = "Juice", Blurb = "Battery levels", RunName = "Juice", RunArgs = "--autostart", LogName = "juice.log",
                // Juice keeps its files next to the exe when it can write there, otherwise in %APPDATA%\Juice
                DataDir = e =>
                {
                    string d = e == null ? null : Path.GetDirectoryName(e);
                    return d != null && (File.Exists(Path.Combine(d, "settings.ini")) || File.Exists(Path.Combine(d, "juice.log"))) ? d : Roam("Juice");
                },
                Highlights = "Battery rings in the taskbar  \u00b7  low-battery alerts held during games  \u00b7  Bluetooth, AirPods, Logitech",
                Repos = new[] { "juice" }, ExitSince = new Version(1, 1),
                InfoTitle = "Which devices Juice can read",
                Info = "\u2022  Any Bluetooth device that shows a battery in Windows Settings: headphones, earbuds, mice, keyboards, controllers\n" +
                       "\u2022  AirPods and Beats (left, right and case)\n" +
                       "\u2022  Logitech mice and keyboards on a Unifying, Bolt or Lightspeed receiver (new in Juice 1.1)\n" +
                       "\u2022  SteelSeries Arctis Nova 5 / 5P / 5X\n" +
                       "\u2022  Attack Shark, Ajazz, ATK, VXE and similar 2.4 GHz mouse receivers\n" +
                       "\u2022  Xbox (over Bluetooth) and GameSir controllers\n\n" +
                       "Not yet: Razer, Corsair, HyperX and other brands' own USB receivers. Right-click Juice > Diagnostics shows everything it can see."
            },
            new AppDef { Name = "Pour", Blurb = "Video downloader", RunName = "Pour", RunArgs = "--tray", StartArgs = "--tray", DataDir = e => Roam("Pour"), Highlights = "Copy a link, get MP4 or MP3 up to 2K  \u00b7  whole playlists  \u00b7  keeps yt-dlp up to date", Repos = new[] { "pour" }, ExitSince = new Version(1, 2) },
            new AppDef { Name = "Pulp", Blurb = "Clipboard history", RunName = "Pulp", RunArgs = "--tray", StartArgs = "--tray", DataDir = e => Roam("Pulp"), Highlights = "Ctrl+Alt+V history with search and pins  \u00b7  strips tracking from links  \u00b7  screenshots too", Repos = new[] { "pulp" }, ExitSince = new Version(1, 3) },
            // Fizz last: its card can carry the PawnIO / anti-cheat notice
            new AppDef { Name = "Fizz", Blurb = "In-game overlay", TaskName = "Fizz", DataDir = e => Roam("Fizz"), Highlights = "FPS + 1% low, temps, load, RAM and VRAM  \u00b7  click-through  \u00b7  nothing injected into games", Repos = new[] { "fizz" }, ExitSince = new Version(1, 1) },
        };

        public static readonly AppDef Minibar = new AppDef { Name = "Minibar", Blurb = "This app", Self = true, DataDir = e => Settings.Dir, Repos = new[] { "minibar" } };
    }

    // ---------------------------------------------------------------- live state of one app
    class AppInfo
    {
        public readonly AppDef Def;
        public AppInfo(AppDef d) { Def = d; }

        public string Exe;               // null = not found
        public string Source;            // where the path came from: "located", "running", "startup", "found"
        public string Version;           // null = unknown
        public Bitmap Icon;
        public int IconFor;              // pixel size the icon was made for
        public string IconPath;

        public bool Running;
        public List<int> Pids = new List<int>();
        public long MemBytes;
        public DateTime? Since;          // when the oldest running process started (uptime in the pill)
        public string OtherCopy;         // running, but from a different exe than the one shown

        public bool? Startup;            // null = can't tell / not set up
        public string StartupNote;       // shown under the toggle when it can't be changed here
        public string StartupCmd;        // current Run value (or null)

        public string Busy;              // "Starting…" etc.
        public string Error; public DateTime ErrorUntil;

        // Fizz only
        public bool? PawnIO;
        public string PawnSetup;         // tools\PawnIO_setup*.exe next to Fizz.exe, if there

        public bool RecentCrash;         // its crash.log changed in the last 7 days

        // updates
        public Release Rel;              // latest release on GitHub (null = not checked yet)
        public bool Updating;
        public bool Queued;              // update waits until he closes the app himself
        bool oldCleaned;
        // leftovers from swapping a running exe (Pour.old-123.exe): gone once the app is closed
        public void CleanOld()
        {
            if (Running) { oldCleaned = false; return; }
            if (oldCleaned || Exe == null || Def.Self) return;
            oldCleaned = true;
            try
            {
                foreach (var f in Directory.GetFiles(Path.GetDirectoryName(Exe), Def.Name + ".old*.exe"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }
        public string Backup;            // newest backup folder Minibar can restore (null = none)
        public string Note; public DateTime NoteUntil;   // good news for a few seconds ("Updated to v1.3")

        public bool Found => Exe != null;
        public string DataDir => Def.DataDir(Exe);
        public string LogPath => Path.Combine(DataDir, Def.LogName);
    }

    static class Apps
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        public static string ProjectsRoot => Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\", "Claude Projects");
        public static string Downloads => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        // ------------------------------------------------------------ finding the exe
        public static string ExeFromCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\""))
            {
                int q = cmd.IndexOf('"', 1);
                return q > 1 ? cmd.Substring(1, q - 1) : null;
            }
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return i > 0 ? cmd.Substring(0, i + 4) : null;
        }

        public static string ArgsFromCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return "";
            cmd = cmd.Trim();
            if (cmd.StartsWith("\"")) { int q = cmd.IndexOf('"', 1); return q > 0 ? cmd.Substring(q + 1).Trim() : ""; }
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return i > 0 ? cmd.Substring(i + 4).Trim() : "";
        }

        public static string RunValue(string name)
        {
            if (name == null) return null;
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue(name) as string; }
            catch { return null; }
        }

        static bool DisabledInTaskManager(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                    return k?.GetValue(name) is byte[] b && b.Length > 0 && (b[0] & 1) != 0;
            }
            catch { return false; }
        }

        public static bool SameFile(string a, string b) =>
            a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        public static Version FileVer(string path)
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(path);
                return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
            }
            catch { return new Version(0, 0); }
        }

        // <App>.exe under C:\Claude Projects (a few levels deep, backups and .old.exe skipped) and Downloads\<App>
        // where installed apps go: beside Minibar's own folder when it has one (Downloads\Minibar -> Downloads\Pulp),
        // otherwise beside the exe itself (Minibar.exe straight in Downloads -> Downloads\Pulp)
        public static string AppsHome(string selfExe)
        {
            string mine = Path.GetDirectoryName(selfExe ?? "") ?? "";
            if (Path.GetFileName(mine).Equals("Minibar", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(mine) ?? mine;
            return mine;
        }

        static IEnumerable<string> Candidates(AppDef d)
        {
            var found = new List<string>();
            void Walk(string dir, int depth)
            {
                if (depth < 0) return;
                try
                {
                    string f = Path.Combine(dir, d.Name + ".exe");
                    if (File.Exists(f)) found.Add(f);
                    foreach (var sub in Directory.GetDirectories(dir))
                    {
                        string n = Path.GetFileName(sub);
                        if (n.Equals("Backups", StringComparison.OrdinalIgnoreCase) || n.Equals("backup", StringComparison.OrdinalIgnoreCase)
                            || n.Equals("source", StringComparison.OrdinalIgnoreCase) || n.StartsWith(".")) continue;
                        Walk(sub, depth - 1);
                    }
                }
                catch { }
            }
            try
            {
                if (Directory.Exists(ProjectsRoot))
                    foreach (var sub in Directory.GetDirectories(ProjectsRoot))
                        if (Path.GetFileName(sub).StartsWith(d.Name, StringComparison.OrdinalIgnoreCase)
                            || (d.Name == "Echoa" && Path.GetFileName(sub).StartsWith("Echoa", StringComparison.OrdinalIgnoreCase)))
                            Walk(sub, 2);
            }
            catch { }
            string dl = Path.Combine(Downloads, d.Name);
            if (Directory.Exists(dl)) Walk(dl, 2);
            // where Minibar's Install puts apps: next to Minibar's folder, or %LOCALAPPDATA%\Programs\<App>
            try
            {
                string side = Path.Combine(AppsHome(System.Windows.Forms.Application.ExecutablePath), d.Name);
                if (Directory.Exists(side) && !side.Equals(dl, StringComparison.OrdinalIgnoreCase)) Walk(side, 1);
            }
            catch { }
            string progs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", d.Name);
            if (Directory.Exists(progs)) Walk(progs, 1);
            return found;
        }

        public static void Locate(AppInfo a, Settings s, string runningPath)
        {
            string exe = null, src = null;
            if (s.Located.TryGetValue(a.Def.Name, out var loc) && File.Exists(loc)) { exe = loc; src = "located"; }
            else if (runningPath != null && File.Exists(runningPath)) { exe = runningPath; src = "running"; }
            else
            {
                string run = ExeFromCommand(RunValue(a.Def.RunName));
                if (run == null && s.RunCmd.TryGetValue(a.Def.Name, out var kept)) run = ExeFromCommand(kept);
                if (run != null && File.Exists(run) && Path.GetFileName(run).Equals(a.Def.Name + ".exe", StringComparison.OrdinalIgnoreCase)) { exe = run; src = "startup"; }
                else
                {
                    exe = Candidates(a.Def).OrderByDescending(FileVer).ThenByDescending(f => { try { return File.GetLastWriteTimeUtc(f); } catch { return DateTime.MinValue; } }).FirstOrDefault();
                    src = exe != null ? "found" : null;
                }
            }
            bool changed = !SameFile(exe, a.Exe) || (exe == null) != (a.Exe == null);
            a.Exe = exe;
            a.Source = src;
            if (changed || a.Version == null) a.Version = exe != null ? VersionText(exe) : null;
            if (a.Def.Name == "Fizz") CheckFizz(a);
        }

        public static string VersionText(string path)
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(path);
                if (v.FileMajorPart == 0 && v.FileMinorPart == 0 && v.FileBuildPart == 0) return null;
                if (v.FileBuildPart == 0 && v.FilePrivatePart == 0) return v.FileMajorPart + "." + v.FileMinorPart;
                if (v.FilePrivatePart == 0) return v.FileMajorPart + "." + v.FileMinorPart + "." + v.FileBuildPart;
                return v.FileMajorPart + "." + v.FileMinorPart + "." + v.FileBuildPart + "." + v.FilePrivatePart;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------ running or not (one process snapshot for all apps)
        public static void Scan(IList<AppInfo> apps, Settings s)
        {
            var byName = new Dictionary<string, List<Tuple<int, long>>>(StringComparer.OrdinalIgnoreCase);
            var started = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in apps) byName[a.Def.Name] = new List<Tuple<int, long>>();
            Process[] all = new Process[0];
            try { all = Process.GetProcesses(); } catch { }
            foreach (var p in all)
            {
                try
                {
                    if (byName.TryGetValue(p.ProcessName, out var l))
                    {
                        long mem = 0;
                        try { mem = p.WorkingSet64; } catch { }
                        l.Add(Tuple.Create(p.Id, mem));
                        try { var st = p.StartTime; if (!started.TryGetValue(p.ProcessName, out var cur) || st < cur) started[p.ProcessName] = st; } catch { }   // admin processes: unknown
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            foreach (var a in apps)
            {
                var l = byName[a.Def.Name];
                string path = null;
                foreach (var t in l) { path = Native.ProcessPath(t.Item1); if (path != null) break; }
                bool wasRunning = a.Running;
                a.Running = l.Count > 0;
                a.Pids = l.Select(t => t.Item1).ToList();
                a.MemBytes = l.Sum(t => t.Item2);
                a.Since = a.Running && started.TryGetValue(a.Def.Name, out var since) ? since : (DateTime?)null;
                // the exe was moved or deleted (an old copy cleaned up): find it again
                if (a.Exe != null && !File.Exists(a.Exe)) { a.Icon?.Dispose(); a.Icon = null; a.Version = null; Locate(a, s, path); }
                // follow the running copy unless he picked a path himself
                if (a.Running && path != null && !SameFile(path, a.Exe) && !(a.Source == "located"))
                    Locate(a, s, path);
                a.OtherCopy = a.Running && path != null && a.Exe != null && !SameFile(path, a.Exe) ? path : null;
                if (a.Running != wasRunning && a.Def.Name == "Fizz") CheckFizz(a);
                a.CleanOld();
                a.RecentCrash = false;
                if (a.Def.LogName == "crash.log")
                    try
                    {
                        var fi = new FileInfo(a.LogPath);
                        a.RecentCrash = fi.Exists && fi.Length > 0 && fi.LastWriteTime > DateTime.Now.AddDays(-7);
                    }
                    catch { }
            }
        }

        // ------------------------------------------------------------ start / stop
        public static void Start(AppInfo a, string args = null)
        {
            if (a.Exe == null) throw new InvalidOperationException("Minibar can't find " + a.Def.Name + ".exe.");
            if (a.Def.TaskName != null && TaskQuery(a.Def.TaskName, out _) == 0)
            {
                // Fizz: its logon task runs it as admin without a UAC prompt
                if (Run("schtasks.exe", "/Run /TN \"" + a.Def.TaskName + "\"", out string o) == 0) return;
            }
            var psi = new ProcessStartInfo(a.Exe, args ?? a.Def.StartArgs)
            {
                UseShellExecute = true,                    // lets apps that need admin (Fizz) show their UAC prompt
                WorkingDirectory = Path.GetDirectoryName(a.Exe)
            };
            try { Process.Start(psi)?.Dispose(); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException("Cancelled at the admin prompt."); }
        }

        // the processes of this app right now (by name; any copy)
        public static List<int> PidsNow(AppDef d)
        {
            var list = new List<int>();
            try { foreach (var p in Process.GetProcessesByName(d.Name)) { list.Add(p.Id); p.Dispose(); } } catch { }
            return list;
        }

        // the args the app normally starts with: its own start-with-Windows command if it has one, else Minibar's quiet start
        public static string RestartArgs(AppInfo a)
        {
            string cmd = RunValue(a.Def.RunName);
            return cmd != null && SameFile(ExeFromCommand(cmd), a.Exe) ? ArgsFromCommand(cmd) : a.Def.StartArgs;
        }

        // force-closes the app and anything it started (Pour's yt-dlp, Fizz's PresentMon), then clears its tray icon
        public static void Stop(AppInfo a)
        {
            var pids = a.Pids.ToList();
            if (pids.Count == 0) return;
            // newer apps close themselves cleanly when asked (tray icon removed, everything saved)
            if (a.Def.ExitSince != null && a.Exe != null && FileVer(a.Exe) >= a.Def.ExitSince)
            {
                try
                {
                    using (var p = Process.Start(new ProcessStartInfo(a.Exe, "--exit") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(a.Exe) }))
                        p?.WaitForExit(3000);
                    for (int i = 0; i < 40 && StillRunning(pids); i++) System.Threading.Thread.Sleep(100);
                    if (!StillRunning(pids)) return;
                }
                catch (Exception ex) { Log.Crash(ex); }
            }
            var wins = Native.WindowsOf(new HashSet<int>(pids));
            string args = "/F /T " + string.Join(" ", pids.Select(p => "/PID " + p));
            int rc = Run("taskkill.exe", args, out string output);
            // an app running as admin (Fizz) can only be closed from an admin prompt
            if (rc != 0 && StillRunning(pids))
                RunElevated("taskkill.exe", "/F /T /IM \"" + a.Def.Name + ".exe\"");
            for (int i = 0; i < 30 && StillRunning(pids); i++) System.Threading.Thread.Sleep(100);
            Native.RemoveTrayIcons(wins);
            if (StillRunning(pids)) throw new Exception(a.Def.Name + " didn't close.");
        }

        static bool StillRunning(List<int> pids)
        {
            foreach (int p in pids)
            {
                try { using (var pr = Process.GetProcessById(p)) if (!pr.HasExited) return true; }
                catch (ArgumentException) { }          // gone
                catch { return true; }                 // exists but we can't look at it (admin)
            }
            return false;
        }

        static int Run(string exe, string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return -1; }
                    output += err.Result;
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { output = ex.Message; return -1; }
        }

        // one UAC prompt; throws OperationCanceledException if he says no
        static int RunElevated(string exe, string args)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true }))
                {
                    if (p == null) return -1;
                    p.WaitForExit(20000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException("Cancelled at the admin prompt."); }
        }

        // ------------------------------------------------------------ start with Windows
        public static void ReadStartup(AppInfo a, Settings s, bool queryTask)
        {
            if (a.Def.RunName != null)
            {
                a.StartupCmd = RunValue(a.Def.RunName);
                string runExe = ExeFromCommand(a.StartupCmd);
                // an entry that points at a deleted copy doesn't start anything: show it as off (turning it on rewrites it)
                bool dead = runExe != null && !File.Exists(runExe);
                a.Startup = a.StartupCmd != null && !dead && !DisabledInTaskManager(a.Def.RunName);
                a.StartupNote = null;
                return;
            }
            if (!queryTask) return;
            int rc = TaskQuery(a.Def.TaskName, out bool enabled);
            if (rc != 0) { a.Startup = null; a.StartupNote = "Set up from Fizz itself the first time (it needs admin once)."; }
            else { a.Startup = enabled; a.StartupNote = null; }
        }

        static int TaskQuery(string name, out bool enabled)
        {
            enabled = false;
            int rc = Run("schtasks.exe", "/Query /TN \"" + name + "\" /FO CSV /NH", out string o);
            if (rc == 0) enabled = o.IndexOf("\"Disabled\"", StringComparison.OrdinalIgnoreCase) < 0;
            return rc;
        }

        public static void SetStartup(AppInfo a, Settings s, bool on)
        {
            if (a.Def.TaskName != null)
            {
                if (TaskQuery(a.Def.TaskName, out _) != 0) throw new Exception("Open Fizz once and turn on \"Start with Windows\" there first.");
                RunElevated("schtasks.exe", "/Change /TN \"" + a.Def.TaskName + "\" " + (on ? "/ENABLE" : "/DISABLE"));
                return;
            }
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on)
                {
                    if (a.Exe == null) throw new Exception("Minibar can't find " + a.Def.Name + ".exe.");
                    // restore the app's own command if it was for this exe, otherwise write it the way the app does
                    string cmd = s.RunCmd.TryGetValue(a.Def.Name, out var kept) && SameFile(ExeFromCommand(kept), a.Exe) ? kept
                        : ("\"" + a.Exe + "\" " + a.Def.RunArgs).Trim();
                    string cur = k.GetValue(a.Def.RunName) as string;
                    if (cur == null || !SameFile(ExeFromCommand(cur), a.Exe)) k.SetValue(a.Def.RunName, cmd);
                    s.RunCmd.Remove(a.Def.Name);
                    try
                    {
                        using (var ap = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                            if (ap?.GetValue(a.Def.RunName) is byte[] b && b.Length > 0 && (b[0] & 1) != 0) ap.DeleteValue(a.Def.RunName, false);
                    }
                    catch { }
                }
                else
                {
                    if (k.GetValue(a.Def.RunName) is string cur) s.RunCmd[a.Def.Name] = cur;
                    k.DeleteValue(a.Def.RunName, false);
                }
            }
            s.Save();
        }

        // ------------------------------------------------------------ Fizz: what it needs before it works
        public static void CheckFizz(AppInfo a)
        {
            bool installed = false;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var k = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
                        if (k != null && k.GetValue("DisplayVersion") != null) installed = true;
                }
                catch { }
            }
            a.PawnIO = installed;
            a.PawnSetup = null;
            try
            {
                string tools = a.Exe == null ? null : Path.Combine(Path.GetDirectoryName(a.Exe), "tools");
                if (tools != null && Directory.Exists(tools))
                    a.PawnSetup = Directory.GetFiles(tools, "PawnIO_setup*.exe").OrderByDescending(f => f).FirstOrDefault();
            }
            catch { }
        }

        public static void InstallPawnIO(AppInfo a)
        {
            if (a.PawnSetup == null) throw new Exception("PawnIO's installer isn't in Fizz's tools folder.");
            try { Process.Start(new ProcessStartInfo(a.PawnSetup) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(a.PawnSetup) })?.Dispose(); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException("Cancelled at the admin prompt."); }
        }

        // ------------------------------------------------------------ explorer helpers
        public static void OpenFolder(string dir)
        {
            if (!Directory.Exists(dir)) throw new Exception("That folder doesn't exist yet.");
            Process.Start("explorer.exe", "\"" + dir + "\"")?.Dispose();
        }

        public static void ShowFile(string file)
        {
            if (!File.Exists(file)) throw new Exception("No file there yet.");
            Process.Start("explorer.exe", "/select,\"" + file + "\"")?.Dispose();
        }

        public static void OpenText(string file)
        {
            if (!File.Exists(file)) throw new Exception("No log yet. That's a good sign.");
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file + "\"") { UseShellExecute = true })?.Dispose();
        }
    }
}
