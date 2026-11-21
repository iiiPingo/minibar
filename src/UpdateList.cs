using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace Minibar
{
    // Updates page: one row per app (and Minibar itself) with what you have, what GitHub has, and what you can do.
    class UpdateList : MbControl
    {
        List<AppInfo> rows = new List<AppInfo>();
        public event Action<AppInfo, string> Act;
        readonly Font fName, fSmall, fPill, fBtn, fChip, fLetter;
        readonly List<Tuple<Rectangle, string, AppInfo>> hits = new List<Tuple<Rectangle, string, AppInfo>>();
        string hover;
        int contentH;
        readonly SmoothScroller sc;
        int scroll => sc.Value;
        public Func<AppInfo, bool> AutoOn;

        public UpdateList(float scale) : base(scale)
        {
            sc = new SmoothScroller(this, () => contentH - Height);
            sc.S = v => S(v);
            fName = Theme.UiTitle(11.5f);
            fSmall = Theme.Ui(9f);
            fPill = Theme.Ui(8.75f);
            fBtn = Theme.UiSemi(9.5f);
            fChip = Theme.Ui(9f);
            fLetter = Theme.UiTitle(13f);
        }

        public void SetRows(List<AppInfo> list) { rows = list; Invalidate(); }

        const TextFormatFlags Measure = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        int TextW(Graphics g, string t, Font f) => TextRenderer.MeasureText(g, t, f, new Size(int.MaxValue, int.MaxValue), Measure).Width;
        int RowH => S(84);

        static readonly Color RowBg = Color.FromArgb(43, 43, 43), RowEdge = Color.FromArgb(56, 56, 56);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);
            hits.Clear();
            int y = -scroll, w = Width - S(12);
            foreach (var a in rows)
            {
                var r = new Rectangle(0, y, w, RowH);
                if (r.Bottom >= 0 && r.Top <= Height) DrawRow(g, a, r);
                y += RowH + S(8);
            }
            contentH = y + scroll;
            sc.DrawBar(g);
        }

        void Hit(Rectangle r, string id, AppInfo a) => hits.Add(Tuple.Create(r, a.Def.Name + "#" + id, a));
        bool Hov(AppInfo a, string id) => hover == a.Def.Name + "#" + id;

        void DrawRow(Graphics g, AppInfo a, Rectangle r)
        {
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            Theme.Fill(g, rf, RowBg, S(12));
            Theme.Stroke(g, rf, RowEdge, S(12), Math.Max(1, s));

            var ir = new Rectangle(r.X + S(18), r.Y + (r.Height - S(36)) / 2, S(36), S(36));
            if (a.Def.Self) Theme.DrawLogo(g, ir);
            else if (a.Found && a.Icon != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(a.Icon, ir); }
            else { Theme.Fill(g, ir, Theme.Field, S(9)); TextRenderer.DrawText(g, a.Def.Name.Substring(0, 1), fLetter, ir, Theme.Sub, Ux.C); }

            int x = ir.Right + S(16);
            int nw = TextW(g, a.Def.Name, fName) + S(2);
            TextRenderer.DrawText(g, a.Def.Name, fName, new Rectangle(x, r.Y + S(16), nw, S(26)), Theme.Text, Ux.L);

            // status pill
            var st = a.Found ? Updater.State(a) : UpdState.Unknown;
            string pill; bool accent = false;
            switch (st)
            {
                case UpdState.UpToDate: pill = "Up to date"; break;
                case UpdState.Available: pill = "Update available"; accent = true; break;
                case UpdState.Ahead: pill = "Newer than GitHub"; break;
                case UpdState.NoVersion: pill = "No version number"; break;
                case UpdState.NotOnGitHub: pill = "Not on GitHub yet"; break;
                case UpdState.Problem: pill = "Couldn't check"; break;
                default: pill = a.Found ? "Not checked yet" : "Not found"; break;
            }
            int pw = TextW(g, pill, fPill) + S(22);
            var pr = new Rectangle(x + nw + S(10), r.Y + S(18), pw, S(22));
            Theme.Fill(g, pr, accent ? Color.FromArgb(40, Theme.Accent) : Color.FromArgb(54, 54, 54), pr.Height / 2f);
            TextRenderer.DrawText(g, pill, fPill, pr, accent ? Theme.Accent : Theme.Sub, Ux.C);

            // right side, laid out from the right edge
            int right = r.Right - S(16), cy = r.Y + (r.Height - S(32)) / 2;
            {
                // auto-update switch
                bool on = AutoOn?.Invoke(a) ?? false;
                string tl = "Auto";
                int tw = S(40) + S(8) + TextW(g, tl, fChip);
                var tr = new Rectangle(right - tw, cy, tw, S(32));
                var sw = new RectangleF(tr.X, cy + S(6), S(40), S(20));
                bool th = Hov(a, "auto");
                if (on) Theme.Fill(g, sw, th ? Theme.AccentHover : Theme.Accent, sw.Height / 2);
                else { if (th) Theme.Fill(g, sw, Color.FromArgb(58, 58, 58), sw.Height / 2); Theme.Stroke(g, sw, th ? Theme.Text : Theme.Sub, sw.Height / 2, Math.Max(1, s)); }
                float d = on ? S(12) : S(10);
                float kx = on ? sw.Right - S(4) - d : sw.X + S(5);
                Theme.Disc(g, new PointF(kx + d / 2, sw.Y + sw.Height / 2), d / 2, on ? Color.FromArgb(24, 24, 24) : Theme.Sub);
                TextRenderer.DrawText(g, tl, fChip, new Rectangle(tr.X + S(48), cy, tw - S(48), S(32)), Theme.Sub, Ux.L);
                Hit(tr, "auto", a);
                right = tr.X - S(16);
            }
            if (a.Busy != null)
            {
                int bw = Math.Max(S(120), TextW(g, a.Busy, fBtn) + S(30));
                var br = new Rectangle(right - bw, cy, bw, S(32));
                Theme.Fill(g, br, Color.FromArgb(50, 50, 50), S(7));
                TextRenderer.DrawText(g, a.Busy, fBtn, br, Theme.Sub, Ux.C);
                right = br.X - S(8);
            }
            else if (st == UpdState.Available && !a.Updating)
            {
                bool h = Hov(a, "update");
                string t = a.Queued ? (h ? "Cancel update" : "Updates when you close " + a.Def.Name) : "Update to v" + a.Rel.Label;
                int bw = TextW(g, a.Queued ? "Updates when you close " + a.Def.Name : t, fBtn) + S(32);
                var br = new Rectangle(right - bw, cy, bw, S(32));
                if (a.Queued) Theme.Fill(g, br, Color.FromArgb(h ? 60 : 34, Theme.Accent), S(7));
                else Theme.Fill(g, br, h ? Theme.AccentHover : Theme.Accent, S(7));
                TextRenderer.DrawText(g, t, fBtn, br, a.Queued ? Theme.Accent : Color.FromArgb(22, 22, 22), Ux.C);
                Hit(br, "update", a);
                right = br.X - S(8);
            }
            if (a.Backup != null && a.Busy == null)
                right -= Chip(g, a, "rollback", "Restore " + (Updater.BackupLabel(a.Backup) ?? "previous"), right, cy) + S(4);
            if (a.Rel?.Repo != null && a.Rel.Problem != "missing")
                right -= Chip(g, a, "notes", "What's new", right, cy) + S(4);

            // second line: what you have and what GitHub has (or the problem)
            string have = !a.Found ? "Not found on this PC" : a.Version != null ? "You have v" + a.Version : "Your copy has no version number";
            if (a.Def.Name == "Juice" && a.Found && a.Version == null && a.Rel?.Tag != null) have = "No version number yet, so Minibar can't compare";
            string gh;
            if (a.Error != null && DateTime.Now < a.ErrorUntil) gh = null;
            else if (a.Rel == null) gh = "Not checked yet";
            else if (a.Rel.Problem == "missing") gh = "No releases at github.com/iiiPingo/" + (a.Def.Repos?.FirstOrDefault() ?? a.Def.Name.ToLowerInvariant());
            else if (a.Rel.Problem != null) gh = a.Rel.Problem;
            else gh = "GitHub has v" + a.Rel.Label + (a.Rel.Published != null ? " (" + Updater.Nice(a.Rel.Published) + ")" : "");
            var l2 = Rectangle.FromLTRB(x, r.Y + S(44), Math.Max(x + S(40), right - S(8)), r.Y + S(66));
            if (gh == null) TextRenderer.DrawText(g, a.Error, fSmall, l2, Theme.Red, Ux.L);
            else if (a.Note != null && DateTime.Now < a.NoteUntil) TextRenderer.DrawText(g, a.Note, fSmall, l2, Theme.Accent, Ux.L);
            else TextRenderer.DrawText(g, have + "   ·   " + gh, fSmall, l2, Theme.Dim, Ux.L);
        }

        // draws a ghost chip whose right edge is at `right`; returns its width
        int Chip(Graphics g, AppInfo a, string id, string text, int right, int y)
        {
            int w = TextW(g, text, fChip) + S(24);
            var r = new Rectangle(right - w, y, w, S(32));
            bool hv = Hov(a, id);
            if (hv) Theme.Fill(g, r, Color.FromArgb(58, 58, 58), S(7));
            else Theme.Stroke(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Color.FromArgb(64, 64, 64), S(7), Math.Max(1, s));
            TextRenderer.DrawText(g, text, fChip, r, hv ? Theme.Text : Theme.Sub, Ux.C);
            Hit(r, id, a);
            return w;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); sc.Fit(); }

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
            string id = HitAt(e.Location)?.Item2;
            if (id != hover) { hover = id; Cursor = id != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); sc.MouseDown(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); sc.CaptureLost(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); sc.Leave(); if (hover != null) { hover = null; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (sc.MouseUp(e)) return;
            if (e.Button != MouseButtons.Left) return;
            var h = HitAt(e.Location);
            if (h != null) Act?.Invoke(h.Item3, h.Item2.Substring(h.Item2.IndexOf('#') + 1));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            sc.Wheel(e.Delta, S(92));
        }
    }
}
