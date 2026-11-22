using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MinibarSetup
{
    static class Program
    {
        // Minibar-Setup.exe                 install / update (window)
        // "Uninstall Minibar.exe"           the same file copied into Minibar's folder: uninstall (window)
        //   --uninstall [--quiet]           uninstall; --quiet = no window, keeps settings (Installed apps' QuietUninstallString)
        [STAThread]
        static int Main(string[] args)
        {
            bool uninstall = args.Contains("--uninstall") || Path.GetFileName(Inst.SelfPath).IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0;
            bool quiet = args.Contains("--quiet");
            bool relocated = args.Contains("--relocated");
            Inst.UseRegisteredDir();

            // Uninstall runs from a temp copy so Minibar's whole folder can go. This process waits for it,
            // because Installed apps and Revo watch the uninstaller they started.
            if (uninstall && !relocated)
            {
                try
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "Minibar-Uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                    File.Copy(Inst.SelfPath, tmp, true);
                    using (var p = Process.Start(new ProcessStartInfo(tmp, "--uninstall --relocated" + (quiet ? " --quiet" : "")) { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() }))
                    {
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                }
                catch { }   // couldn't: uninstall from here, the cleanup removes this file afterwards
            }

            using (var mutex = new Mutex(true, "Minibar.Setup.v1", out bool created))
            {
                if (!created) return 1;   // a setup window is already open

                if (uninstall && quiet)
                {
                    try
                    {
                        var running = Inst.FindRunning().Where(r => Inst.InDir(r.Path)).ToList();
                        Inst.Kill(Inst.Close(running, 20000));
                        var left = Inst.Uninstall(false, _ => { });
                        Inst.Cleanup(left.Concat(new[] { Inst.Uninstaller, relocated ? Inst.SelfPath : null }), Inst.Dir);
                        return 0;
                    }
                    catch { return 2; }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                try { UseMinibarAccent(); } catch { }
                var w = new SetupWindow(uninstall);
                Application.ThreadException += (o, e) => w.ShowError(e.Exception);
                Application.Run(w);
                if (uninstall)
                    Inst.Cleanup(w.Removed ? w.Leftovers.Concat(new[] { Inst.Uninstaller, relocated ? Inst.SelfPath : null }) : new[] { relocated ? Inst.SelfPath : null },
                        w.Removed ? Inst.Dir : null);
                return 0;
            }
        }

        // the accent colour he picked in Minibar, so the setup looks like his Minibar
        static void UseMinibarAccent()
        {
            string f = Path.Combine(Inst.DataDir, "settings.ini");
            if (!File.Exists(f)) return;
            foreach (var line in File.ReadAllLines(f))
            {
                var t = line.Trim();
                if (t.StartsWith("accent=", StringComparison.OrdinalIgnoreCase) && Minibar.Theme.TryHex(t.Substring(7), out var c) && t.Length > 7)
                    Minibar.Theme.SetAccent(c);
            }
        }
    }
}
