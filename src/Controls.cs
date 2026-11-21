using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Minibar
{
    // Small dark controls for the main window (same family as Pulp's settings window).
    static class Ux
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        public static void Dark(Control c) => c.HandleCreated += (o, e) => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
        public static void Cue(TextBox b, string text) { try { if (b.IsHandleCreated) SendMessage(b.Handle, 0x1501, (IntPtr)1, text); } catch { } }
        public const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        public const TextFormatFlags R = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        public const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
    }

    abstract class MbControl : Control
    {
        protected readonly float s;
        protected MbControl(float scale)
        {
            s = scale;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected int S(float v) => (int)Math.Round(v * s);
    }

    class DarkBox : Panel
    {
        public readonly TextBox Box;
        readonly float s;
        public DarkBox(float scale)
        {
            s = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Padding = new Padding(S(10), S(7), S(10), S(5));
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text, Font = Theme.Ui(10f), Dock = DockStyle.Fill };
            Box.GotFocus += (o, e) => Invalidate();
            Box.LostFocus += (o, e) => Invalidate();
            Controls.Add(Box);
        }
        int S(float v) => (int)Math.Round(v * s);
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Theme.Bg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.Fill(e.Graphics, r, Theme.Field, S(6));
            Theme.Stroke(e.Graphics, r, Box.Focused ? Theme.Accent : Theme.Border, S(6), Math.Max(1, s));
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }
    }

    class Toggle : MbControl
    {
        bool on;
        public event Action Changed;
        public string Hint;
        public Toggle(string text, float scale) : base(scale)
        {
            Text = text;
            Font = Theme.Ui(10f);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            Height = S(28);
        }
        public bool On { get => on; set { if (on != value) { on = value; Invalidate(); } } }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); if (!Enabled) return; On = !On; Changed?.Invoke(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { On = !On; Changed?.Invoke(); e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            int top = Hint != null ? S(2) : (Height - S(20)) / 2;
            var pill = new RectangleF(S(1), top, S(40), S(20));
            if (on) Theme.Fill(g, pill, Enabled ? Theme.Accent : Theme.Dim, pill.Height / 2);
            else Theme.Stroke(g, pill, Focused ? Theme.Text : Enabled ? Theme.Sub : Theme.Dim, pill.Height / 2, Math.Max(1, s));
            float d = on ? S(14) : S(12);
            float kx = on ? pill.Right - S(3) - d : pill.X + S(4);
            using (var b = new SolidBrush(on ? Color.FromArgb(24, 24, 24) : Enabled ? Theme.Sub : Theme.Dim))
                g.FillEllipse(b, kx, pill.Y + (pill.Height - d) / 2, d, d);
            var tr = new Rectangle(S(52), (int)pill.Y - S(2), Width - S(52), S(24));
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : Theme.Dim, Ux.L);
            if (Hint != null)
                using (var f = Theme.Ui(8.75f))
                    TextRenderer.DrawText(g, Hint, f, new Rectangle(S(52), tr.Bottom, Width - S(52), Height - tr.Bottom), Theme.Dim,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    class FlatBtn : MbControl
    {
        public bool Primary;
        bool hover, down;
        public FlatBtn(string text, float scale, bool primary = false) : base(scale)
        {
            Text = text;
            Primary = primary;
            Font = primary ? Theme.UiSemi(10f) : Theme.Ui(10f);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            Size = new Size(S(120), S(32));
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg, fg;
            if (!Enabled) { bg = Theme.CardSoft; fg = Theme.Dim; }
            else if (Primary) { bg = hover ? Theme.AccentHover : Theme.Accent; fg = Color.FromArgb(20, 20, 20); }
            else { bg = down ? Theme.CardHover : hover ? Color.FromArgb(52, 52, 52) : Theme.Card; fg = Theme.Text; }
            Theme.Fill(g, r, bg, S(6));
            if (Focused && !Primary && Enabled) Theme.Stroke(g, r, Theme.Sub, S(6), Math.Max(1, s));
            TextRenderer.DrawText(g, Text, Font, r, fg, Ux.C);
        }
    }

    // accent colour picker: preset swatches + a custom colour
    class SwatchRow : MbControl
    {
        public Color Value = Theme.DefaultAccent;
        public event Action<Color> Picked;
        int hover = -1;
        readonly Font fIcon;
        int Count => Theme.Presets.Length + 1;
        public SwatchRow(float scale) : base(scale)
        {
            Height = S(36);
            Cursor = Cursors.Hand;
            fIcon = new Font(Theme.IconFont, 10f);
        }
        Rectangle Cell(int i) => new Rectangle(i * S(36), 0, S(36), S(36));
        int CustomIndex => Theme.Presets.Length;
        bool IsPreset => Array.Exists(Theme.Presets, p => p.ToArgb() == Value.ToArgb());
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            for (int i = 0; i < Count; i++)
            {
                var c = Cell(i);
                var center = new PointF(c.X + c.Width / 2f, c.Y + c.Height / 2f);
                bool custom = i == CustomIndex;
                Color col = custom ? (IsPreset ? Theme.Card : Value) : Theme.Presets[i];
                bool sel = custom ? !IsPreset : Theme.Presets[i].ToArgb() == Value.ToArgb();
                if (sel || i == hover) Theme.Disc(g, center, S(15), sel ? Theme.Text : Theme.Border);
                Theme.Disc(g, center, S(13), BackColor);
                Theme.Disc(g, center, S(11), col);
                if (custom && IsPreset) TextRenderer.DrawText(g, "\uE710", fIcon, c, Theme.Sub, Ux.C);   // Add
            }
        }
        int Hit(Point p) { for (int i = 0; i < Count; i++) if (Cell(i).Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = Hit(e.Location);
            if (h < 0 || e.Button != MouseButtons.Left) return;
            if (h < CustomIndex) { Value = Theme.Presets[h]; Invalidate(); Picked?.Invoke(Value); return; }
            using (var dlg = new ColorDialog { Color = Value, FullOpen = true, AnyColor = true })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                Value = Color.FromArgb(255, dlg.Color);
                Invalidate();
                Picked?.Invoke(Value);
            }
        }
    }
}

namespace Minibar
{
    // Smooth scrolling for the owner-drawn lists: the wheel moves a target, the view eases toward it at ~60 fps.
    // Uses the real wheel delta, so touchpads scroll by the pixel instead of a whole step.
    sealed class SmoothScroller : IDisposable
    {
        readonly Control owner;
        readonly Timer timer = new Timer { Interval = 10 };
        readonly Func<int> max;
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        const double Ms = 220;   // each notch glides for this long, easing out (time-based, so slow frames never jump)
        float pos, target, from;

        public SmoothScroller(Control c, Func<int> maxScroll)
        {
            owner = c;
            max = maxScroll;
            timer.Tick += (o, e) => Step();
        }

        public int Value => (int)Math.Round(pos);

        float Clamp(float v) => Math.Max(0, Math.Min(Math.Max(0, max()), v));

        public void Wheel(int delta, float pixelsPerNotch)
        {
            if (Math.Abs(delta) < 120)
            {
                // touchpad: tiny steps that already arrive smoothly, so follow them 1:1 (a glide only lags)
                timer.Stop();
                target = pos = from = Clamp(pos - delta * pixelsPerNotch / 120f);
                owner.Invalidate();
                owner.Update();
                return;
            }
            from = pos;
            target = Clamp(target - delta * pixelsPerNotch / 120f);
            clock.Restart();
            if (!timer.Enabled) timer.Start();
            Step();
        }

        // ---- scroll bar: drawn by the owner via DrawBar, dragged or clicked through the Mouse* calls
        public Func<float, int> S = v => (int)Math.Round(v);
        bool drag, barHover;
        int grab;
        public bool Dragging => drag;
        int ContentH => Math.Max(0, max()) + owner.Height;
        bool HasBar => max() > 0;
        Rectangle Zone => new Rectangle(owner.Width - S(12), 0, S(12), owner.Height);

        Rectangle Thumb()
        {
            int h = owner.Height, th = Math.Max(S(30), (int)((long)h * h / Math.Max(1, ContentH)));
            int ty = (int)((long)(h - th) * Value / Math.Max(1, max()));
            return new Rectangle(owner.Width - S(12), ty, S(12), th);
        }

        public void DrawBar(Graphics g)
        {
            if (!HasBar) return;
            var t = Thumb();
            bool big = drag || barHover;   // a little wider under the mouse, like Windows 11
            int w = big ? S(6) : S(3);
            Theme.Fill(g, new Rectangle(owner.Width - S(2) - w, t.Y, w, t.Height), big ? Theme.Sub : Theme.Border, w / 2f);
        }

        void DragTo(int y)
        {
            var t = Thumb();
            float f = Math.Max(0, Math.Min(1, (y - grab) / (float)Math.Max(1, owner.Height - t.Height)));
            timer.Stop();
            target = pos = from = Clamp(f * max());
            owner.Invalidate();
            owner.Update();
        }

        // true = the scroll bar took the event
        public bool MouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !HasBar || !Zone.Contains(e.Location)) return false;
            var t = Thumb();
            grab = t.Contains(e.Location) ? e.Y - t.Y : t.Height / 2;   // a click on the track centres the thumb there
            drag = true;
            owner.Capture = true;
            DragTo(e.Y);
            return true;
        }

        public bool MouseMove(MouseEventArgs e)
        {
            if (drag) { DragTo(e.Y); return true; }
            bool on = HasBar && Zone.Contains(e.Location);
            if (on != barHover) { barHover = on; owner.Invalidate(); }
            return on;
        }

        public bool MouseUp(MouseEventArgs e)
        {
            if (!drag) return false;
            drag = false;
            owner.Capture = false;
            barHover = HasBar && Zone.Contains(e.Location);
            owner.Invalidate();
            return true;
        }

        public void Leave() { if (barHover && !drag) { barHover = false; owner.Invalidate(); } }
        public void CaptureLost() { if (drag && !owner.Capture) { drag = false; owner.Invalidate(); } }

        // keep inside the content after a resize or a list change
        public void Fit()
        {
            float t = Clamp(target), p = Clamp(pos);
            if (t != target || p != pos) { target = t; pos = p; owner.Invalidate(); }
        }

        public void JumpTo(int v) { target = pos = Clamp(v); timer.Stop(); owner.Invalidate(); }

        void Step()
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / Ms);
            pos = (float)(from + (target - from) * (1 - Math.Pow(1 - t, 3)));
            if (t >= 1) { pos = target; timer.Stop(); }
            owner.Invalidate();
            owner.Update();
        }

        public void Dispose() { timer.Dispose(); }
    }
}
