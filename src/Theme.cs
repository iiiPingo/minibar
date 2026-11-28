using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Minibar
{
    static class Theme
    {
        // ---------------------------------------------------------------- palettes: Dark (default), OLED, Light (theme= in settings.ini)
        public static Palette P { get; private set; } = Palette.Dark;
        public static ThemeMode Mode => P.Mode;
        public static bool IsLight => P.Mode == ThemeMode.Light;

        public static Color Bg => P.Bg;               // page canvas
        public static Color Nav => P.Nav;             // left side menu
        public static Color Card => P.Card;           // buttons, active nav pill
        public static Color CardSoft => P.CardSoft;   // disabled button
        public static Color CardHi => P.CardHi;       // button hover
        public static Color CardHover => P.CardHover; // button pressed
        public static Color Field => P.Field;         // inputs, icon tiles
        public static Color Border => P.Border;
        public static Color Hairline => P.Hairline;   // 1px edge round buttons (OLED, Light); empty = none (Dark)
        public static Color Text => P.Text;
        public static Color Sub => P.Sub;
        public static Color Dim => P.Dim;
        public static Color Track => P.Track;         // slider track
        public static Color Red => P.Red;
        public static Color Tile => P.Tile;           // the big cards in the lists
        public static Color TileHover => P.TileHover;
        public static Color TileEdge => P.TileEdge;
        public static Color TileEdgeHover => P.TileEdgeHover;
        public static Color Divider => P.Divider;
        public static Color FocusEdge => P.FocusEdge;
        public static Color Pill => P.Pill;           // grey status pill
        public static Color Hover => P.Hover;         // ghost chip / switch hover
        public static Color Btn => P.Btn;             // secondary button on a card
        public static Color BtnHover => P.BtnHover;
        public static Color BtnEdge => P.BtnEdge;
        public static Color BtnOff => P.BtnOff;
        public static Color Chip => P.Chip;           // filled link chip
        public static Color ChipHover => P.ChipHover;
        public static Color ChipEdge => P.ChipEdge;   // ghost chip outline
        public static Color Note => P.Note;           // the (i) note box
        public static Color NoteEdge => P.NoteEdge;
        public static Color SideCard => P.SideCard;   // update card in the side menu
        public static Color SideEdge => P.SideEdge;
        public static Color SideLine => P.SideLine;
        public static Color NavHover => P.NavHover;
        public static Color Bar => P.Bar;             // battery bar track
        public static Color Knob => P.Knob;           // ring round the brightness knob
        public static Color Shadow => P.Shadow;
        public static Color MenuBack => P.MenuBack;
        public static Color MenuHi => P.MenuHi;
        public static Color MenuSep => P.MenuSep;
        // text drawn on an accent fill: always dark, every accent is bright
        public static readonly Color Ink = Color.FromArgb(22, 22, 22);

        // accent colour: honey by default, changeable in Settings
        public static readonly Color DefaultAccent = Color.FromArgb(242, 193, 78);   // #F2C14E
        public static Color Accent { get; private set; } = DefaultAccent;
        public static Color AccentHover { get; private set; } = Mix(DefaultAccent, Color.White, 0.22f);
        public static Color AccentSoft { get; private set; } = Color.FromArgb(40, DefaultAccent);
        // accent used as text or a thin glyph: darker on Light so it stays readable on white
        public static Color AccentText => IsLight ? Mix(Accent, Color.Black, 0.45f) : Accent;

        public static ThemeMode ParseMode(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "oled": return ThemeMode.Oled;
                case "light": return ThemeMode.Light;
                default: return ThemeMode.Dark;
            }
        }

        public static string ModeName(ThemeMode m) => m == ThemeMode.Oled ? "oled" : m == ThemeMode.Light ? "light" : "dark";

        public static void SetMode(ThemeMode m) => P = Palette.For(m);

        // a control's colour under the old palette -> the same role under the current one (for live switching)
        public static Color Remap(Color c, Palette from, Color accentTextBefore)
        {
            if (c.IsEmpty || c.A < 255) return c;
            int v = c.ToArgb();
            if (v == from.Bg.ToArgb()) return Bg;
            if (v == from.Nav.ToArgb()) return Nav;
            if (v == from.Text.ToArgb()) return Text;
            if (v == from.Sub.ToArgb()) return Sub;
            if (v == from.Dim.ToArgb()) return Dim;
            if (v == from.Field.ToArgb()) return Field;
            if (v == from.Red.ToArgb()) return Red;
            if (v == accentTextBefore.ToArgb()) return AccentText;
            return c;
        }

        // Windows 11 title bar: dark or light caption buttons, caption and border colours
        public static void ApplyTitleBar(IntPtr h, Color? darkCaption = null)
        {
            if (h == IntPtr.Zero) return;
            try
            {
                int dark = IsLight ? 0 : 1;
                Dwm(h, 20, ref dark, 4);              // DWMWA_USE_IMMERSIVE_DARK_MODE
                int cap, border;
                switch (Mode)
                {
                    case ThemeMode.Oled: cap = 0x000000; border = 0x000000; break;
                    case ThemeMode.Light: cap = 0x00F3F3F3; border = 0x00E0E0E0; break;
                    default: cap = ColorRef(darkCaption ?? Nav); border = unchecked((int)0xFFFFFFFF); break;   // Dark: as before, system border
                }
                Dwm(h, 35, ref cap, 4);               // DWMWA_CAPTION_COLOR
                Dwm(h, 34, ref border, 4);            // DWMWA_BORDER_COLOR
                int text = IsLight ? 0x001B1B1B : unchecked((int)0xFFFFFFFF);  // DWMWA_TEXT_COLOR: dark on Light, default otherwise
                Dwm(h, 36, ref text, 4);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")] static extern int Dwm(IntPtr h, int attr, ref int value, int size);

        public static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

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
        // fallback when Lexend can't be loaded
        static readonly string UiFamily = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        static readonly string UiSemiFamily = FontExists("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold"
            : FontExists("Segoe UI Semibold") ? "Segoe UI Semibold" : null;
        static readonly string UiDisplaySemi = FontExists("Segoe UI Variable Display Semibold") ? "Segoe UI Variable Display Semibold" : null;

        // Lexend (built into the exe): Medium for normal text, Bold for names, headings and buttons
        public static Font Ui(float size) => AppFonts.Make(AppFonts.Medium, size, false) ?? new Font(UiFamily, size);
        public static Font UiSemi(float size) => AppFonts.Make(AppFonts.Bold, size, true)
            ?? (UiSemiFamily != null ? new Font(UiSemiFamily, size) : new Font(UiFamily, size, FontStyle.Bold));
        public static Font UiTitle(float size) => AppFonts.Make(AppFonts.Bold, size, true)
            ?? (UiDisplaySemi != null ? new Font(UiDisplaySemi, size) : UiSemi(size));

        // Segoe Fluent Icons / MDL2 glyphs
        public const string GInfo = "\uE946", GPlay = "\uE768", GStop = "\uE71A", GFolder = "\uE8B7", GData = "\uE8F1", GLog = "\uE8A5",
            GSearch = "\uE721", GWarn = "\uE7BA", GRefresh = "\uE72C", GApps = "\uE71D", GSettings = "\uE713";

        static bool FontExists(string name)
        {
            try { using (var f = new Font(name, 10)) return f.Name == name; } catch { return false; }
        }

        public static void DrawIcon(Graphics g, Image img, Rectangle r, float opacity)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (opacity >= 0.999f) { g.DrawImage(img, r); return; }
            using (var ia = new System.Drawing.Imaging.ImageAttributes())
            {
                ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity });
                g.DrawImage(img, r, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
            }
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

    enum ThemeMode { Dark, Oled, Light }

    // every colour the UI draws with, per theme. Dark is the original look, value for value.
    sealed class Palette
    {
        public ThemeMode Mode;
        public Color Bg, Nav, Card, CardSoft, CardHi, CardHover, Field, Border, Hairline, Text, Sub, Dim, Track, Red,
            Tile, TileHover, TileEdge, TileEdgeHover, Divider, FocusEdge, Pill, Hover, Btn, BtnHover, BtnEdge, BtnOff,
            Chip, ChipHover, ChipEdge, Note, NoteEdge, SideCard, SideEdge, SideLine, NavHover, Bar, Knob, Shadow,
            MenuBack, MenuHi, MenuSep;

        static Color G(int v) => Color.FromArgb(v, v, v);
        static Color H(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

        public static readonly Palette Dark = new Palette
        {
            Mode = ThemeMode.Dark,
            Bg = G(32), Nav = G(27), Card = G(45), CardSoft = G(39), CardHi = G(52), CardHover = G(58), Field = G(52), Border = G(62),
            Hairline = Color.Empty, Text = G(242), Sub = G(170), Dim = G(120), Track = G(88), Red = Color.FromArgb(240, 96, 96),
            Tile = G(43), TileHover = G(47), TileEdge = G(56), TileEdgeHover = G(66), Divider = G(54), FocusEdge = G(78),
            Pill = G(52), Hover = G(58), Btn = G(56), BtnHover = G(66), BtnEdge = G(70), BtnOff = G(50),
            Chip = G(54), ChipHover = G(70), ChipEdge = G(64), Note = G(36), NoteEdge = G(60),
            SideCard = G(34), SideEdge = G(50), SideLine = G(48), NavHover = G(36), Bar = G(58), Knob = G(69),
            Shadow = Color.FromArgb(18, 0, 0, 0), MenuBack = G(40), MenuHi = G(60), MenuSep = G(64),
        };

        // Discord Onyx style: pure black canvas, near-black raised surfaces, #1F1F24 hairlines
        public static readonly Palette Oled = new Palette
        {
            Mode = ThemeMode.Oled,
            Bg = H(0x000000), Nav = H(0x000000), Card = H(0x131316), CardSoft = H(0x0D0D10), CardHi = H(0x1A1A1E), CardHover = H(0x202026),
            Field = H(0x131316), Border = H(0x1F1F24), Hairline = H(0x1F1F24), Text = G(242), Sub = G(170), Dim = G(120), Track = H(0x2A2A30),
            Red = Color.FromArgb(240, 96, 96),
            Tile = H(0x0D0D10), TileHover = H(0x111114), TileEdge = H(0x1F1F24), TileEdgeHover = H(0x2C2C33), Divider = H(0x1F1F24), FocusEdge = H(0x3A3A42),
            Pill = H(0x131316), Hover = H(0x1A1A1E), Btn = H(0x131316), BtnHover = H(0x1C1C21), BtnEdge = H(0x1F1F24), BtnOff = H(0x0D0D10),
            Chip = H(0x131316), ChipHover = H(0x202026), ChipEdge = H(0x1F1F24), Note = H(0x131316), NoteEdge = H(0x1F1F24),
            SideCard = H(0x0D0D10), SideEdge = H(0x1F1F24), SideLine = H(0x1F1F24), NavHover = H(0x0D0D10), Bar = H(0x1F1F24), Knob = H(0x2A2A30),
            Shadow = Color.FromArgb(0, 0, 0, 0), MenuBack = H(0x0D0D10), MenuHi = H(0x1C1C21), MenuSep = H(0x1F1F24),
        };

        // Windows 11 Fluent light: #F3F3F3 canvas, white raised surfaces, #E0E0E0 hairlines, #1B1B1B / #5C5C5C text
        public static readonly Palette Light = new Palette
        {
            Mode = ThemeMode.Light,
            Bg = H(0xF3F3F3), Nav = H(0xF3F3F3), Card = H(0xFFFFFF), CardSoft = H(0xF9F9F9), CardHi = H(0xF9F9F9), CardHover = H(0xF0F0F0),
            Field = H(0xFFFFFF), Border = H(0xE0E0E0), Hairline = H(0xE0E0E0), Text = H(0x1B1B1B), Sub = H(0x5C5C5C), Dim = H(0x767676), Track = H(0xC8C8C8),
            Red = H(0xC42B1C),
            Tile = H(0xFFFFFF), TileHover = H(0xF9F9F9), TileEdge = H(0xE0E0E0), TileEdgeHover = H(0xD0D0D0), Divider = H(0xEBEBEB), FocusEdge = H(0xA0A0A0),
            Pill = H(0xF0F0F0), Hover = H(0xF0F0F0), Btn = H(0xF9F9F9), BtnHover = H(0xF0F0F0), BtnEdge = H(0xE0E0E0), BtnOff = H(0xF3F3F3),
            Chip = H(0xF3F3F3), ChipHover = H(0xEAEAEA), ChipEdge = H(0xE0E0E0), Note = H(0xF9F9F9), NoteEdge = H(0xE0E0E0),
            SideCard = H(0xFFFFFF), SideEdge = H(0xE0E0E0), SideLine = H(0xE0E0E0), NavHover = H(0xEAEAEA), Bar = H(0xE0E0E0), Knob = H(0xFFFFFF),
            Shadow = Color.FromArgb(12, 0, 0, 0), MenuBack = H(0xFFFFFF), MenuHi = H(0xF0F0F0), MenuSep = H(0xE0E0E0),
        };

        public static Palette For(ThemeMode m) => m == ThemeMode.Oled ? Oled : m == ThemeMode.Light ? Light : Dark;
    }

    // ---------------------------------------------------------------- Lexend, built into the exe (nothing installed on the PC)
    // Like Fizz's AppFonts: one PrivateFontCollection per file (GDI+ Font objects) plus AddFontMemResourceEx
    // (so TextRenderer / GDI finds the same faces). The memory stays allocated for the life of the process.
    // Lexend-Medium.ttf is family "Lexend Medium" (Regular style); Lexend-Bold.ttf is family "Lexend" with only
    // a Bold face, so it must be created with FontStyle.Bold. Anything failing: null, and Theme falls back to Segoe UI.
    static class AppFonts
    {
        public static FontFamily Medium, Bold;
        static readonly System.Drawing.Text.PrivateFontCollection[] pfc =
            { new System.Drawing.Text.PrivateFontCollection(), new System.Drawing.Text.PrivateFontCollection() };

        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pb, uint cb, IntPtr pdv, ref uint pcFonts);

        static AppFonts()
        {
            try
            {
                bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
                var names = new[] { "fonts.Lexend-Medium.ttf", "fonts.Lexend-Bold.ttf" };
                for (int i = 0; i < names.Length; i++)
                {
                    byte[] data;
                    using (var st = typeof(AppFonts).Assembly.GetManifestResourceStream(names[i]))
                    {
                        if (st == null) return;
                        data = new byte[st.Length];
                        int read = 0;
                        while (read < data.Length) { int n = st.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
                    }
                    IntPtr mem = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length);   // never freed: GDI and GDI+ read from it
                    System.Runtime.InteropServices.Marshal.Copy(data, 0, mem, data.Length);
                    pfc[i].AddMemoryFont(mem, data.Length);
                    if (windows)
                    {
                        uint count = 0;
                        if (AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref count) == IntPtr.Zero) return;
                    }
                }
                if (pfc[0].Families.Length == 0 || pfc[1].Families.Length == 0) return;
                Medium = pfc[0].Families[0];
                Bold = pfc[1].Families[0];
            }
            catch { Medium = Bold = null; }
        }

        // render harness only (Mono doesn't draw memory fonts in controls): use the installed families instead
        public static void UseInstalled(string medium, string bold)
        {
            try { Medium = new FontFamily(medium); Bold = new FontFamily(bold); } catch { }
        }

        // the face each file has: Medium as Regular, Bold as Bold (new Font("Lexend", size) would throw: no Regular face)
        public static Font Make(FontFamily f, float size, bool bold)
        {
            if (f == null) return null;
            try
            {
                var want = bold ? FontStyle.Bold : FontStyle.Regular;
                var other = bold ? FontStyle.Regular : FontStyle.Bold;
                var style = f.IsStyleAvailable(want) ? want : f.IsStyleAvailable(other) ? other : (FontStyle?)null;
                return style == null ? null : new Font(f, size, style.Value);
            }
            catch { return null; }
        }
    }

    // ---------------------------------------------------------------- dark menu
    class DarkColors : ProfessionalColorTable
    {
        static Color Back => Theme.MenuBack;
        static Color Hi => Theme.MenuHi;
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
        public override Color SeparatorDark => Theme.MenuSep;
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
