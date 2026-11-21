using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Minibar
{
    static class Native
    {
        public static readonly int WM_SHOWMINIBAR = SafeRegister("Minibar.Show.v1");
        static int SafeRegister(string n) { try { return RegisterWindowMessage(n); } catch { return 0; } }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string s);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        // ------------------------------------------------------------ processes
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder buf, ref int size);
        const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public static string ProcessPath(int pid)
        {
            if (pid <= 0) return null;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString(0, n) : null;
            }
            finally { CloseHandle(h); }
        }

        // ------------------------------------------------------------ windows of a process (for removing a stopped app's tray icon)
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);

        public static List<IntPtr> WindowsOf(ICollection<int> pids)
        {
            var list = new List<IntPtr>();
            try
            {
                EnumWindows((h, l) =>
                {
                    GetWindowThreadProcessId(h, out int pid);
                    if (pids.Contains(pid)) list.Add(h);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return list;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA d);

        // a force-closed app leaves its tray icon behind until the mouse passes over it; remove it ourselves
        public static void RemoveTrayIcons(IEnumerable<IntPtr> windows)
        {
            foreach (var h in windows)
                for (int id = 0; id <= 8; id++)
                {
                    try
                    {
                        var d = new NOTIFYICONDATA { cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)), hWnd = h, uID = id };
                        Shell_NotifyIcon(2, ref d);   // NIM_DELETE
                    }
                    catch { return; }
                }
        }

        // ------------------------------------------------------------ icons
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, int count, int flags);

        // test hook: lets the harness hand in icon frames where user32 isn't available
        public static Func<string, int, Bitmap> IconSource;

        static Bitmap RawIcon(string path, int size)
        {
            if (IconSource != null) return IconSource(path, size);
            try
            {
                var h = new IntPtr[1];
                var ids = new int[1];
                if (PrivateExtractIcons(path, 0, size, size, h, ids, 1, 0) < 1 || h[0] == IntPtr.Zero) return null;
                try { using (var ic = Icon.FromHandle(h[0])) return ic.ToBitmap(); }
                finally { DestroyIcon(h[0]); }
            }
            catch { return null; }
        }

        // The exe icon at `size`, with any transparent border trimmed off. Some icons
        // (Juice, Pour) draw their tile a little inside the canvas; without trimming
        // that tile comes out smaller than the others and the card's shadow shows
        // around it as a dark ring.
        public static Bitmap ExeIcon(string path, int size)
        {
            var bmp = RawIcon(path, size);
            if (bmp == null) return null;
            try
            {
                var r = TileBounds(bmp);
                int pad = Math.Min(Math.Min(r.Left, r.Top), Math.Min(bmp.Width - r.Right, bmp.Height - r.Bottom));
                if (r.Width < 4 || pad < 2) return bmp;           // tile already fills the canvas

                // ask for a bigger copy so the trimmed tile still has full detail
                int big = Math.Min(256, (int)Math.Ceiling(size * (double)bmp.Width / r.Width) + 1);
                var src = big > size ? RawIcon(path, big) : null;
                if (src != null) { bmp.Dispose(); bmp = src; r = TileBounds(bmp); }

                var outb = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(outb))
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);   // no fading at the cut edges
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(bmp, new Rectangle(0, 0, size, size), r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, ia);
                }
                bmp.Dispose();
                return outb;
            }
            catch { return bmp; }
        }

        // the app's logo built into Minibar, for apps that aren't installed yet (no exe to take it from)
        public static Bitmap BuiltInIcon(string app, int size)
        {
            try
            {
                using (var st = typeof(Native).Assembly.GetManifestResourceStream("appicon." + app + ".png"))
                {
                    if (st == null) return null;
                    using (var src = new Bitmap(st))
                    {
                        var outb = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(outb))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.DrawImage(src, new Rectangle(0, 0, size, size));
                        }
                        return outb;
                    }
                }
            }
            catch { return null; }
        }

        // square box around the clearly visible pixels (alpha > 128), centred on them
        static Rectangle TileBounds(Bitmap b)
        {
            int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly,
                               System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[d.Stride];
                for (int y = 0; y < b.Height; y++)
                {
                    Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, d.Stride);
                    for (int x = 0; x < b.Width; x++)
                        if (row[x * 4 + 3] > 128)
                        {
                            if (x < x0) x0 = x; if (x > x1) x1 = x;
                            if (y < y0) y0 = y; if (y > y1) y1 = y;
                        }
                }
            }
            finally { b.UnlockBits(d); }
            if (x1 < 0) return new Rectangle(0, 0, b.Width, b.Height);
            int w = x1 - x0 + 1, h = y1 - y0 + 1, s = Math.Max(w, h);
            int cx = x0 + w / 2, cy = y0 + h / 2;
            int rx = Math.Max(0, Math.Min(b.Width - s, cx - s / 2)), ry = Math.Max(0, Math.Min(b.Height - s, cy - s / 2));
            return new Rectangle(rx, ry, Math.Min(s, b.Width), Math.Min(s, b.Height));
        }
    }
}
