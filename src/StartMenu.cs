using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Minibar
{
    // Start menu shortcuts, so Windows search finds the apps ("Pulp", "Minibar"...).
    // Only shortcuts Minibar made itself (marked in their description) are ever changed or removed.
    static class StartMenu
    {
        const string Mark = "Added by Minibar";
        static readonly Dictionary<string, string> done = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string Folder => Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        static string LinkFor(string name) => Path.Combine(Folder, name + ".lnk");

        // Make, fix or remove shortcuts to match what Minibar sees. Cheap when nothing changed.
        public static void Sync(IEnumerable<AppInfo> apps, string selfExe, bool on)
        {
            try
            {
                if (string.IsNullOrEmpty(Folder)) return;
                foreach (var a in apps)
                {
                    if (a.Def.Self) continue;
                    One(a.Def.Name, on && a.Found && File.Exists(a.Exe) ? a.Exe : null, a.Def.Blurb);
                }
                One("Minibar", on ? selfExe : null, "Home for your drink apps");
            }
            catch { }   // never worth bothering anyone about
        }

        static void One(string name, string exe, string blurb)
        {
            if (done.TryGetValue(name, out var last) && string.Equals(last, exe ?? "", StringComparison.OrdinalIgnoreCase)) return;
            string link = LinkFor(name);
            object shell = null, sc = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                shell = Activator.CreateInstance(t);
                if (File.Exists(link))
                {
                    sc = Call(shell, "CreateShortcut", link);
                    bool ours = (Get(sc, "Description") as string ?? "").Contains(Mark);
                    string target = Get(sc, "TargetPath") as string ?? "";
                    if (!ours) { done[name] = exe ?? ""; return; }   // the user's own shortcut: leave it alone
                    if (exe == null) { File.Delete(link); done[name] = ""; return; }
                    if (string.Equals(target, exe, StringComparison.OrdinalIgnoreCase)) { done[name] = exe; return; }
                    Release(sc); sc = null;
                }
                else if (exe == null) { done[name] = ""; return; }

                sc = Call(shell, "CreateShortcut", link);
                Set(sc, "TargetPath", exe);
                Set(sc, "WorkingDirectory", Path.GetDirectoryName(exe));
                Set(sc, "IconLocation", exe + ",0");
                Set(sc, "Description", blurb + " · " + Mark);
                Call(sc, "Save");
                done[name] = exe;
            }
            catch
            {
                // can't write it (folder blocked, AV...): don't try again every 1.5 s; Look again / the setting retries
                done[name] = exe ?? "";
            }
            finally { Release(sc); Release(shell); }
        }

        static object Call(object o, string m, params object[] args) => o.GetType().InvokeMember(m, BindingFlags.InvokeMethod, null, o, args);
        static object Get(object o, string p) => o.GetType().InvokeMember(p, BindingFlags.GetProperty, null, o, null);
        static void Set(object o, string p, object v) => o.GetType().InvokeMember(p, BindingFlags.SetProperty, null, o, new[] { v });
        static void Release(object o) { try { if (o != null && System.Runtime.InteropServices.Marshal.IsComObject(o)) System.Runtime.InteropServices.Marshal.ReleaseComObject(o); } catch { } }

        // where Minibar's own shortcut for an app points (null when there's none, or it's the user's own)
        public static string TargetOf(string name)
        {
            object shell = null, sc = null;
            try
            {
                if (string.IsNullOrEmpty(Folder)) return null;
                string link = LinkFor(name);
                if (!File.Exists(link)) return null;
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                shell = Activator.CreateInstance(t);
                sc = Call(shell, "CreateShortcut", link);
                if (!(Get(sc, "Description") as string ?? "").Contains(Mark)) return null;
                return Get(sc, "TargetPath") as string;
            }
            catch { return null; }
            finally { Release(sc); Release(shell); }
        }

        // forget what was synced, so the next Sync checks every shortcut again
        public static void Reset() => done.Clear();
    }
}
