using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Minibar
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // started by a self-update: wait for the old Minibar to close first
            int ai = Array.IndexOf(args, "--after-update");
            if (ai >= 0 && ai + 1 < args.Length && int.TryParse(args[ai + 1], out int oldPid))
                try { using (var old = System.Diagnostics.Process.GetProcessById(oldPid)) old.WaitForExit(15000); } catch { }
            Updater.CleanAfterSelfUpdate();
            using (var mutex = new Mutex(true, "Minibar.SingleInstance.v1", out bool created))
            {
                if (!created)
                {
                    // already open: bring that window up
                    Native.PostMessage((IntPtr)0xffff, Native.WM_SHOWMINIBAR, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                InstallInfo.Sync();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (o, e) => Log.Crash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (o, e) => Log.Crash(e.ExceptionObject as Exception);
                TaskScheduler.UnobservedTaskException += (o, e) => { Log.Crash(e.Exception); e.SetObserved(); };
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                MainWindow w;
                try
                {
                    var st = Settings.Load();
                    if (st.Accent.Length > 0 && Theme.TryHex(st.Accent, out var c)) Theme.SetAccent(c);
                    w = new MainWindow(st);
                }
                catch (Exception ex)
                {
                    Log.Crash(ex);
                    MessageBox.Show("Minibar couldn't start:\n\n" + ex.Message + "\n\nDetails are in " + Log.Path, "Minibar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Application.Run(w);
                GC.KeepAlive(mutex);
            }
        }
    }

    // Held by every install / update / rollback while it runs, so the setup (or anything else) can wait for it
    // instead of force-closing Minibar halfway through.
    static class UpdateLock
    {
        public const string Name = "Minibar.Updating.v1";
    }

    // Installed with Minibar-Setup: keep the version in Windows' Installed apps list current after self-updates
    static class InstallInfo
    {
        public const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Minibar";
        public static bool IsInstallDir(string dir)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key))
                {
                    string loc = (k?.GetValue("InstallLocation") as string ?? "").TrimEnd('\\');
                    return loc.Length > 0 && string.Equals(Path.GetFullPath(loc), Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        public static void Sync()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, true))
                {
                    if (k == null) return;
                    string loc = (k.GetValue("InstallLocation") as string ?? "").TrimEnd('\\');
                    string dir = (Path.GetDirectoryName(Application.ExecutablePath) ?? "").TrimEnd('\\');
                    if (loc.Length == 0 || !string.Equals(Path.GetFullPath(loc), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) return;
                    string v = Apps.VersionText(Application.ExecutablePath);
                    if (v != null && !string.Equals(k.GetValue("DisplayVersion") as string, v)) k.SetValue("DisplayVersion", v);
                }
            }
            catch { }
        }
    }

    static class Log
    {
        public static string Path => System.IO.Path.Combine(Settings.Dir, "crash.log");
        static readonly object gate = new object();
        public static void Crash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Settings.Dir);
                    var fi = new FileInfo(Path);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(Path);
                    File.AppendAllText(Path, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + ex + "\r\n\r\n");
                }
            }
            catch { }
        }
    }
}
