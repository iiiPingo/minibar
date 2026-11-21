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
