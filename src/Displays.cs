using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace Minibar
{
    enum BrightState { Reading, Ok, Unavailable }

    // one connected display, as shown on the Displays page (owned by the UI thread once handed over)
    class DisplayInfo
    {
        public string Key;              // GDI device name, e.g. \\.\DISPLAY1 (stable while it stays plugged in)
        public string Name;             // the monitor's own name (EDID), e.g. "DELL S2721DGF"
        public bool Primary, Internal;
        public string Connection;       // "HDMI", "DisplayPort", "Built-in"... or null
        public int Width, Height, Hz, Scale;   // Scale = Windows scaling in percent (100, 125, 150...)
        public Rectangle Bounds;        // physical pixels, for the order
        public BrightState State = BrightState.Reading;
        public int Brightness = -1;     // 0-100, -1 = not known yet
        public string Why;              // short reason when unavailable
    }

    // ---------------------------------------------------------------- pure logic (tested off Windows)
    static class DisplayMath
    {
        public static int Clamp(int v) => v < 0 ? 0 : v > 100 ? 100 : v;

        // GetDpiForMonitor's effective DPI as the percentage Settings shows (96 = 100%, 144 = 150%)
        public static int ScalePercent(uint dpi) => dpi == 0 ? 100 : (int)Math.Round(dpi * 100.0 / 96.0);

        // a monitor's raw brightness range (DDC/CI: usually 0-100, some 0-255 or 0-50) <-> percent
        public static uint ToRaw(int pct, uint min, uint max)
        {
            pct = Clamp(pct);
            if (max <= min) return min;
            return (uint)(min + Math.Round((max - min) * pct / 100.0));
        }

        public static int ToPercent(uint raw, uint min, uint max)
        {
            if (max <= min) return 0;
            if (raw <= min) return 0;
            if (raw >= max) return 100;
            return Clamp((int)Math.Round((raw - min) * 100.0 / (max - min)));
        }

        // dmDisplayFrequency is 0 or 1 when the driver uses its default: then the path's exact rate, rounded
        public static int Hz(int dmFrequency, uint num, uint den)
        {
            if (dmFrequency > 1) return dmFrequency;
            if (den == 0 || num == 0) return 0;
            return (int)Math.Round(num / (double)den);
        }

        // Primary first, then left to right, top to bottom
        public static List<DisplayInfo> Order(IEnumerable<DisplayInfo> all) =>
            all.OrderByDescending(d => d.Primary).ThenBy(d => d.Bounds.X).ThenBy(d => d.Bounds.Y).ThenBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToList();

        // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY
        public static bool IsInternal(int tech) => tech == 6 /* LVDS */ || tech == 11 /* DP embedded (eDP) */ || tech == 13 /* UDI embedded */ || tech == unchecked((int)0x80000000);

        public static string Connection(int tech)
        {
            switch (tech)
            {
                case 0: return "VGA";
                case 4: return "DVI";
                case 5: return "HDMI";
                case 10: return "DisplayPort";
                case 15: return "Miracast";
                case 16: return "Wireless";
                case 17: return "Wireless";
                default: return IsInternal(tech) ? "Built-in" : null;
            }
        }

        public static string Resolution(DisplayInfo d) => d.Width > 0 && d.Height > 0 ? d.Width + " \u00D7 " + d.Height : "Unknown";
        public static string Rate(DisplayInfo d) => d.Hz > 0 ? d.Hz + " Hz" : "Unknown";
        public static string ScaleText(DisplayInfo d) => d.Scale > 0 ? d.Scale + "%" : "Unknown";

        // the name to show: the monitor's own, else something plain
        public static string NameFor(string friendly, bool internalPanel, int index)
        {
            friendly = (friendly ?? "").Trim();
            if (friendly.Length > 0 && !friendly.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase)) return friendly;
            return internalPanel ? "Built-in display" : "Display " + index;
        }
    }

    // Rate limit for slider drags: the first value goes at once, then at most one every IntervalMs,
    // always ending on the last value (Tick while dragging, Flush on mouse-up). Time is passed in, so it's testable.
    sealed class Coalescer
    {
        public int IntervalMs = 70;
        long lastSent = long.MinValue / 2;
        int? pending, sentValue;

        public bool HasPending => pending != null;

        public int? Push(int v, long nowMs) { pending = DisplayMath.Clamp(v); return Tick(nowMs); }

        public int? Tick(long nowMs)
        {
            if (pending == null || nowMs - lastSent < IntervalMs) return null;
            return Take(nowMs);
        }

        public int? Flush(long nowMs) => pending == null ? null : Take(nowMs);

        int? Take(long nowMs)
        {
            int v = pending.Value;
            pending = null;
            if (sentValue == v) return null;   // already there
            sentValue = v;
            lastSent = nowMs;
            return v;
        }

        // the value on screen was read again (refresh): forget what was sent
        public void Reset() { pending = null; sentValue = null; lastSent = long.MinValue / 2; }
    }

    // ---------------------------------------------------------------- Win32: monitors, modes, names
    static class DisplayNative
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFOEX mi);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);

        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr h, int type, out uint x, out uint y);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

        // ---- QueryDisplayConfig
        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)]
        struct PATH_SOURCE { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
        [StructLayout(LayoutKind.Sequential)]
        struct PATH_TARGET
        {
            public LUID adapterId; public uint id, modeInfoIdx;
            public int outputTechnology, rotation, scaling;
            public uint refreshNum, refreshDen;
            public int scanLineOrdering, targetAvailable;
            public uint statusFlags;
        }
        [StructLayout(LayoutKind.Sequential)] struct PATH_INFO { public PATH_SOURCE source; public PATH_TARGET target; public uint flags; }
        [StructLayout(LayoutKind.Explicit, Size = 64)] struct MODE_INFO { [FieldOffset(0)] public uint infoType; }
        [StructLayout(LayoutKind.Sequential)] struct HEADER { public int type; public int size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SOURCE_NAME { public HEADER header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TARGET_NAME
        {
            public HEADER header;
            public uint flags;
            public int outputTechnology;
            public ushort edidManufactureId, edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME req);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME req);
        const uint QDC_ONLY_ACTIVE_PATHS = 2;

        public class Target { public string Friendly, DevicePath; public int Tech; public uint RefreshNum, RefreshDen; }

        // GDI device name (\\.\DISPLAY1) -> the monitor's EDID name, connection type and exact refresh rate
        static Dictionary<string, Target> Targets()
        {
            var map = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
            try
            {
                PATH_INFO[] paths = null; MODE_INFO[] modes = null;
                uint np = 0, nm = 0;
                for (int tries = 0; tries < 3; tries++)
                {
                    if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0) return map;
                    paths = new PATH_INFO[np]; modes = new MODE_INFO[nm];
                    int rc = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero);
                    if (rc == 0) break;
                    if (rc != 122 /* ERROR_INSUFFICIENT_BUFFER: changed in between */) return map;
                    paths = null;
                }
                if (paths == null) return map;
                for (int i = 0; i < np; i++)
                {
                    var p = paths[i];
                    var src = new SOURCE_NAME { header = new HEADER { type = 1, size = Marshal.SizeOf(typeof(SOURCE_NAME)), adapterId = p.source.adapterId, id = p.source.id } };
                    if (DisplayConfigGetDeviceInfo(ref src) != 0 || string.IsNullOrEmpty(src.viewGdiDeviceName)) continue;
                    var tgt = new TARGET_NAME { header = new HEADER { type = 2, size = Marshal.SizeOf(typeof(TARGET_NAME)), adapterId = p.target.adapterId, id = p.target.id } };
                    bool named = DisplayConfigGetDeviceInfo(ref tgt) == 0;
                    var t = new Target
                    {
                        Friendly = named ? tgt.monitorFriendlyDeviceName : null,
                        DevicePath = named ? tgt.monitorDevicePath : null,
                        Tech = named ? tgt.outputTechnology : p.target.outputTechnology,
                        RefreshNum = p.target.refreshNum, RefreshDen = p.target.refreshDen
                    };
                    // duplicated screens: one source, several targets; keep the first that has a name
                    if (!map.TryGetValue(src.viewGdiDeviceName, out var had) || (string.IsNullOrWhiteSpace(had.Friendly) && !string.IsNullOrWhiteSpace(t.Friendly)))
                        map[src.viewGdiDeviceName] = t;
                }
            }
            catch (Exception ex) { Log.Crash(ex); }
            return map;
        }

        public class Found { public DisplayInfo Info; public IntPtr HMonitor; public string DevicePath; }

        // every active display. Runs on the display worker thread, switched to per-monitor DPI awareness
        // for the call, so GetDpiForMonitor gives each monitor's own scale (not the system's).
        public static List<Found> Scan()
        {
            IntPtr old = IntPtr.Zero;
            try { old = SetThreadDpiAwarenessContext(new IntPtr(-4) /* PER_MONITOR_AWARE_V2 */); } catch { }
            try
            {
                var targets = Targets();
                var mons = new List<IntPtr>();
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) => { mons.Add(h); return true; }, IntPtr.Zero);
                var list = new List<Found>();
                int n = 0;
                foreach (var h in mons)
                {
                    n++;
                    var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                    if (!GetMonitorInfo(h, ref mi)) continue;
                    var d = new DisplayInfo
                    {
                        Key = mi.szDevice,
                        Primary = (mi.dwFlags & 1) != 0,
                        Bounds = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom)
                    };
                    var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                    int freq = 0;
                    if (EnumDisplaySettings(mi.szDevice, -1 /* ENUM_CURRENT_SETTINGS */, ref dm))
                    {
                        d.Width = dm.dmPelsWidth; d.Height = dm.dmPelsHeight; freq = dm.dmDisplayFrequency;
                    }
                    else { d.Width = d.Bounds.Width; d.Height = d.Bounds.Height; }
                    try { if (GetDpiForMonitor(h, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out _) == 0) d.Scale = DisplayMath.ScalePercent(dx); } catch { }
                    targets.TryGetValue(mi.szDevice, out var t);
                    d.Internal = t != null && DisplayMath.IsInternal(t.Tech);
                    d.Connection = t == null ? null : DisplayMath.Connection(t.Tech);
                    d.Hz = DisplayMath.Hz(freq, t?.RefreshNum ?? 0, t?.RefreshDen ?? 0);
                    d.Name = DisplayMath.NameFor(t?.Friendly, d.Internal, n);
                    list.Add(new Found { Info = d, HMonitor = h, DevicePath = t?.DevicePath });
                }
                var order = DisplayMath.Order(list.Select(f => f.Info));
                // "Display 2" numbers follow the order on the page
                int k = 0;
                foreach (var d in order) { k++; if (d.Name.StartsWith("Display ") && int.TryParse(d.Name.Substring(8), out _)) d.Name = "Display " + k; }
                return order.Select(d => list.First(f => f.Info == d)).ToList();
            }
            finally { try { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); } catch { } }
        }
    }

    // ---------------------------------------------------------------- brightness: WMI (built-in panel) and DDC/CI (external)
    static class Brightness
    {
        // ---- DDC/CI through dxva2.dll. Handles are opened per call and always destroyed.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PHYSICAL_MONITOR { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint n);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint n, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool DestroyPhysicalMonitors(uint n, [In] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool SetMonitorBrightness(IntPtr h, uint v);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool SetVCPFeature(IntPtr h, byte code, uint v);
        const byte VcpLuminance = 0x10;

        static T WithPhysical<T>(IntPtr hMon, Func<IntPtr, T> work, T none)
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint n) || n == 0 || n > 16) return none;
            var arr = new PHYSICAL_MONITOR[n];
            if (!GetPhysicalMonitorsFromHMONITOR(hMon, n, arr)) return none;
            try { return work(arr[0].h); }
            finally { try { DestroyPhysicalMonitors(n, arr); } catch { } }
        }

        // method: 1 = VCP 0x10 (fast, no capabilities string), 2 = GetMonitorBrightness (slower, the high-level API)
        public static int DdcRead(IntPtr hMon, out int method)
        {
            int m = 0;
            int pct = WithPhysical(hMon, h =>
            {
                for (int attempt = 0; attempt < 2; attempt++)   // DDC/CI often misses the first request after waking
                {
                    if (GetVCPFeatureAndVCPFeatureReply(h, VcpLuminance, IntPtr.Zero, out uint cur, out uint max) && max > 0) { m = 1; return DisplayMath.ToPercent(cur, 0, max); }
                    if (GetMonitorBrightness(h, out uint mn, out uint c2, out uint mx) && mx > mn) { m = 2; return DisplayMath.ToPercent(c2, mn, mx); }
                    Thread.Sleep(40);
                }
                return -1;
            }, -1);
            method = m;
            return pct;
        }

        public static bool DdcWrite(IntPtr hMon, int method, int pct)
        {
            return WithPhysical(hMon, h =>
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (method != 2 && GetVCPFeatureAndVCPFeatureReply(h, VcpLuminance, IntPtr.Zero, out _, out uint max) && max > 0)
                    { if (SetVCPFeature(h, VcpLuminance, DisplayMath.ToRaw(pct, 0, max))) return true; }
                    else if (GetMonitorBrightness(h, out uint mn, out _, out uint mx) && mx > mn)
                    { if (SetMonitorBrightness(h, DisplayMath.ToRaw(pct, mn, mx))) return true; }
                    Thread.Sleep(30);
                }
                return false;
            }, false);
        }

        // ---- WMI root\wmi for the built-in panel. instance = WmiMonitorBrightness.InstanceName, or null for the first active one.
        public static int WmiRead(string devicePath, out string instance)
        {
            instance = null;
            try
            {
                using (var q = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE"))
                using (var res = q.Get())
                {
                    string first = null; int firstVal = -1;
                    foreach (ManagementObject o in res)
                        using (o)
                        {
                            string inst = o["InstanceName"] as string;
                            int v = Convert.ToInt32(o["CurrentBrightness"]);
                            if (first == null) { first = inst; firstVal = v; }
                            if (Matches(inst, devicePath)) { instance = inst; return DisplayMath.Clamp(v); }
                        }
                    instance = first;
                    return first == null ? -1 : DisplayMath.Clamp(firstVal);
                }
            }
            catch { return -1; }
        }

        public static bool WmiWrite(string instance, int pct)
        {
            try
            {
                using (var mc = new ManagementClass(@"root\wmi", "WmiMonitorBrightnessMethods", null))
                using (var all = mc.GetInstances())
                {
                    foreach (ManagementObject o in all)
                        using (o)
                        {
                            if (instance != null && !string.Equals(o["InstanceName"] as string, instance, StringComparison.OrdinalIgnoreCase)) continue;
                            using (var p = o.GetMethodParameters("WmiSetBrightness"))
                            {
                                p["Timeout"] = (uint)1;
                                p["Brightness"] = (byte)DisplayMath.Clamp(pct);
                                o.InvokeMethod("WmiSetBrightness", p, null)?.Dispose();
                            }
                            return true;
                        }
                }
            }
            catch { }
            return false;
        }

        // WMI: DISPLAY\BOE0A1B\4&2a7f1c0&0&UID8388688_0; device path: \\?\DISPLAY#BOE0A1B#4&2a7f1c0&0&UID8388688#{e6f07b5f-...}
        public static bool Matches(string wmiInstance, string devicePath)
        {
            if (string.IsNullOrEmpty(wmiInstance) || string.IsNullOrEmpty(devicePath)) return false;
            var p = devicePath.Split('#');
            if (p.Length < 3) return false;
            string want = ("DISPLAY\\" + p[1] + "\\" + p[2]).ToUpperInvariant();
            return wmiInstance.ToUpperInvariant().StartsWith(want);
        }
    }

    // ---------------------------------------------------------------- the worker: one background thread does all display work
    // Monitor scans, WMI and DDC/CI (slow I2C) never run on the UI thread. Brightness writes keep only the newest value per
    // display, so a slow monitor never builds up a backlog. Results are handed to `post` (the UI thread).
    sealed class DisplayService : IDisposable
    {
        sealed class Slot { public IntPtr HMon; public bool Internal; public string DevicePath, WmiInstance; public int Method = -1; public bool Dead; }

        readonly Action<Action> post;
        readonly object gate = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Dictionary<string, int> pendingSet = new Dictionary<string, int>();
        readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>();   // worker thread only
        Thread th;
        int scanGen;          // newest refresh asked for
        int doneGen;          // newest refresh the worker ran
        volatile bool stop;

        public event Action<int, List<DisplayInfo>> Scanned;            // gen, displays (Reading state)
        public event Action<int, string, int, string> BrightnessRead;   // gen, key, percent or -1, why when -1
        public event Action<string> WriteFailed;                        // key

        public DisplayService(Action<Action> post) { this.post = post; }

        public int Gen { get { lock (gate) return scanGen; } }

        void Ensure()
        {
            if (th != null) return;
            th = new Thread(Loop) { IsBackground = true, Name = "Minibar displays" };
            th.Start();
        }

        public int Refresh()
        {
            int g;
            lock (gate) { g = ++scanGen; }
            Ensure();
            wake.Set();
            return g;
        }

        public void Set(string key, int pct)
        {
            if (key == null) return;
            lock (gate) pendingSet[key] = DisplayMath.Clamp(pct);
            Ensure();
            wake.Set();
        }

        void Loop()
        {
            while (!stop)
            {
                try
                {
                    wake.WaitOne(2000);
                    if (stop) return;
                    while (!stop)
                    {
                        // brightness writes first (the slider is what he's looking at), newest value only
                        KeyValuePair<string, int>? w = null;
                        int g;
                        lock (gate)
                        {
                            if (pendingSet.Count > 0) { var f = pendingSet.First(); pendingSet.Remove(f.Key); w = f; }
                            g = scanGen;
                        }
                        if (w != null) { Write(w.Value.Key, w.Value.Value); continue; }
                        if (g != doneGen) { doneGen = g; RunScan(g); continue; }
                        break;
                    }
                }
                catch (Exception ex) { Log.Crash(ex); }
            }
        }

        bool Stale(int g) { lock (gate) return g != scanGen; }

        void RunScan(int g)
        {
            List<DisplayNative.Found> found;
            try { found = DisplayNative.Scan(); }
            catch (Exception ex) { Log.Crash(ex); found = new List<DisplayNative.Found>(); }
            var old = new Dictionary<string, Slot>(slots);
            slots.Clear();
            foreach (var f in found)
            {
                old.TryGetValue(f.Info.Key, out var was);
                slots[f.Info.Key] = new Slot
                {
                    HMon = f.HMonitor, Internal = f.Info.Internal, DevicePath = f.DevicePath,
                    // same monitor still there: remember which way worked
                    WmiInstance = was != null && was.DevicePath == f.DevicePath ? was.WmiInstance : null,
                    Method = was != null && was.DevicePath == f.DevicePath ? was.Method : -1
                };
            }
            var infos = found.Select(f => f.Info).ToList();
            post(() => Scanned?.Invoke(g, infos));
            foreach (var d in infos)
            {
                if (Stale(g)) return;
                // a waiting slider value goes before the next (slow) read
                bool writes; lock (gate) writes = pendingSet.Count > 0;
                if (writes)
                {
                    KeyValuePair<string, int>[] ws;
                    lock (gate) { ws = pendingSet.ToArray(); pendingSet.Clear(); }
                    foreach (var w in ws) Write(w.Key, w.Value);
                }
                string why;
                int pct = Read(d.Key, out why);
                string key = d.Key;
                post(() => BrightnessRead?.Invoke(g, key, pct, why));
            }
        }

        int Read(string key, out string why)
        {
            why = null;
            if (!slots.TryGetValue(key, out var s)) { why = "gone"; return -1; }
            try
            {
                if (s.Internal)
                {
                    int v = Brightness.WmiRead(s.DevicePath, out string inst);
                    if (v >= 0) { s.WmiInstance = inst; s.Method = 0; return v; }
                }
                int m;
                int pct = Brightness.DdcRead(s.HMon, out m);
                if (pct >= 0) { s.Method = m; return pct; }
                // an external screen that's actually WMI-driven (some all-in-ones / tablets)
                if (!s.Internal)
                {
                    int v = Brightness.WmiRead(s.DevicePath, out string inst);
                    if (v >= 0 && Brightness.Matches(inst, s.DevicePath)) { s.WmiInstance = inst; s.Method = 0; return v; }
                }
                why = s.Internal ? "WMI" : "DDC/CI";
                s.Dead = true;
                return -1;
            }
            catch (Exception ex) { Log.Crash(ex); why = s.Internal ? "WMI" : "DDC/CI"; s.Dead = true; return -1; }
        }

        void Write(string key, int pct)
        {
            if (!slots.TryGetValue(key, out var s) || s.Dead || s.Method < 0) return;
            bool ok;
            try { ok = s.Method == 0 ? Brightness.WmiWrite(s.WmiInstance, pct) : Brightness.DdcWrite(s.HMon, s.Method, pct); }
            catch (Exception ex) { Log.Crash(ex); ok = false; }
            if (!ok) post(() => WriteFailed?.Invoke(key));
        }

        public void Dispose()
        {
            stop = true;
            try { wake.Set(); } catch { }
        }
    }
}
