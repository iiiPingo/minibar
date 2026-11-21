using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Minibar
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(32, 32, 32);
        public static readonly Color Card = Color.FromArgb(45, 45, 45);
        public static readonly Color CardSoft = Color.FromArgb(39, 39, 39);
        public static readonly Color CardHover = Color.FromArgb(58, 58, 58);
        public static readonly Color Field = Color.FromArgb(52, 52, 52);
        public static readonly Color Border = Color.FromArgb(62, 62, 62);
        public static readonly Color Text = Color.FromArgb(242, 242, 242);
        public static readonly Color Sub = Color.FromArgb(170, 170, 170);
        public static readonly Color Dim = Color.FromArgb(120, 120, 120);
        public static readonly Color Track = Color.FromArgb(88, 88, 88);
        public static readonly Color Nav = Color.FromArgb(27, 27, 27);
        public static readonly Color Red = Color.FromArgb(240, 96, 96);

        // accent colour: honey by default, changeable in Settings
        public static readonly Color DefaultAccent = Color.FromArgb(242, 193, 78);   // #F2C14E
        public static Color Accent { get; private set; } = DefaultAccent;
        public static Color AccentHover { get; private set; } = Mix(DefaultAccent, Color.White, 0.22f);
        public static Color AccentSoft { get; private set; } = Color.FromArgb(40, DefaultAccent);

        // the family colours (the same swatches every app offers)
        public static readonly Color[] Presets =
        {
            DefaultAccent,                      // honey
            Color.FromArgb(217, 154, 98),       // Echoa cocoa
            Color.FromArgb(76, 168, 255),       // blue
            Color.FromArgb(61, 219, 176),       // mint
            Color.FromArgb(95, 208, 104),       // Juice green
            Color.FromArgb(255, 138, 61),       // Pulp tangerine
            Color.FromArgb(208, 100, 74),       // Pour terracotta
            Color.FromArgb(242, 95, 92),        // cherry red
            Color.FromArgb(255, 92, 158),       // Fizz pink
            Color.FromArgb(176, 124, 255),      // ube
            Color.FromArgb(200, 200, 200),      // silver
        };

        public static void SetAccent(Color c)
        {
            c = Color.FromArgb(255, c.R, c.G, c.B);
            Accent = c;
            AccentHover = Mix(c, Color.White, 0.22f);
            AccentSoft = Color.FromArgb(40, c);
        }

        public static Color Mix(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        public static string Hex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        public static bool TryHex(string s, out Color c)
        {
            c = DefaultAccent;
            s = (s ?? "").Trim().TrimStart('#');
            if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int v)) return false;
            c = Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
            return true;
        }

        public static readonly string IconFont = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        static readonly string UiFamily = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        static readonly string UiSemiFamily = FontExists("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold"
            : FontExists("Segoe UI Semibold") ? "Segoe UI Semibold" : null;
        public static Font Ui(float size) => new Font(UiFamily, size);
        public static Font UiSemi(float size) => UiSemiFamily != null ? new Font(UiSemiFamily, size) : new Font(UiFamily, size, FontStyle.Bold);
        static readonly string UiDisplaySemi = FontExists("Segoe UI Variable Display Semibold") ? "Segoe UI Variable Display Semibold" : null;
        public static Font UiTitle(float size) => UiDisplaySemi != null ? new Font(UiDisplaySemi, size) : UiSemi(size);

        // Segoe Fluent Icons / MDL2 glyphs
        public const string GInfo = "\uE946", GPlay = "\uE768", GStop = "\uE71A", GFolder = "\uE8B7", GData = "\uE8F1", GLog = "\uE8A5",
            GSearch = "\uE721", GWarn = "\uE7BA", GRefresh = "\uE72C", GApps = "\uE71D", GSettings = "\uE713";

        static bool FontExists(string name)
        {
            try { using (var f = new Font(name, 10)) return f.Name == name; } catch { return false; }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, Color c, float rad)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, Color c, float rad, float w = 1)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        public static void Disc(Graphics g, PointF c, float r, Color col)
        {
            using (var b = new SolidBrush(col)) g.FillEllipse(b, c.X - r, c.Y - r, 2 * r, 2 * r);
        }

        // ---------------------------------------------------------------- logo: the stocked minibar (from the embedded icon)
        static Icon appIcon;
        static Bitmap logo;
        public static Icon AppIcon
        {
            get
            {
                if (appIcon != null) return appIcon;
                try
                {
                    using (var st = typeof(Theme).Assembly.GetManifestResourceStream("minibar.ico"))
                        appIcon = new Icon(st);
                }
                catch { appIcon = SystemIcons.Application; }
                return appIcon;
            }
        }

        public static void DrawLogo(Graphics g, RectangleF r)
        {
            try
            {
                if (logo == null)
                    using (var st = typeof(Theme).Assembly.GetManifestResourceStream("minibar.ico"))
                    using (var ic = new Icon(st, new Size(64, 64)))
                        logo = ic.ToBitmap();
                var old = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, r);
                g.InterpolationMode = old;
            }
            catch { Fill(g, r, Card, r.Width * 0.24f); }
        }
    }

    // ---------------------------------------------------------------- dark menu
    class DarkColors : ProfessionalColorTable
    {
        static readonly Color Back = Color.FromArgb(40, 40, 40), Hi = Color.FromArgb(60, 60, 60);
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Hi;
        public override Color MenuItemSelected => Hi;
        public override Color MenuItemSelectedGradientBegin => Hi;
        public override Color MenuItemSelectedGradientEnd => Hi;
        public override Color MenuItemPressedGradientBegin => Hi;
        public override Color MenuItemPressedGradientEnd => Hi;
        public override Color SeparatorDark => Color.FromArgb(64, 64, 64);
        public override Color SeparatorLight => Back;
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dim;
            base.OnRenderItemText(e);
        }
    }
}
