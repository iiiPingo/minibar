using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace Minibar
{
    // %APPDATA%\Minibar\settings.ini: plain key=value lines, readable and hand-editable
    class Settings
    {
        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minibar");
        public static string FilePath => Path.Combine(Dir, "settings.ini");

        public string Accent = "";                       // empty = honey
        public Rectangle Window = Rectangle.Empty;       // negative w/h = maximized
        public bool MinimizeToTray = false;              // tray=1: minimizing hides the window to a tray icon
        public bool CheckUpdates = true;                 // checkupdates=0 turns the GitHub check off
        public DateTime LastCheck = DateTime.MinValue;   // lastcheck= (UTC ticks)
        // auto.<App>=1: install that app's updates by itself when Minibar finds them (off by default)
        public readonly HashSet<string> AutoUpdate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // rel.<App>= the last release Minibar saw on GitHub, so opening Minibar doesn't ask GitHub every time
        public readonly Dictionary<string, string> Releases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // path.<App>= an exe you picked with "Locate..." (wins over everything Minibar finds itself)
        public readonly Dictionary<string, string> Located = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // run.<App>= the app's own start-with-Windows command, kept while it's switched off so turning it back on restores it exactly
        public readonly Dictionary<string, string> RunCmd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k.Equals("accent", StringComparison.OrdinalIgnoreCase)) s.Accent = v;
                    else if (k.Equals("window", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = v.Split(',').Select(x => int.TryParse(x.Trim(), out int n) ? n : int.MinValue).ToArray();
                        if (p.Length == 4 && p.All(n => n != int.MinValue)) s.Window = new Rectangle(p[0], p[1], p[2], p[3]);
                    }
                    else if (k.Equals("tray", StringComparison.OrdinalIgnoreCase)) s.MinimizeToTray = v == "1";
                    else if (k.Equals("checkupdates", StringComparison.OrdinalIgnoreCase)) s.CheckUpdates = v != "0";
                    else if (k.Equals("lastcheck", StringComparison.OrdinalIgnoreCase)) { if (long.TryParse(v, out long t) && t > 0 && t < DateTime.MaxValue.Ticks) s.LastCheck = new DateTime(t, DateTimeKind.Utc); }
                    else if (k.StartsWith("auto.", StringComparison.OrdinalIgnoreCase)) { if (v == "1") s.AutoUpdate.Add(k.Substring(5)); }
                    else if (k.StartsWith("rel.", StringComparison.OrdinalIgnoreCase) && v.Length > 0) s.Releases[k.Substring(4)] = v;
                    else if (k.StartsWith("path.", StringComparison.OrdinalIgnoreCase) && v.Length > 0) s.Located[k.Substring(5)] = v;
                    else if (k.StartsWith("run.", StringComparison.OrdinalIgnoreCase) && v.Length > 0) s.RunCmd[k.Substring(4)] = v;
                }
            }
            catch (Exception ex) { Log.Crash(ex); }
            return s;
        }

        // after a restore: take over what was read from the restored file
        public void CopyFrom(Settings o)
        {
            Accent = o.Accent; MinimizeToTray = o.MinimizeToTray; CheckUpdates = o.CheckUpdates;
            AutoUpdate.Clear(); foreach (var a in o.AutoUpdate) AutoUpdate.Add(a);
            Located.Clear(); foreach (var kv in o.Located) Located[kv.Key] = kv.Value;
            RunCmd.Clear(); foreach (var kv in o.RunCmd) RunCmd[kv.Key] = kv.Value;
        }

        static readonly object saveGate = new object();

        public void Save()
        {
            lock (saveGate)
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# Minibar settings (safe to edit while Minibar is closed)");
                sb.AppendLine("accent=" + Accent);
                if (!Window.IsEmpty) sb.AppendLine("window=" + Window.X + "," + Window.Y + "," + Window.Width + "," + Window.Height);
                sb.AppendLine("tray=" + (MinimizeToTray ? "1" : "0"));
                sb.AppendLine("checkupdates=" + (CheckUpdates ? "1" : "0"));
                if (LastCheck > DateTime.MinValue) sb.AppendLine("lastcheck=" + LastCheck.Ticks);
                foreach (var a in AutoUpdate.OrderBy(k => k)) sb.AppendLine("auto." + a + "=1");
                foreach (var kv in Releases.OrderBy(k => k.Key)) sb.AppendLine("rel." + kv.Key + "=" + kv.Value);
                foreach (var kv in Located.OrderBy(k => k.Key)) sb.AppendLine("path." + kv.Key + "=" + kv.Value);
                foreach (var kv in RunCmd.OrderBy(k => k.Key)) sb.AppendLine("run." + kv.Key + "=" + kv.Value);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Crash(ex); }
        }
    }
}
