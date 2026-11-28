using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Minibar
{
    // The Displays page's cards: name, resolution, scale, refresh rate and a brightness slider each.
    // Owner-drawn like AppList. Slider drags are rate-limited (Coalescer) and the final value always goes on mouse-up.
    class DisplayList : MbControl
    {
        List<DisplayInfo> shown = new List<DisplayInfo>();
        public bool Loading = true;
        public event Action<DisplayInfo, int> Changed;     // brightness to apply (already rate-limited)

        readonly Font fName, fSub, fLabel, fValue, fPct, fIcon, fSun, fPill, fEmpty;
        readonly SmoothScroller sc;
        int contentH;
        readonly List<Tuple<Rectangle, DisplayInfo>> sliders = new List<Tuple<Rectangle, DisplayInfo>>();
        readonly Dictionary<string, Coalescer> co = new Dictionary<string, Coalescer>();
        readonly Timer tick = new Timer { Interval = 25 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        DisplayInfo drag, hoverSlider, focus;
        bool wheelPending;
        int lastWheelMs;

        static readonly Color CardBg = Color.FromArgb(43, 43, 43), CardEdge = Color.FromArgb(56, 56, 56);
        const int CardH = 164;

        public DisplayList(float scale) : base(scale)
        {
            sc = new SmoothScroller(this, () => contentH - Height);
            sc.S = v => S(v);
            SetStyle(ControlStyles.Selectable, true);
            fName = Theme.UiTitle(12.5f);
            fSub = Theme.Ui(9f);
            fLabel = Theme.Ui(9f);
            fValue = Theme.UiSemi(11f);
            fPct = Theme.UiSemi(10f);
            fIcon = new Font(Theme.IconFont, 17f);
            fSun = new Font(Theme.IconFont, 12f);
            fPill = Theme.Ui(8.75f);
            fEmpty = Theme.Ui(10f);
            tick.Tick += (o, e) => Pump(false);
        }

        public IReadOnlyList<DisplayInfo> Displays => shown;

        public void SetDisplays(List<DisplayInfo> list)
        {
            // a refresh while dragging: keep the dragged value on screen
            foreach (var d in list)
            {
                var was = shown.Find(x => x.Key == d.Key);
                if (was != null && was.Brightness >= 0 && d.Brightness < 0) d.Brightness = was.Brightness;
            }
            shown = list ?? new List<DisplayInfo>();
            if (drag != null) drag = shown.Find(x => x.Key == drag.Key);
            if (focus != null) focus = shown.Find(x => x.Key == focus.Key);
            hoverSlider = null;
            Loading = false;
            sc.Fit();
            Invalidate();
        }

        // read back from the monitor: show it, unless he's moving that slider right now
        public void SetBrightness(string key, int pct, string why)
        {
            var d = shown.Find(x => x.Key == key);
            if (d == null) return;
            if (pct < 0) { d.State = BrightState.Unavailable; d.Why = why; }
            else
            {
                d.State = BrightState.Ok;
                if (drag != d && !(Co(d).HasPending)) { d.Brightness = pct; Co(d).Reset(); }
            }
            Invalidate();
        }

        public void MarkFailed(string key)
        {
            var d = shown.Find(x => x.Key == key);
            if (d == null || d == drag) return;
            d.State = BrightState.Unavailable;
            d.Why = d.Internal ? "WMI" : "DDC/CI";
            Invalidate();
        }

        Coalescer Co(DisplayInfo d)
        {
            if (!co.TryGetValue(d.Key, out var c)) co[d.Key] = c = new Coalescer();
            return c;
        }

        long Now => clock.ElapsedMilliseconds;

        void SetPct(DisplayInfo d, int pct)
        {
            pct = DisplayMath.Clamp(pct);
            if (d.Brightness == pct) return;
            d.Brightness = pct;
            Invalidate();
            var v = Co(d).Push(pct, Now);
            if (v != null) Changed?.Invoke(d, v.Value);
            if (!tick.Enabled) tick.Start();
        }

        // while moving: the trailing value once the interval allows; after: the final value at once
        void Pump(bool final)
        {
            bool any = false;
            foreach (var d in shown)
            {
                var c = Co(d);
                if (!c.HasPending) continue;
                bool fin = final || (d != drag && !wheelPending);
                var v = fin ? c.Flush(Now) : c.Tick(Now);
                if (v != null) Changed?.Invoke(d, v.Value);
                if (c.HasPending) any = true;
            }
            if (wheelPending && Now - lastWheelMs > 120) wheelPending = false;
            if (!any && drag == null && !wheelPending) tick.Stop();
        }

        // ------------------------------------------------------------ drawing
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);
            sliders.Clear();
            int w = Width - S(12);
            if (shown.Count == 0)
            {
                TextRenderer.DrawText(g, Loading ? "Looking for displays…" : "No displays found. Press Refresh after plugging one in.", fEmpty,
                    new Rectangle(0, S(8), w, S(30)), Theme.Sub, Ux.L);
                contentH = 0;
                return;
            }
            int y = -sc.Value;
            foreach (var d in shown)
            {
                var r = new Rectangle(0, y, w, S(CardH));
                if (r.Bottom >= 0 && r.Y <= Height) DrawCard(g, d, r);
                else sliders.Add(Tuple.Create(SliderHit(r), d));
                y += r.Height + S(12);
            }
            contentH = y + sc.Value;
            sc.DrawBar(g);
        }

        Rectangle Track(Rectangle card)
        {
            int x0 = card.X + S(18) + S(34);
            int right = card.Right - S(18) - S(56);
            return new Rectangle(x0 + S(8), card.Y + S(140) - S(2), Math.Max(S(40), right - x0 - S(16)), S(4));
        }

        Rectangle SliderHit(Rectangle card)
        {
            var t = Track(card);
            return new Rectangle(t.X - S(10), t.Y - S(12), t.Width + S(20), t.Height + S(24));
        }

        int TextW(Graphics g, string t, Font f) => TextRenderer.MeasureText(g, t, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;

        void DrawCard(Graphics g, DisplayInfo d, Rectangle r)
        {
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            Theme.Fill(g, rf, CardBg, S(12));
            Theme.Stroke(g, rf, Focused && focus == d ? Color.FromArgb(78, 78, 78) : CardEdge, S(12), Math.Max(1, s));
            int x0 = r.X + S(18), top = r.Y;

            // icon tile: laptop for the built-in panel, monitor otherwise
            var ir = new Rectangle(x0, top + S(16), S(44), S(44));
            Theme.Fill(g, ir, Theme.Field, S(11));
            TextRenderer.DrawText(g, d.Internal ? "" : "", fIcon, ir, d.Primary ? Theme.Accent : Theme.Sub, Ux.C);

            // name + Primary pill, then what kind of display it is
            int nx = ir.Right + S(16);
            int pillW = d.Primary ? TextW(g, "Primary", fPill) + S(22) : 0;
            int maxName = r.Right - S(18) - nx - (pillW > 0 ? pillW + S(12) : 0);
            int nw = Math.Min(maxName, TextW(g, d.Name, fName) + S(2));
            TextRenderer.DrawText(g, d.Name, fName, new Rectangle(nx, top + S(14), nw, S(26)), Theme.Text, Ux.L | TextFormatFlags.EndEllipsis);
            if (d.Primary)
            {
                var pill = new Rectangle(nx + nw + S(12), top + S(16), pillW, S(22));
                Theme.Fill(g, pill, Color.FromArgb(34, Theme.Accent), pill.Height / 2f);
                TextRenderer.DrawText(g, "Primary", fPill, pill, Theme.Text, Ux.C);
            }
            string kind = (d.Internal ? "Built-in" : "External") + (d.Connection != null && d.Connection != "Built-in" ? "  ·  " + d.Connection : "");
            TextRenderer.DrawText(g, kind, fSub, new Rectangle(nx, top + S(40), r.Right - S(18) - nx, S(20)), Theme.Sub, Ux.L);

            // the three numbers
            int cx = x0, cw = Math.Max(S(120), Math.Min(S(170), (r.Width - S(36)) / 3));
            void Stat(string label, string value)
            {
                TextRenderer.DrawText(g, label, fLabel, new Rectangle(cx, top + S(74), cw, S(18)), Theme.Dim, Ux.L);
                TextRenderer.DrawText(g, value, fValue, new Rectangle(cx, top + S(92), cw, S(24)), Theme.Text, Ux.L);
                cx += cw;
            }
            Stat("Resolution", DisplayMath.Resolution(d));
            Stat("Scale", DisplayMath.ScaleText(d));
            Stat("Refresh rate", DisplayMath.Rate(d));

            // brightness
            var sunR = new Rectangle(x0, top + S(140) - S(14), S(28), S(28));
            var t = Track(r);
            if (d.State == BrightState.Ok)
            {
                TextRenderer.DrawText(g, "", fSun, sunR, Theme.Sub, Ux.C);
                Theme.Fill(g, t, Theme.Track, t.Height / 2f);
                int pct = Math.Max(0, d.Brightness);
                float fx = t.X + t.Width * pct / 100f;
                Theme.Fill(g, new RectangleF(t.X, t.Y, Math.Max(t.Height, fx - t.X), t.Height), Theme.Accent, t.Height / 2f);
                bool hot = drag == d || hoverSlider == d;
                var c = new PointF(fx, t.Y + t.Height / 2f);
                Theme.Disc(g, c, S(9), Color.FromArgb(69, 69, 69));
                Theme.Disc(g, c, hot ? S(6) : S(5), Theme.Accent);
                TextRenderer.DrawText(g, pct + "%", fPct, new Rectangle(r.Right - S(18) - S(52), t.Y - S(10), S(52), S(24)), Theme.Text, Ux.R);
                sliders.Add(Tuple.Create(SliderHit(r), d));
            }
            else
            {
                TextRenderer.DrawText(g, "", fSun, sunR, Theme.Dim, Ux.C);
                string why = d.Why ?? (d.Internal ? "WMI" : "DDC/CI");
                string msg = d.State == BrightState.Reading ? "Reading brightness…"
                    : "Brightness unavailable (" + why + ")" + (why == "DDC/CI" ? "  ·  turn on DDC/CI in the monitor's menu" : "");
                TextRenderer.DrawText(g, msg, fSub, new Rectangle(t.X - S(8), t.Y - S(10), r.Right - S(18) - t.X, S(24)), Theme.Dim, Ux.L);
            }
        }

        // ------------------------------------------------------------ mouse and keys
        DisplayInfo SliderAt(Point p)
        {
            foreach (var h in sliders) if (h.Item1.Contains(p) && h.Item2.State == BrightState.Ok) return h.Item2;
            return null;
        }

        int PctAt(DisplayInfo d, int x)
        {
            int i = shown.IndexOf(d);
            var card = new Rectangle(0, -sc.Value + i * (S(CardH) + S(12)), Width - S(12), S(CardH));
            var t = Track(card);
            return DisplayMath.Clamp((int)Math.Round((x - t.X) * 100.0 / Math.Max(1, t.Width)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (sc.MouseDown(e)) return;
            if (e.Button != MouseButtons.Left) return;
            var d = SliderAt(e.Location);
            if (d == null) return;
            drag = focus = d;
            Capture = true;
            SetPct(d, PctAt(d, e.X));
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (sc.MouseMove(e)) return;
            if (drag != null) { SetPct(drag, PctAt(drag, e.X)); return; }
            var h = SliderAt(e.Location);
            if (h != hoverSlider) { hoverSlider = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (sc.MouseUp(e)) return;
            if (drag == null) return;
            SetPct(drag, PctAt(drag, e.X));
            drag = null;
            Capture = false;
            Pump(true);   // the final value, always
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            sc.CaptureLost();
            if (drag != null && !Capture) { drag = null; Pump(true); Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            sc.Leave();
            if (hoverSlider != null && drag == null) { hoverSlider = null; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var d = SliderAt(e.Location);
            if (d != null)
            {
                // over a slider: the wheel changes brightness, 5% a notch (touchpads: finer)
                focus = d;
                wheelPending = true;
                lastWheelMs = (int)Now;
                int step = (int)Math.Round(e.Delta / 120.0 * 5);
                if (step == 0) step = Math.Sign(e.Delta);
                SetPct(d, Math.Max(0, d.Brightness) + step);
                return;
            }
            sc.Wheel(e.Delta, S(60));
        }

        protected override bool IsInputKey(Keys k) =>
            k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var ok = shown.FindAll(x => x.State == BrightState.Ok);
            if (ok.Count == 0) return;
            if (focus == null || !ok.Contains(focus)) focus = ok[0];
            int i = ok.IndexOf(focus);
            int v = Math.Max(0, focus.Brightness);
            switch (e.KeyCode)
            {
                case Keys.Left: SetPct(focus, v - 1); break;
                case Keys.Right: SetPct(focus, v + 1); break;
                case Keys.PageDown: SetPct(focus, v - 10); break;
                case Keys.PageUp: SetPct(focus, v + 10); break;
                case Keys.Home: SetPct(focus, 0); break;
                case Keys.End: SetPct(focus, 100); break;
                case Keys.Up: focus = ok[Math.Max(0, i - 1)]; Invalidate(); break;
                case Keys.Down: focus = ok[Math.Min(ok.Count - 1, i + 1)]; Invalidate(); break;
                default: return;
            }
            e.Handled = true;
            if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) { wheelPending = true; lastWheelMs = (int)Now; }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Pump(true); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); sc.Fit(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tick.Dispose(); sc.Dispose(); }
            base.Dispose(disposing);
        }

        // for the tests
        public void DragForTest(DisplayInfo d, int pct, bool up) { drag = d; SetPct(d, pct); if (up) { drag = null; Pump(true); } }
    }
}
