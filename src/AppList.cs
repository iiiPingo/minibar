using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Minibar
{
    // The app cards: icon, name, running state, version, path, Start/Stop, start with Windows, folder links.
    class AppList : MbControl
    {
        List<AppInfo> apps = new List<AppInfo>();
        public event Action<AppInfo, string> Act;   // app, action id
        readonly Font fName, fBody, fSmall, fChip, fBtn, fIcon, fLetter, fPill, fBtnIcon, fChipIcon, fNoticeIcon, fH, fPillSemi, fPillIcon, fInfoIcon;
        readonly List<Tuple<Rectangle, string, AppInfo>> hits = new List<Tuple<Rectangle, string, AppInfo>>();
        string hover;
        int contentH;
        readonly SmoothScroller sc;
        int scroll => sc.Value;

        public AppList(float scale) : base(scale)
        {
            sc = new SmoothScroller(this, () => contentH - Height);
            sc.S = v => S(v);
            fName = Theme.UiTitle(12.5f);
            fBody = Theme.Ui(10f);
            fSmall = Theme.Ui(9f);
            fChip = Theme.Ui(9f);
            fBtn = Theme.UiSemi(9.5f);
            fIcon = new Font(Theme.IconFont, 10f);
            fLetter = Theme.UiTitle(15f);
            fPill = Theme.Ui(8.75f);
            fBtnIcon = new Font(Theme.IconFont, 9f);
            fChipIcon = new Font(Theme.IconFont, 10f);
            fNoticeIcon = new Font(Theme.IconFont, 13f);
            fH = Theme.UiSemi(10f);
            fPillSemi = Theme.UiSemi(9f);
            fPillIcon = new Font(Theme.IconFont, 8.5f);
            fInfoIcon = new Font(Theme.IconFont, 10f);
        }

        public void SetApps(List<AppInfo> list) { apps = list; Invalidate(); }

        const TextFormatFlags Measure = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        int TextW(Graphics g, string t, Font f) => TextRenderer.MeasureText(g, t, f, new Size(int.MaxValue, int.MaxValue), Measure).Width;

        bool ShowFizzNotice(AppInfo a) => a.Def.Name == "Fizz" && a.Found && !a.Running;

        string NoticeTitle(AppInfo a) => a.PawnIO == false ? "Install PawnIO before you start Fizz" : "Before you start Fizz";
        string NoticeText(AppInfo a) =>
            (a.PawnIO == false ? "Fizz needs this driver to read temperatures and clocks. Installing it takes one admin prompt.\n" : "")
            + "Fizz asks for admin when it starts. The overlay doesn't touch your games, but FACEIT's anti-cheat may block the PawnIO driver, so close Fizz before FACEIT matches. "
            + "(FACEIT is a separate matchmaking service, mostly for CS2, with its own strict anti-cheat. Normal Steam matches are fine.)\n"
            + "Both helpers already come in Fizz's tools folder; the official downloads are below if you ever need them.";

        int NoticeTextW(int width) => width - S(16 + 32 + 18);

        int NoticeH(Graphics g, AppInfo a, int width)
        {
            var sz = TextRenderer.MeasureText(g, NoticeText(a), fSmall, new Size(NoticeTextW(width), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return S(14) + S(22) + sz.Height + S(10) + S(44);   // + the button row
        }

        const int Base = 134;
        int FizzExtra(Graphics g, AppInfo a, int width) => ShowFizzNotice(a) ? NoticeH(g, a, width - S(36)) + S(18) : 0;
        int CardH(Graphics g, AppInfo a, int width) => S(Base) + FizzExtra(g, a, width) + (InfoOpen(a) ? InfoH(g, a, width - S(36)) + S(18) : 0);

        // the (i) note (Juice: which devices work), opened from the (i) next to the name
        readonly HashSet<string> infoOpen = new HashSet<string>();
        bool InfoOpen(AppInfo a) => a.Def.Info != null && infoOpen.Contains(a.Def.Name);
        public void ToggleInfo(AppInfo a) { if (!infoOpen.Remove(a.Def.Name)) infoOpen.Add(a.Def.Name); Invalidate(); }
        int InfoTextW(int width) => width - S(16 + 16);
        int InfoH(Graphics g, AppInfo a, int width)
        {
            var sz = TextRenderer.MeasureText(g, a.Def.Info, fSmall, new Size(InfoTextW(width), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return S(14) + S(24) + sz.Height + S(14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);
            hits.Clear();
            int y = -scroll, w = Width - S(12);
            foreach (var a in apps)
            {
                int h = CardH(g, a, w);
                var r = new Rectangle(0, y, w, h);
                hits.Add(Tuple.Create(r, a.Def.Name + "#card", a));   // first, so the buttons drawn on it win the hit test
                if (y + h >= 0 && y <= Height) DrawCard(g, a, r);
                y += h + S(12);
            }
            contentH = y + scroll;
            sc.DrawBar(g);
        }

        void Hit(Rectangle r, string id, AppInfo a) => hits.Add(Tuple.Create(r, a.Def.Name + "#" + id, a));
        bool uhq(AppInfo a) => Hov(a, "update");
        bool Hov(AppInfo a, string id) => hover == a.Def.Name + "#" + id;
        string hoverCard;

        static readonly Color CardBg = Color.FromArgb(43, 43, 43), CardBgHover = Color.FromArgb(47, 47, 47), CardEdge = Color.FromArgb(56, 56, 56), Divider = Color.FromArgb(54, 54, 54);

        void DrawCard(Graphics g, AppInfo a, Rectangle r)
        {
            bool hc = hoverCard == a.Def.Name;
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            Theme.Fill(g, rf, hc ? CardBgHover : CardBg, S(12));
            Theme.Stroke(g, rf, hc ? Color.FromArgb(66, 66, 66) : CardEdge, S(12), Math.Max(1, s));
            int x0 = r.X + S(18), top = r.Y;

            // icon with a soft shadow
            var ir = new Rectangle(x0, top + S(18), S(44), S(44));
            for (int i = 3; i >= 1; i--)
                Theme.Fill(g, new RectangleF(ir.X - i * 0.5f, ir.Y + S(2) + i * 0.6f, ir.Width + i, ir.Height + i * 0.4f), Color.FromArgb(18, 0, 0, 0), S(11) + i);
            if (a.Found && (a.Icon == null || a.IconFor != ir.Width || a.IconPath != a.Exe))
            {
                a.Icon?.Dispose();
                a.Icon = Native.ExeIcon(a.Exe, ir.Width);
                a.IconFor = ir.Width;
                a.IconPath = a.Exe;
            }
            if (a.Found && a.Icon != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(a.Icon, ir); }
            else
            {
                Theme.Fill(g, ir, Theme.Field, S(11));
                TextRenderer.DrawText(g, a.Def.Name.Substring(0, 1), fLetter, ir, Theme.Sub, Ux.C);
            }

            // name + status pill + version
            int nx = ir.Right + S(16);
            int nw = TextW(g, a.Def.Name, fName) + S(2);
            TextRenderer.DrawText(g, a.Def.Name, fName, new Rectangle(nx, top + S(16), nw, S(26)), Theme.Text, Ux.L);
            string state; Color pillBg, dot, stc; bool hollow = false;
            if (!a.Found) { state = "Not found"; pillBg = Color.FromArgb(52, 52, 52); dot = Theme.Dim; stc = Theme.Sub; hollow = true; }
            else if (a.Running) { state = "Running" + (a.Since != null ? "  ·  " + Uptime(DateTime.Now - a.Since.Value) : ""); pillBg = Color.FromArgb(34, Theme.Accent); dot = Theme.Accent; stc = Theme.Text; }
            else { state = "Not running"; pillBg = Color.FromArgb(52, 52, 52); dot = Theme.Sub; stc = Theme.Sub; hollow = true; }
            int pw = TextW(g, state, fPill) + S(30);
            var pill = new Rectangle(nx + nw + S(12), top + S(18), pw, S(22));
            Theme.Fill(g, pill, pillBg, pill.Height / 2f);
            var dc = new PointF(pill.X + S(11), pill.Y + pill.Height / 2f);
            Theme.Disc(g, dc, S(3.5f), dot);
            if (hollow) Theme.Disc(g, dc, S(2f), pillBg);
            TextRenderer.DrawText(g, state, fPill, new Rectangle(pill.X + S(20), pill.Y, pill.Width - S(24), pill.Height), stc, Ux.L);
            if (a.Def.Info != null)
            {
                var ir2 = new Rectangle(pill.Right + S(6), pill.Y - S(1), S(24), S(24));
                bool open = InfoOpen(a), ih = Hov(a, "info");
                if (open || ih) Theme.Fill(g, ir2, open ? Color.FromArgb(40, Theme.Accent) : Color.FromArgb(58, 58, 58), ir2.Height / 2f);
                // drawn by hand (a font glyph sits off-centre): ring, dot, stem
                Color ic = open ? Theme.Accent : ih ? Theme.Text : Theme.Sub;
                float cxi = ir2.X + ir2.Width / 2f, cyi = ir2.Y + ir2.Height / 2f, rr = S(7.5f);
                using (var pen = new Pen(ic, Math.Max(1f, s * 1.3f))) g.DrawEllipse(pen, cxi - rr, cyi - rr, rr * 2, rr * 2);
                Theme.Disc(g, new PointF(cxi, cyi - rr * 0.45f), Math.Max(0.9f, s * 1.05f), ic);
                using (var pen = new Pen(ic, Math.Max(1f, s * 1.5f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawLine(pen, cxi, cyi - rr * 0.08f, cxi, cyi + rr * 0.5f);
                Hit(ir2, "info", a);
            }

            string ver = !a.Found ? "" : a.Version != null ? "v" + a.Version : "version unknown";
            int vw = ver.Length > 0 ? TextW(g, ver, fSmall) : 0;
            if (ver.Length > 0)
                TextRenderer.DrawText(g, ver, fSmall, new Rectangle(r.Right - S(18) - vw - S(2), top + S(17), vw + S(2), S(24)), Theme.Dim, Ux.R);
            // a newer version on GitHub: accent pill next to the version
            if (Updater.CanUpdate(a) && a.Busy == null)
            {
                // queued: it installs the moment he closes the app (click again to cancel)
                string ut = a.Queued ? (uhq(a) ? "Cancel update" : "Updates when you close it") : "Update to v" + a.Rel.Label;
                int uw = TextW(g, a.Queued ? "Updates when you close it" : ut, fPillSemi) + S(36);   // same width while hovered, so it never flickers
                var ur = new Rectangle(r.Right - S(18) - vw - S(12) - uw, top + S(16), uw, S(26));
                bool uh = Hov(a, "update");
                Color ink = a.Queued ? Theme.Accent : Color.FromArgb(22, 22, 22);
                Theme.Fill(g, ur, a.Queued ? Color.FromArgb(uh ? 60 : 34, Theme.Accent) : uh ? Theme.AccentHover : Theme.Accent, ur.Height / 2f);
                TextRenderer.DrawText(g, a.Queued ? "\uE823" : "\uE896", fPillIcon, new Rectangle(ur.X + S(10), ur.Y, S(14), ur.Height), ink, Ux.C);   // Recent / Download
                TextRenderer.DrawText(g, ut, fPillSemi, new Rectangle(ur.X + S(28), ur.Y, uw - S(32), ur.Height), ink, Ux.L);
                Hit(ur, "update", a);
            }

            // second line: what it is · where it lives (or the last error for a few seconds)
            var l2 = new Rectangle(nx, top + S(44), r.Right - S(18) - nx, S(20));
            if (a.Error != null && DateTime.Now < a.ErrorUntil)
                TextRenderer.DrawText(g, a.Error, fSmall, l2, Theme.Red, Ux.L);
            else if (a.Note != null && DateTime.Now < a.NoteUntil)
                TextRenderer.DrawText(g, a.Note, fSmall, l2, Theme.Accent, Ux.L);
            else
            {
                // what the app is, then what it's best at (the exe's place is one click away under Folder)
                string more = !a.Found ? "Not on this PC, or somewhere Minibar doesn't look. Use Locate to point at " + a.Def.Name + ".exe."
                    : a.OtherCopy != null ? "Running from another copy: " + a.OtherCopy
                    : a.Def.Highlights ?? "";
                int bw = TextW(g, a.Def.Blurb, fSmall);
                TextRenderer.DrawText(g, a.Def.Blurb, fSmall, new Rectangle(l2.X, l2.Y, bw + S(2), l2.Height), Theme.Sub, Ux.L);
                // highlights: show as many whole ones as fit, never a word cut in half
                if (a.Found && a.OtherCopy == null && more.Contains("\u00b7"))
                {
                    var parts = more.Split(new[] { "\u00b7" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                    int room = l2.Right - (l2.X + bw + S(18));
                    while (parts.Count > 1 && TextW(g, string.Join("  \u00b7  ", parts), fSmall) > room) parts.RemoveAt(parts.Count - 1);
                    more = string.Join("  \u00b7  ", parts);
                }
                if (more.Length > 0)
                {
                    Theme.Disc(g, new PointF(l2.X + bw + S(9), l2.Y + l2.Height / 2f), S(1.5f), Theme.Dim);
                    TextRenderer.DrawText(g, more, fSmall, Rectangle.FromLTRB(l2.X + bw + S(18), l2.Y, l2.Right, l2.Bottom), Theme.Dim, Ux.L);
                }
            }

            // divider, then the action row
            using (var pen = new Pen(Divider, Math.Max(1, s))) g.DrawLine(pen, r.X + S(18), top + S(80), r.Right - S(18), top + S(80));
            int by = top + S(92);
            int bx = x0;
            if (!a.Found)
            {
                bx += Button(g, a, "locate", Theme.GSearch, "Locate…", bx, by, true) + S(8);
                Chip(g, a, "rescan", Theme.GRefresh, "Look again", bx, by);
            }
            else
            {
                if (a.Busy != null) bx += Button(g, a, "busy", null, a.Busy, bx, by, false, disabled: true) + S(18);
                else if (a.Running) bx += Button(g, a, "stop", Theme.GStop, "Stop", bx, by, false) + S(18);
                else bx += Button(g, a, "start", Theme.GPlay, "Start", bx, by, true) + S(18);

                // start with Windows (Windows 11 style switch)
                bool can = a.Startup.HasValue && a.Busy == null;
                string tl = a.Startup.HasValue ? "Start with Windows" : "Start with Windows: set in Fizz";
                int tw = S(40) + S(10) + TextW(g, tl, fBody) + S(4);
                var tr = new Rectangle(bx, by, tw, S(32));
                var sw = new RectangleF(bx, by + S(6), S(40), S(20));
                bool on = a.Startup == true;
                bool th = Hov(a, "startup") && can;
                if (on) Theme.Fill(g, sw, can ? (th ? Theme.AccentHover : Theme.Accent) : Theme.Dim, sw.Height / 2);
                else
                {
                    if (th) Theme.Fill(g, sw, Color.FromArgb(58, 58, 58), sw.Height / 2);
                    Theme.Stroke(g, sw, can ? (th ? Theme.Text : Theme.Sub) : Theme.Dim, sw.Height / 2, Math.Max(1, s));
                }
                float d = on ? S(12) : S(10);
                float kx = on ? sw.Right - S(4) - d : sw.X + S(5);
                if (th) d += S(1);
                Theme.Disc(g, new PointF(kx + d / 2, sw.Y + sw.Height / 2), d / 2, on ? Color.FromArgb(24, 24, 24) : can ? Theme.Sub : Theme.Dim);
                TextRenderer.DrawText(g, tl, fBody, new Rectangle(bx + S(50), by, tw - S(50), S(32)), can ? Theme.Text : Theme.Dim, Ux.L);
                if (can) Hit(tr, "startup", a);

                // folder links, right-aligned; on a narrow window the least-used ones go first
                var chips = new List<Tuple<string, string, string>>
                {
                    Tuple.Create("folder", Theme.GFolder, "Folder"), Tuple.Create("data", Theme.GData, "Data"),
                    Tuple.Create("log", Theme.GLog, "Log"), Tuple.Create("locate", Theme.GSearch, "Locate"),
                };
                string[] dropOrder = { "locate", "data", "log", "folder" };
                int ChipsW() => chips.Sum(c => ChipW(g, c.Item3)) + S(4) * Math.Max(0, chips.Count - 1);
                foreach (var dropId in dropOrder)
                {
                    if (chips.Count == 0 || r.Right - S(14) - ChipsW() >= tr.Right + S(10)) break;
                    chips.RemoveAll(c => c.Item1 == dropId);
                }
                int cx = r.Right - S(14) - ChipsW();
                foreach (var c in chips)
                {
                    int cw = Chip(g, a, c.Item1, c.Item2, c.Item3, cx, by);
                    if (c.Item1 == "log" && a.RecentCrash)   // a crash in the last week: red dot on Log
                    {
                        Theme.Disc(g, new PointF(cx + cw - S(7), by + S(8)), S(4.5f), hc ? CardBgHover : CardBg);
                        Theme.Disc(g, new PointF(cx + cw - S(7), by + S(8)), S(3.2f), Theme.Red);
                    }
                    cx += cw + S(4);
                }
            }

            // the (i) note
            if (InfoOpen(a))
            {
                int ih2 = InfoH(g, a, r.Width - S(36));
                var nr = new Rectangle(r.X + S(18), top + S(Base) + FizzExtra(g, a, r.Width), r.Width - S(36), ih2);
                Theme.Fill(g, nr, Color.FromArgb(36, 36, 36), S(10));
                Theme.Stroke(g, new RectangleF(nr.X + 0.5f, nr.Y + 0.5f, nr.Width - 1, nr.Height - 1), Color.FromArgb(60, 60, 60), S(10), Math.Max(1, s));
                TextRenderer.DrawText(g, a.Def.InfoTitle ?? "Good to know", fH, new Rectangle(nr.X + S(16), nr.Y + S(14), nr.Width - S(32), S(22)), Theme.Text, Ux.L);
                TextRenderer.DrawText(g, a.Def.Info, fSmall, new Rectangle(nr.X + S(16), nr.Y + S(40), InfoTextW(nr.Width), ih2 - S(40)), Theme.Sub,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }

            // Fizz: what it needs, shown before he starts it
            if (ShowFizzNotice(a))
            {
                int nh = NoticeH(g, a, r.Width - S(36));
                var nr = new Rectangle(r.X + S(18), top + S(Base), r.Width - S(36), nh);
                Theme.Fill(g, nr, Color.FromArgb(24, Theme.Accent), S(10));
                Theme.Stroke(g, new RectangleF(nr.X + 0.5f, nr.Y + 0.5f, nr.Width - 1, nr.Height - 1), Color.FromArgb(60, Theme.Accent), S(10), Math.Max(1, s));
                bool warn = a.PawnIO == false;
                TextRenderer.DrawText(g, warn ? Theme.GWarn : Theme.GInfo, fNoticeIcon, new Rectangle(nr.X + S(14), nr.Y + S(14), S(22), S(22)), Theme.Accent, Ux.C);
                int tx = nr.X + S(48), tw2 = NoticeTextW(nr.Width);
                TextRenderer.DrawText(g, NoticeTitle(a), fH, new Rectangle(tx, nr.Y + S(14), tw2, S(22)), Theme.Text, Ux.L);
                TextRenderer.DrawText(g, NoticeText(a), fSmall, new Rectangle(tx, nr.Y + S(38), tw2, nh - S(38)), Theme.Sub,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                int bx2 = tx, by2 = nr.Bottom - S(44);
                if (a.PawnIO == false && a.PawnSetup != null)
                    bx2 += Button(g, a, "pawn", null, "Install PawnIO", bx2, by2, true, h: S(30)) + S(8);
                bx2 += LinkChip(g, a, "link:pawnio", "PawnIO  \u00b7  pawnio.eu", bx2, by2) + S(6);
                LinkChip(g, a, "link:presentmon", "PresentMon  \u00b7  GitHub", bx2, by2);
            }
        }

        static string Uptime(TimeSpan t) =>
            t.TotalMinutes < 1 ? "just started" : t.TotalHours < 1 ? (int)t.TotalMinutes + " min"
            : t.TotalDays < 1 ? (int)t.TotalHours + " h " + t.Minutes + " m" : (int)t.TotalDays + " d " + t.Hours + " h";

        int Button(Graphics g, AppInfo a, string id, string glyph, string text, int x, int y, bool primary, bool disabled = false, int h = 0)
        {
            if (h == 0) h = S(32);
            int gw = glyph != null ? S(20) : 0;
            int w = Math.Max(S(92), TextW(g, text, fBtn) + gw + S(34));
            var r = new Rectangle(x, y, w, h);
            bool hv = !disabled && Hov(a, id);
            Color bg = disabled ? Color.FromArgb(50, 50, 50) : primary ? (hv ? Theme.AccentHover : Theme.Accent) : (hv ? Color.FromArgb(66, 66, 66) : Color.FromArgb(56, 56, 56));
            Color fg = disabled ? Theme.Sub : primary ? Color.FromArgb(22, 22, 22) : Theme.Text;
            Theme.Fill(g, r, bg, S(7));
            if (!primary && !disabled) Theme.Stroke(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Color.FromArgb(70, 70, 70), S(7), Math.Max(1, s));
            int tw = TextW(g, text, fBtn);
            int sx = r.X + (r.Width - tw - gw) / 2;
            if (glyph != null) TextRenderer.DrawText(g, glyph, fBtnIcon, new Rectangle(sx, r.Y, S(14), r.Height), fg, Ux.C);
            TextRenderer.DrawText(g, text, fBtn, new Rectangle(sx + gw, r.Y, tw + S(4), r.Height), fg, Ux.L);
            if (!disabled) Hit(r, id, a);
            return w;
        }

        // a filled chip that opens a web page (\uE8A7 = open in new window)
        int LinkChip(Graphics g, AppInfo a, string id, string text, int x, int y)
        {
            int w = TextW(g, text, fChip) + S(40);
            var r = new Rectangle(x, y, w, S(30));
            bool hv = Hov(a, id);
            Theme.Fill(g, r, hv ? Color.FromArgb(70, 70, 70) : Color.FromArgb(54, 54, 54), S(7));
            Color c = hv ? Theme.Text : Theme.Sub;
            TextRenderer.DrawText(g, text, fChip, new Rectangle(r.X + S(12), r.Y, w - S(34), r.Height), c, Ux.L);
            TextRenderer.DrawText(g, "\uE8A7", fChipIcon, new Rectangle(r.Right - S(26), r.Y, S(16), r.Height), c, Ux.C);
            Hit(r, id, a);
            return w;
        }

        int ChipW(Graphics g, string text) => TextW(g, text, fChip) + S(40);

        int Chip(Graphics g, AppInfo a, string id, string glyph, string text, int x, int y)
        {
            int w = ChipW(g, text);
            var r = new Rectangle(x, y, w, S(32));
            bool hv = Hov(a, id);
            if (hv) Theme.Fill(g, r, Color.FromArgb(58, 58, 58), S(7));
            Color c = hv ? Theme.Text : Theme.Sub;
            TextRenderer.DrawText(g, glyph, fChipIcon, new Rectangle(r.X + S(10), r.Y, S(16), r.Height), c, Ux.C);
            TextRenderer.DrawText(g, text, fChip, new Rectangle(r.X + S(30), r.Y, w - S(34), r.Height), c, Ux.L);
            Hit(r, id, a);
            return w;
        }

        Tuple<Rectangle, string, AppInfo> HitAt(Point p)
        {
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Item1.Contains(p)) return hits[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (sc.MouseMove(e))
            {
                if (hover != null) { hover = null; Invalidate(); }
                Cursor = Cursors.Default;
                return;
            }
            var h = HitAt(e.Location);
            string id = h?.Item2;
            string card = id == null ? null : id.Substring(0, id.IndexOf('#'));
            if (id != null && id.EndsWith("#card")) id = null;
            if (id != hover || card != hoverCard) { hover = id; hoverCard = card; Cursor = id != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); sc.MouseDown(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); sc.CaptureLost(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); sc.Leave(); if (hover != null || hoverCard != null) { hover = null; hoverCard = null; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (sc.MouseUp(e)) return;
            if (e.Button != MouseButtons.Left) return;
            var h = HitAt(e.Location);
            if (h == null || h.Item2.EndsWith("#card")) return;
            string id = h.Item2.Substring(h.Item2.IndexOf('#') + 1);
            Act?.Invoke(h.Item3, id);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            sc.Wheel(e.Delta, S(110));
        }

        public void MoveTest(Point p) => OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0));
        public void ClickTest(Point p) { OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0)); OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)); }
        public List<Tuple<Rectangle, string, AppInfo>> HitsForTest => hits;
        public string DebugScroll => contentH + "/" + Height + "/" + scroll;
        public void BarTest(int x, int y)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, x, y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        }
        public void MouseWheelTest(int delta) => OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 10, 10, delta));

        protected override void OnResize(EventArgs e) { base.OnResize(e); sc.Fit(); }
    }
}
