using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading;

namespace Minibar
{
    // The blocks under the nav: Devices (from Juice's status.txt), Refill (from Refill's status.txt) and the update card.
    // Everything here is plain data and arithmetic, so it can be tested without a window. Reading never throws:
    // a missing, half-written or garbled file just hides its block.

    class Device
    {
        public string Name, Type;   // type: headphones, earbuds, headset, mouse, controller, keyboard, other
        public int Percent;         // 0..100
        public bool Charging;
        public bool Low => Percent <= SideMenu.LowAt;
    }

    class RefillStatus
    {
        public string Exe, LastFile, LastFolder, Paused;   // Paused: "" sorting, "resume", "off", or an ISO time
        public int SortedToday;
    }

    // where each block goes, in client pixels (already scaled)
    class SideLayout
    {
        public int Top, Bottom;                  // the free room between the nav items and the update card
        public int DevicesTop = -1, DevicesShown;   // -1 = hidden
        public int DividerY = -1;
        public int RefillTop = -1;
        public int CardTop, CardHeight;
        public bool CardButton;
        public int VersionY;
    }

    static class SideMenu
    {
        public const int MaxDevices = 4, LowAt = 20;

        // ------------------------------------------------------------ sizes (unscaled px; multiplied by the DPI scale)
        public const int NavItemsBottom = 226;      // 4 nav items: 70 + 3*40 + 36
        public const int GapTop = 14, GapBottom = 12;
        public const int DevHeader = 22, DevRow = 38, Divider = 14;
        public const int RefillH = 86;              // header 22, line 2+22, gap 8, buttons 32
        public const int CardWithButton = 100, CardNoButton = 66, CardGapBelow = 44;
        public const int VersionFromBottom = 36;

        public static int DevicesHeight(int n) => DevHeader + n * DevRow;

        // Fits the blocks between the nav items and the update card. The update card always shows, Refill next;
        // Devices gets what's left, one row per device, and gives up rows (highest battery first) before anything overlaps.
        public static SideLayout Arrange(int clientHeight, float s, int devices, bool refill, bool cardButton)
        {
            int S(float v) => (int)Math.Round(v * s);
            var L = new SideLayout { CardButton = cardButton };
            L.CardHeight = S(cardButton ? CardWithButton : CardNoButton);
            L.CardTop = clientHeight - S(CardGapBelow) - L.CardHeight;
            L.VersionY = clientHeight - S(VersionFromBottom);
            L.Top = S(NavItemsBottom + GapTop);
            L.Bottom = L.CardTop - S(GapBottom);
            int room = L.Bottom - L.Top;
            int y = L.Top;

            bool refillFits = refill && S(RefillH) <= room;
            int left = room - (refillFits ? S(RefillH) + S(Divider) : 0);
            int n = Math.Min(Math.Min(devices, MaxDevices), Math.Max(0, (left - S(DevHeader)) / Math.Max(1, S(DevRow))));
            if (n > 0)
            {
                L.DevicesTop = y;
                L.DevicesShown = n;
                y += S(DevicesHeight(n));
                if (refillFits) { L.DividerY = y + S(Divider) / 2; y += S(Divider); }
            }
            if (refillFits) L.RefillTop = y;
            return L;
        }

        // the devices to show when only n fit: the lowest batteries, kept in Juice's order
        public static List<Device> Pick(IList<Device> all, int n)
        {
            if (n <= 0 || all == null) return new List<Device>();
            if (all.Count <= n) return all.ToList();
            var keep = new HashSet<Device>(all.Select((d, i) => (d, i)).OrderBy(x => x.d.Percent).ThenBy(x => x.i).Take(n).Select(x => x.d));
            return all.Where(keep.Contains).ToList();
        }

        // ------------------------------------------------------------ reading the files
        // small, shared-read, never throws; null = not there or can't be read right now
        public static string ReadSmall(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length > 64 * 1024) return null;   // not a status file
                    var buf = new byte[fs.Length];
                    int got = 0;
                    while (got < buf.Length) { int k = fs.Read(buf, got, buf.Length - got); if (k <= 0) break; got += k; }
                    return Encoding.UTF8.GetString(buf, 0, got).TrimStart('\uFEFF');
                }
            }
            catch { return null; }
        }

        static IEnumerable<KeyValuePair<string, string>> Pairs(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            foreach (var raw in text.TrimStart('\uFEFF').Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                yield return new KeyValuePair<string, string>(line.Substring(0, eq).Trim().ToLowerInvariant(), line.Substring(eq + 1));
            }
        }

        // Juice: device=type|percent|charging|name. Bad lines are skipped; unknown levels aren't shown.
        public static List<Device> ParseJuice(string text)
        {
            var list = new List<Device>();
            foreach (var kv in Pairs(text))
            {
                if (kv.Key != "device") continue;
                var p = kv.Value.Split(new[] { '|' }, 4);
                if (p.Length < 4) continue;
                if (!int.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pct) || pct < 0) continue;
                string name = p[3].Trim();
                if (name.Length == 0) continue;
                list.Add(new Device { Type = p[0].Trim().ToLowerInvariant(), Percent = Math.Min(100, pct), Charging = p[2].Trim() == "1", Name = name });
            }
            return list;
        }

        // Refill: exe=, sorted_today=, last_file=, last_folder=, paused_until=. null when there's no exe line.
        public static RefillStatus ParseRefill(string text)
        {
            var r = new RefillStatus { LastFile = "", LastFolder = "", Paused = "" };
            foreach (var kv in Pairs(text))
            {
                string v = kv.Value.Trim();
                switch (kv.Key)
                {
                    case "exe": r.Exe = v; break;
                    case "sorted_today": int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r.SortedToday); if (r.SortedToday < 0) r.SortedToday = 0; break;
                    case "last_file": r.LastFile = v; break;
                    case "last_folder": r.LastFolder = v; break;
                    case "paused_until": r.Paused = v; break;
                }
            }
            return string.IsNullOrWhiteSpace(r.Exe) ? null : r;
        }

        // ------------------------------------------------------------ Refill's words
        // paused right now? (a timed pause that has ended counts as running)
        public static bool IsPaused(RefillStatus r, DateTime nowUtc) =>
            r.Paused == "resume" || r.Paused == "off" || (PauseEnd(r) is DateTime t && t > nowUtc);

        static DateTime? PauseEnd(RefillStatus r)
        {
            if (string.IsNullOrEmpty(r.Paused) || r.Paused == "resume" || r.Paused == "off") return null;
            if (DateTime.TryParse(r.Paused, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)) return t;
            return null;
        }

        // the right side of the header: "12 today" or "Paused" / "Off"
        public static string RefillBadge(RefillStatus r, DateTime nowUtc) =>
            r.Paused == "off" ? "Off" : IsPaused(r, nowUtc) ? "Paused" : r.SortedToday + " today";

        // the line under the header
        public static string RefillLine(RefillStatus r, DateTime nowUtc)
        {
            if (r.Paused == "off") return "Sorting is off";
            if (r.Paused == "resume") return "Until you resume";
            if (PauseEnd(r) is DateTime t && t > nowUtc)
            {
                int min = (int)Math.Ceiling((t - nowUtc).TotalMinutes);
                return min >= 60 ? "Resumes in " + (min / 60) + " h" + (min % 60 > 0 ? " " + (min % 60) + " min" : "") : "Resumes in " + min + " min";
            }
            if (string.IsNullOrEmpty(r.LastFile)) return "Nothing sorted yet";
            return r.LastFile + (string.IsNullOrEmpty(r.LastFolder) ? "" : " to " + r.LastFolder);
        }

        // tests: pretend Refill is running / read its status from here
#pragma warning disable 0649   // set by the tests only
        public static Func<bool> RunningHook;
        public static string RefillPathHook;
#pragma warning restore 0649

        // Refill is open (its hidden watcher or its window)
        public static bool RefillRunning()
        {
            if (RunningHook != null) return RunningHook();
            foreach (var name in new[] { "Local\\Refill.Watcher.v1", "Local\\Refill.Window.v1" })
            {
                try
                {
                    if (Mutex.TryOpenExisting(name, MutexRights.Synchronize, out Mutex m)) { m.Dispose(); return true; }
                }
                catch (UnauthorizedAccessException) { return true; }   // it's there, just not ours to open
                catch { }
            }
            return false;
        }

        public static string RefillStatusPath => RefillPathHook ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Refill", "status.txt");

        // ------------------------------------------------------------ update card words
        public static string UpdateTitle(int ready, bool checking, bool busy, bool everChecked) =>
            busy ? "Updating…" : checking ? "Checking…" : ready > 0 ? ready + (ready == 1 ? " update ready" : " updates ready") : everChecked ? "All up to date" : "Not checked yet";

        public static string Ago(TimeSpan t) =>
            t.TotalMinutes < 1 ? "just now" : t.TotalMinutes < 60 ? (int)t.TotalMinutes + " min ago" : t.TotalHours < 24 ? (int)t.TotalHours + " h ago" : (int)t.TotalDays + (t.TotalDays < 2 ? " day ago" : " days ago");

        // glyph for a device type (Segoe Fluent Icons / MDL2)
        public static string Glyph(string type)
        {
            switch (type)
            {
                case "headset": return "\uE95B";
                case "headphones": case "earbuds": return "\uE7F6";
                case "mouse": return "\uE962";
                case "controller": return "\uE7FC";
                case "keyboard": return "\uE765";
                default: return "\uE702";   // Bluetooth
            }
        }
    }
}
