using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace MinibarSetup
{
    // Everything the setup does to the PC. Per-user only: no admin, nothing outside the user's own folders and HKCU.
    //   %LOCALAPPDATA%\Programs\Minibar\   Minibar.exe, Minibar.exe.config, README.txt, Uninstall Minibar.exe
    //   Start menu + optional desktop shortcut "Minibar.lnk"
    //   HKCU\...\Uninstall\Minibar          so it shows in Installed apps (and Revo)
    // Settings (%APPDATA%\Minibar) and backups (%LOCALAPPDATA%\Minibar) are Minibar's own and are never touched,
    // except by an uninstall where the user ticked "also delete".
    static class Inst
    {
        public const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Minibar";
        public const string UninstallerName = "Uninstall Minibar.exe";
        public const string ExeName = "Minibar.exe";

        static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static string DefaultDir => Path.Combine(Local, "Programs", "Minibar");

        // overridable for the workspace tests
        public static string Dir = DefaultDir;
        public static string StartLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Minibar.lnk");
        public static string DesktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Minibar.lnk");
        public static string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minibar");
        public static string LocalDataDir = Path.Combine(Local, "Minibar");
        public static string SelfPath = System.Windows.Forms.Application.ExecutablePath;

        public static string Exe => Path.Combine(Dir, ExeName);
        public static string Uninstaller => Path.Combine(Dir, UninstallerName);

        // ------------------------------------------------------------ versions
        public static string PayloadVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Build > 0 ? v.Major + "." + v.Minor + "." + v.Build : v.Major + "." + v.Minor;
            }
        }

        public static string VersionText(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var v = FileVersionInfo.GetVersionInfo(path);
                if (v.FileMajorPart == 0 && v.FileMinorPart == 0 && v.FileBuildPart == 0) return null;
                return v.FileBuildPart > 0 ? v.FileMajorPart + "." + v.FileMinorPart + "." + v.FileBuildPart : v.FileMajorPart + "." + v.FileMinorPart;
            }
            catch { return null; }
        }

        public static Version Parse(string s) { try { return s == null ? null : new Version(s.Split('.').Length == 1 ? s + ".0" : s); } catch { return null; } }

        public static string InstalledVersion => File.Exists(Exe) ? (VersionText(Exe) ?? "?") : null;

        // where an earlier setup put it (the folder is fixed, but trust the registry only if it really is a Minibar folder)
        public static void UseRegisteredDir()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RegKey))
                {
                    string loc = (k?.GetValue("InstallLocation") as string ?? "").Trim().TrimEnd('\\');
                    if (loc.Length > 3 && Path.GetFileName(loc).Equals("Minibar", StringComparison.OrdinalIgnoreCase)
                        && (File.Exists(Path.Combine(loc, ExeName)) || File.Exists(Path.Combine(loc, UninstallerName))))
                        Dir = loc;
                }
            }
            catch { }
        }

        public static bool InDir(string path)
        {
            try
            {
                string d = Path.GetFullPath(Dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return path != null && Path.GetFullPath(path).StartsWith(d, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static bool Same(string a, string b)
        {
            try { return a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // ------------------------------------------------------------ Minibar running?
        public class Running { public int Pid; public string Path; }

        // any Minibar copy, even a renamed download like "Minibar (1).exe" (told apart by its ProductName)
        public static List<Running> FindRunning()
        {
            var list = new List<Running>();
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me || !p.ProcessName.StartsWith("Minibar", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.ProcessName.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0 || p.ProcessName.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string path = Win.ProcessPath(p.Id);
                    if (path == null) continue;
                    string product = null;
                    try { product = FileVersionInfo.GetVersionInfo(path).ProductName; } catch { }
                    if (!string.Equals(product, "Minibar", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new Running { Pid = p.Id, Path = path });
                }
                catch { }
                finally { p.Dispose(); }
            }
            return list;
        }

        // the same name Minibar holds while it installs / updates / rolls back an app (Minibar.UpdateLock)
        public const string UpdateLockName = "Minibar.Updating.v1";

        // true when no Minibar is in the middle of an update (anymore); waits up to ms for it to finish
        public static bool WaitForUpdates(int ms)
        {
            Mutex m;
            try { m = Mutex.OpenExisting(UpdateLockName); }
            catch { return true; }   // nobody holds it: nothing running
            using (m)
            {
                try
                {
                    if (!m.WaitOne(ms)) return false;
                    m.ReleaseMutex();
                    return true;
                }
                catch (AbandonedMutexException) { try { m.ReleaseMutex(); } catch { } return true; }
                catch { return true; }
            }
        }

        public static bool UpdateRunning()
        {
            try { using (var m = Mutex.OpenExisting(UpdateLockName)) { } }
            catch { return false; }
            return !WaitForUpdates(0);
        }

        // asks each Minibar to close like its X button does. Minibar finishes an update it's installing first,
        // so while one is running this waits for it (up to updateWaitMs) instead of giving up after waitMs.
        // Returns the ones still open after the wait.
        public static List<Running> Close(List<Running> running, int waitMs, int updateWaitMs = 180000, Action<string> progress = null)
        {
            foreach (var r in running)
                foreach (var h in Win.WindowsOf(r.Pid))
                    if (Win.TitleOf(h) == "Minibar") Win.PostMessage(h, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            var left = new List<Running>();
            var until = DateTime.UtcNow.AddMilliseconds(waitMs);
            foreach (var r in running)
            {
                try
                {
                    using (var p = Process.GetProcessById(r.Pid))
                    {
                        int ms = (int)Math.Max(0, (until - DateTime.UtcNow).TotalMilliseconds);
                        if (!p.WaitForExit(ms)) left.Add(r);
                    }
                }
                catch { }   // already gone
            }
            if (left.Count > 0 && UpdateRunning())
            {
                progress?.Invoke("Minibar is finishing an update…");
                WaitForUpdates(updateWaitMs);
                var still = new List<Running>();
                foreach (var r in left)
                    try { using (var p = Process.GetProcessById(r.Pid)) if (!p.WaitForExit(10000)) still.Add(r); }
                    catch { }
                left = still;
            }
            return left;
        }

        public static void Kill(List<Running> running)
        {
            foreach (var r in running)
                try { using (var p = Process.GetProcessById(r.Pid)) { p.Kill(); p.WaitForExit(5000); } } catch { }
        }

        // ------------------------------------------------------------ the files inside the setup
        static List<Tuple<string, byte[]>> Payload()
        {
            var asm = Assembly.GetExecutingAssembly();
            var list = new List<Tuple<string, byte[]>>();
            foreach (var n in asm.GetManifestResourceNames().Where(n => n.StartsWith("payload/")))
                using (var st = asm.GetManifestResourceStream(n))
                using (var ms = new MemoryStream())
                {
                    st.CopyTo(ms);
                    list.Add(Tuple.Create(n.Substring(8), ms.ToArray()));
                }
            if (!list.Any(t => t.Item1.Equals(ExeName, StringComparison.OrdinalIgnoreCase))) throw new Exception("This setup file is damaged (Minibar.exe isn't inside). Download it again.");
            return list;
        }

        // ------------------------------------------------------------ install
        public class Result { public List<string> OldCopies = new List<string>(); public string Warning; }

        public static Result Install(bool desktop, IEnumerable<string> closedCopies, Action<string> progress)
        {
            var res = new Result();
            progress("Copying files…");
            Directory.CreateDirectory(Dir);
            var files = Payload();

            // 1) write everything next to its final name first, so a full disk or a blocked folder changes nothing
            var staged = new List<Tuple<string, string>>();   // (temp, final)
            try
            {
                foreach (var f in files)
                {
                    string final = Path.Combine(Dir, f.Item1), tmp = final + ".setup-new";
                    File.WriteAllBytes(tmp, f.Item2);
                    staged.Add(Tuple.Create(tmp, final));
                }
                if (!Same(SelfPath, Uninstaller))
                {
                    string tmp = Uninstaller + ".setup-new";
                    File.Copy(SelfPath, tmp, true);
                    staged.Add(Tuple.Create(tmp, Uninstaller));
                }
            }
            catch
            {
                foreach (var s in staged) try { File.Delete(s.Item1); } catch { }
                throw;
            }

            // 2) swap them in. Minibar.exe goes through a rename, so even a copy that's somehow still running can't block it;
            //    Minibar deletes Minibar.old*.exe itself when it starts.
            string oldExe = Path.Combine(Dir, "Minibar.old-setup.exe");
            foreach (var s in staged.OrderBy(t => Path.GetFileName(t.Item2).Equals(ExeName, StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            {
                bool isExe = Path.GetFileName(s.Item2).Equals(ExeName, StringComparison.OrdinalIgnoreCase);
                try
                {
                    if (isExe && File.Exists(s.Item2))
                    {
                        try { if (File.Exists(oldExe)) File.Delete(oldExe); } catch { }
                        if (File.Exists(oldExe) || Directory.Exists(oldExe)) oldExe = Path.Combine(Dir, "Minibar.old-setup-" + DateTime.Now.Ticks + ".exe");
                        File.Move(s.Item2, oldExe);
                        try { File.Move(s.Item1, s.Item2); }
                        catch { try { File.Move(oldExe, s.Item2); } catch { } throw; }
                        try { File.Delete(oldExe); } catch { }
                    }
                    else
                    {
                        if (File.Exists(s.Item2)) File.Delete(s.Item2);
                        File.Move(s.Item1, s.Item2);
                    }
                }
                catch (Exception ex)
                {
                    // only the uninstaller may be left as it was; anything else is a real failure
                    if (Same(s.Item2, Uninstaller) && File.Exists(Uninstaller))
                    {
                        try { File.Delete(s.Item1); } catch { }
                        res.Warning = "The uninstaller couldn't be replaced, the old one stays.";
                        continue;
                    }
                    foreach (var t in staged) try { if (File.Exists(t.Item1)) File.Delete(t.Item1); } catch { }
                    throw new Exception("Couldn't write " + Path.GetFileName(s.Item2) + " (" + ex.Message + ")");
                }
            }

            // old copies the user may want to delete (the running one we closed, and where the old Start menu shortcut pointed)
            string oldLink = Shortcuts.Target(StartLink);
            foreach (var p in (closedCopies ?? Enumerable.Empty<string>()).Concat(new[] { oldLink }))
                if (p != null && !InDir(p) && File.Exists(p) && !res.OldCopies.Any(x => Same(x, p))
                    && string.Equals(SafeProduct(p), "Minibar", StringComparison.OrdinalIgnoreCase))
                    res.OldCopies.Add(p);

            progress("Adding shortcuts…");
            Shortcuts.Make(StartLink, Exe, "Home for your drink apps");
            if (desktop) Shortcuts.Make(DesktopLink, Exe, "Home for your drink apps");
            else if (InDir(Shortcuts.Target(DesktopLink))) try { File.Delete(DesktopLink); } catch { }

            progress("Registering…");
            Register();
            return res;
        }

        static string SafeProduct(string p) { try { return FileVersionInfo.GetVersionInfo(p).ProductName; } catch { return null; } }

        public static void Register()
        {
            long kb = 0;
            try { kb = new DirectoryInfo(Dir).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024; } catch { }
            using (var k = Registry.CurrentUser.CreateSubKey(RegKey))
            {
                k.SetValue("DisplayName", "Minibar");
                k.SetValue("DisplayVersion", VersionText(Exe) ?? PayloadVersion);
                k.SetValue("Publisher", "Pingo");
                k.SetValue("Comments", "Home for your drink apps");
                k.SetValue("DisplayIcon", Exe + ",0");
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + Uninstaller + "\"");
                k.SetValue("QuietUninstallString", "\"" + Uninstaller + "\" --quiet");
                k.SetValue("URLInfoAbout", "https://github.com/iiiPingo/minibar");
                k.SetValue("HelpLink", "https://github.com/iiiPingo/minibar");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, Math.Max(1, kb)), RegistryValueKind.DWord);
            }
        }

        public static bool Registered
        {
            get { try { using (var k = Registry.CurrentUser.OpenSubKey(RegKey)) return k != null; } catch { return false; } }
        }

        public static void Launch()
        {
            Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = true, WorkingDirectory = Dir })?.Dispose();
        }

        // ------------------------------------------------------------ uninstall
        // Removes Minibar's folder, its shortcuts and its Installed apps entry. The drink apps themselves stay.
        // Returns files that are still locked (the uninstaller that started us); Cleanup deletes those afterwards.
        public static List<string> Uninstall(bool removeData, Action<string> progress)
        {
            progress("Removing shortcuts…");
            foreach (var l in new[] { StartLink, DesktopLink })
                if (InDir(Shortcuts.Target(l))) try { File.Delete(l); } catch { }

            progress("Removing Minibar…");
            try { Registry.CurrentUser.DeleteSubKeyTree(RegKey, false); } catch { }

            var left = new List<string>();
            if (Directory.Exists(Dir) && Path.GetFileName(Dir.TrimEnd('\\', '/')).Equals("Minibar", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var f in Directory.GetFiles(Dir, "*", SearchOption.AllDirectories))
                {
                    if (Same(f, SelfPath)) continue;
                    if (!DeleteRetry(f)) left.Add(f);
                }
                foreach (var d in Directory.GetDirectories(Dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                    try { Directory.Delete(d, false); } catch { }
                try { Directory.Delete(Dir, false); } catch { }
            }

            if (removeData)
            {
                progress("Deleting settings…");
                foreach (var d in new[] { DataDir, LocalDataDir })
                    try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            }
            return left;
        }

        static bool DeleteRetry(string f)
        {
            for (int i = 0; i < 8; i++)
            {
                try
                {
                    if (!File.Exists(f)) return true;
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    return true;
                }
                catch { Thread.Sleep(250); }
            }
            return false;
        }

        // a hidden cmd removes what a running exe couldn't remove from itself, a few seconds after we exit
        public static void Cleanup(IEnumerable<string> files, string dir)
        {
            try
            {
                var sb = new StringBuilder("/d /c ping 127.0.0.1 -n 4 >nul");
                foreach (var f in files.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                    sb.Append(" & del /f /q \"" + f + "\"");
                if (dir != null) sb.Append(" & rmdir \"" + dir + "\"");
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), sb.ToString())
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetTempPath() })?.Dispose();
            }
            catch { }
        }
    }

    // .lnk files through WScript.Shell (late bound, no references needed)
    static class Shortcuts
    {
        public static void Make(string link, string target, string desc)
        {
            object shell = null, sc = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(link));
                shell = Activator.CreateInstance(t);
                sc = Call(shell, "CreateShortcut", link);
                Set(sc, "TargetPath", target);
                Set(sc, "WorkingDirectory", Path.GetDirectoryName(target));
                Set(sc, "IconLocation", target + ",0");
                Set(sc, "Description", desc);   // no "Added by Minibar" mark: Minibar leaves this one alone
                Call(sc, "Save");
            }
            catch { }
            finally { Release(sc); Release(shell); }
        }

        public static string Target(string link)
        {
            object shell = null, sc = null;
            try
            {
                if (!File.Exists(link)) return null;
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                shell = Activator.CreateInstance(t);
                sc = Call(shell, "CreateShortcut", link);
                string s = Get(sc, "TargetPath") as string;
                return string.IsNullOrEmpty(s) ? null : s;
            }
            catch { return null; }
            finally { Release(sc); Release(shell); }
        }

        static object Call(object o, string m, params object[] args) => o.GetType().InvokeMember(m, BindingFlags.InvokeMethod, null, o, args);
        static object Get(object o, string p) => o.GetType().InvokeMember(p, BindingFlags.GetProperty, null, o, null);
        static void Set(object o, string p, object v) => o.GetType().InvokeMember(p, BindingFlags.SetProperty, null, o, new[] { v });
        static void Release(object o) { try { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); } catch { } }
    }

    static class Win
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder buf, ref int size);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        public static string ProcessPath(int pid)
        {
            try
            {
                IntPtr h = OpenProcess(0x1000, false, pid);
                if (h == IntPtr.Zero) return null;
                try
                {
                    var sb = new StringBuilder(1024);
                    int n = sb.Capacity;
                    return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString(0, n) : null;
                }
                finally { CloseHandle(h); }
            }
            catch
            {
                try { using (var p = Process.GetProcessById(pid)) return p.MainModule?.FileName; } catch { return null; }
            }
        }

        public static List<IntPtr> WindowsOf(int pid)
        {
            var list = new List<IntPtr>();
            try
            {
                EnumWindows((h, l) =>
                {
                    GetWindowThreadProcessId(h, out int p);
                    if (p == pid) list.Add(h);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return list;
        }

        public static string TitleOf(IntPtr h)
        {
            try { var sb = new StringBuilder(256); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); } catch { return ""; }
        }
    }
}
