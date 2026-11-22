using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Minibar;

namespace MinibarSetup
{
    // One small dark window in Minibar's style: install / update, or uninstall.
    class SetupWindow : Form
    {
        enum Phase { Ready, Busy, Done }

        readonly bool uninstall;
        readonly float s;
        Phase phase = Phase.Ready;
        readonly Toggle option;
        readonly FlatBtn primary, secondary, extra;
        readonly Font fTitle, fSub, fHead, fBody, fSmall;
        readonly System.Windows.Forms.Timer poll;

        string head, body, path;           // the card
        string status; Color statusColor;  // line under the options
        float progress = -1;               // -1 = no bar
        bool forceClose;                   // Minibar didn't close by itself: next click closes it for real
        bool doneIcon;
        Inst.Result result;
        string installed;                  // version already there (null = none)

        public bool Removed { get; private set; }
        public List<string> Leftovers { get; private set; } = new List<string>();

        int S(float v) => (int)Math.Round(v * s);
        int W => S(560);
        int H => S(404);

        public SetupWindow(bool uninstall)
        {
            this.uninstall = uninstall;
            Text = uninstall ? "Uninstall Minibar" : "Minibar Setup";
            Icon = Theme.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (var g = CreateGraphics()) s = Math.Max(1f, g.DpiX / 96f);
            ClientSize = new Size(W, H);

            fTitle = Theme.UiTitle(17f);
            fSub = Theme.Ui(10f);
            fHead = Theme.UiSemi(11f);
            fBody = Theme.Ui(9.75f);
            fSmall = Theme.Ui(9f);

            option = new Toggle(uninstall ? "Also delete Minibar's settings and saved backups" : "Desktop shortcut", s);
            if (uninstall) { option.Hint = "Backups are the older app versions Minibar keeps so you can go back."; option.Height = S(46); }
            option.SetBounds(S(36), S(256), W - S(72), option.Height);
            Controls.Add(option);

            primary = new FlatBtn(uninstall ? "Uninstall" : "Install", s, true) { Size = new Size(S(150), S(34)) };
            secondary = new FlatBtn("Cancel", s) { Size = new Size(S(110), S(34)) };
            extra = new FlatBtn("Show old copy", s) { Size = new Size(S(136), S(34)), Visible = false };
            primary.Location = new Point(W - S(36) - primary.Width, H - S(30) - primary.Height);
            secondary.Location = new Point(primary.Left - S(10) - secondary.Width, primary.Top);
            extra.Location = new Point(S(36), primary.Top);
            primary.Click += (o, e) => Primary();
            secondary.Click += (o, e) => Secondary();
            extra.Click += (o, e) => ShowOldCopy();
            Controls.AddRange(new Control[] { primary, secondary, extra });

            Describe();
            poll = new System.Windows.Forms.Timer { Interval = 1500 };
            poll.Tick += (o, e) => { if (phase == Phase.Ready && !forceClose) RunningNote(); };
            poll.Start();
            RunningNote();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1; Win.DwmSetWindowAttribute(Handle, 20, ref on, 4);                       // dark title bar
                int c = Theme.Bg.R | Theme.Bg.G << 8 | Theme.Bg.B << 16; Win.DwmSetWindowAttribute(Handle, 35, ref c, 4);
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            primary.Focus();
            try { Win.SetForegroundWindow(Handle); } catch { }
        }

        // ------------------------------------------------------------ what the card says
        void Describe()
        {
            string ver = Inst.PayloadVersion;
            path = Inst.Dir;
            if (uninstall)
            {
                head = "Remove Minibar from this PC";
                body = "Removes its folder, its shortcuts and its entry in Installed apps. Echoa, Fizz, Juice, Pour and Pulp stay installed and keep working.";
                if (!Directory.Exists(Inst.Dir) && !Inst.Registered) { head = "Minibar isn't installed"; body = "There's nothing to remove."; primary.Enabled = false; option.Visible = false; secondary.Text = "Close"; }
                return;
            }
            installed = Inst.InstalledVersion;
            option.On = installed == null || File.Exists(Inst.DesktopLink) && Inst.InDir(Shortcuts.Target(Inst.DesktopLink));
            var have = Inst.Parse(installed); var get = Inst.Parse(ver);
            if (installed == null)
            {
                head = "Install Minibar " + ver;
                body = "Just for you, no admin needed. Minibar keeps itself up to date from GitHub, and your apps and their settings are never touched.";
                primary.Text = "Install";
            }
            else if (have != null && get != null && have < get)
            {
                head = "Update Minibar " + installed + " to " + ver;
                body = "Your settings and your apps stay exactly as they are.";
                primary.Text = "Update";
            }
            else if (have != null && get != null && have > get)
            {
                head = "You already have Minibar " + installed;
                body = "That's newer than this setup (" + ver + "). Minibar updates itself, so there's nothing to do here.";
                primary.Text = "Open Minibar";
                secondary.Text = "Install " + ver;
                secondary.Width = S(130);
                secondary.Left = primary.Left - S(10) - secondary.Width;
                option.Visible = false;
            }
            else
            {
                head = "Minibar " + installed + " is already installed";
                body = "Reinstall puts a fresh copy in place. Your settings stay.";
                primary.Text = "Open Minibar";
                secondary.Text = "Reinstall";
                option.Visible = false;
            }
        }

        bool AlreadyCurrent => !uninstall && installed != null && primary.Text == "Open Minibar";

        void RunningNote()
        {
            List<Inst.Running> r;
            try { r = Inst.FindRunning(); } catch { return; }
            if (uninstall) r = r.Where(x => Inst.InDir(x.Path)).ToList();
            string note = null;
            if (r.Count > 0 && !AlreadyCurrent)
                note = uninstall ? "Minibar is open. It will be closed first."
                    : "Minibar is open. Setup closes it first and opens it again when it's done.";
            SetStatus(note, Theme.Sub);
        }

        void SetStatus(string text, Color c)
        {
            if (status == text && statusColor == c) return;
            status = text; statusColor = c;
            Invalidate();
        }

        // ------------------------------------------------------------ buttons
        void Primary()
        {
            if (phase == Phase.Busy) return;
            if (phase == Phase.Done)
            {
                if (!uninstall && primary.Text == "Open Minibar") Open();
                Close();
                return;
            }
            if (AlreadyCurrent) { Open(); Close(); return; }
            Start();
        }

        void Secondary()
        {
            if (phase == Phase.Busy) return;
            if (phase == Phase.Ready && AlreadyCurrent) { Start(); return; }   // Reinstall / Install anyway
            Close();
        }

        void Open()
        {
            try { Inst.Launch(); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't open Minibar: " + ex.Message, "Minibar", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        void ShowOldCopy()
        {
            var p = result?.OldCopies.FirstOrDefault();
            if (p == null) return;
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + p + "\"")?.Dispose(); } catch { }
        }

        // ------------------------------------------------------------ the work (on its own thread; the window stays responsive)
        void Start()
        {
            phase = Phase.Busy;
            bool desktop = option.On, removeData = option.On, kill = forceClose;
            forceClose = false;
            primary.Enabled = secondary.Enabled = option.Enabled = false;
            ControlBox = false;
            progress = 0.05f;
            SetStatus(uninstall ? "Uninstalling…" : "Installing…", Theme.Sub);
            var t = new Thread(() =>
            {
                Exception err = null; bool stuck = false, reopen = false;
                try
                {
                    var running = Inst.FindRunning();
                    if (uninstall) running = running.Where(x => Inst.InDir(x.Path)).ToList();
                    if (running.Count > 0)
                    {
                        Step(0.15f, "Closing Minibar…");
                        var left = Inst.Close(running, 20000);
                        if (left.Count > 0)
                        {
                            if (!kill) { stuck = true; return; }
                            Inst.Kill(left);
                        }
                        reopen = !uninstall;
                    }
                    if (uninstall)
                    {
                        Step(0.4f, "Removing Minibar…");
                        var left = Inst.Uninstall(removeData, m => Step(0.7f, m));
                        Ui(() => { Leftovers = left; Removed = true; });
                    }
                    else
                    {
                        var res = Inst.Install(desktop, running.Select(r => r.Path), m => Step(m.StartsWith("Copy") ? 0.35f : m.StartsWith("Add") ? 0.7f : 0.85f, m));
                        Ui(() => result = res);
                    }
                    Step(1f, null);
                }
                catch (Exception ex) { err = ex; }
                finally
                {
                    bool s2 = stuck, r2 = reopen; var e2 = err;
                    Ui(() => Finish(e2, s2, r2));
                }
            }) { IsBackground = false, Name = "setup" };
            t.Start();
        }

        void Step(float p, string msg) => Ui(() => { progress = p; if (msg != null) SetStatus(msg, Theme.Sub); Invalidate(); });

        void Ui(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        void Finish(Exception err, bool stuck, bool reopen)
        {
            progress = -1;
            ControlBox = true;
            primary.Enabled = secondary.Enabled = option.Enabled = true;
            if (stuck)
            {
                phase = Phase.Ready;
                forceClose = true;
                primary.Text = "Close it anyway";
                SetStatus("Minibar didn't close. It may be finishing an update: wait a moment and try again, or close it anyway.", Theme.Red);
                return;
            }
            if (err != null)
            {
                phase = Phase.Ready;
                SetStatus((uninstall ? "Uninstall didn't finish: " : "Nothing was changed: ") + err.Message, Theme.Red);
                if (!uninstall) primary.Text = "Try again";
                return;
            }
            phase = Phase.Done;
            doneIcon = true;
            option.Visible = false;
            status = null;
            if (uninstall)
            {
                head = "Minibar was removed";
                body = "Your drink apps are still here and keep working.";
                path = null;
                primary.Text = "Close";
                secondary.Visible = false;
            }
            else
            {
                head = installed == null ? "Minibar is installed" : "Minibar " + Inst.PayloadVersion + " is ready";
                body = "Find it in Start: search \"Minibar\"." + (File.Exists(Inst.DesktopLink) ? " There's a shortcut on your desktop too." : "") + " You can delete the setup file now.";
                if (result?.OldCopies.Count > 0)
                {
                    body += "\n\nYour old copy is still in " + Path.GetDirectoryName(result.OldCopies[0]) + ". You can delete it; your settings carry over.";
                    extra.Visible = true;
                }
                if (result?.Warning != null) body += "\n" + result.Warning;
                if (reopen) { Open(); primary.Text = "Done"; secondary.Visible = false; }
                else { primary.Text = "Open Minibar"; secondary.Text = "Close"; }
            }
            Invalidate();
        }

        public void ShowError(Exception ex)
        {
            try { SetStatus("Something went wrong: " + ex.Message, Theme.Red); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (phase == Phase.Busy) { e.Cancel = true; return; }
            poll.Stop();
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && phase != Phase.Busy) { Close(); e.Handled = true; }
        }

        // ------------------------------------------------------------ painting
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);

            // header: logo, name, line
            Theme.DrawLogo(g, new RectangleF(S(36), S(30), S(60), S(60)));
            TextRenderer.DrawText(g, uninstall ? "Uninstall Minibar" : "Minibar", fTitle, new Rectangle(S(112), S(30), W - S(150), S(34)), Theme.Text, Ux.L);
            TextRenderer.DrawText(g, "Home for your drink apps  ·  version " + Inst.PayloadVersion, fSub, new Rectangle(S(113), S(64), W - S(150), S(22)), Theme.Sub, Ux.L);

            // card
            var card = new Rectangle(S(36), S(118), W - S(72), S(118));
            if (phase == Phase.Done && body != null && body.Contains("\n\n")) card.Height = S(176);
            Theme.Fill(g, card, Theme.Card, S(10));
            int x = card.X + S(20), w = card.Width - S(40);
            if (doneIcon)
            {
                var c = new PointF(card.X + S(34), card.Y + S(36));
                Theme.Disc(g, c, S(14), Theme.Accent);
                using (var pen = new Pen(Color.FromArgb(24, 24, 24), Math.Max(2f, S(2.4f))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(c.X - S(6), c.Y + S(0.5f)), new PointF(c.X - S(1.5f), c.Y + S(5)), new PointF(c.X + S(6.5f), c.Y - S(4.5f)) });
                x = card.X + S(62); w = card.Right - S(20) - x;
            }
            TextRenderer.DrawText(g, head, fHead, new Rectangle(x, card.Y + S(18), w, S(26)), Theme.Text, Ux.L);
            var bodyR = new Rectangle(x, card.Y + S(48), w, card.Height - S(48) - (path != null ? S(34) : S(10)));
            TextRenderer.DrawText(g, body, fBody, bodyR, Theme.Sub, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (path != null)
                TextRenderer.DrawText(g, path, fSmall, new Rectangle(x, card.Bottom - S(30), w, S(20)), Theme.Dim, Ux.L | TextFormatFlags.PathEllipsis);

            // status + progress
            int sy = option.Visible ? option.Bottom + S(10) : card.Bottom + S(16);
            if (!string.IsNullOrEmpty(status))
                TextRenderer.DrawText(g, status, fBody, new Rectangle(S(36), sy, W - S(72), S(40)), statusColor,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (progress >= 0)
            {
                var bar = new RectangleF(S(36), primary.Top - S(18), W - S(72), Math.Max(3, S(4)));
                Theme.Fill(g, bar, Theme.Track, bar.Height / 2);
                Theme.Fill(g, new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * Math.Min(1f, progress)), bar.Height), Theme.Accent, bar.Height / 2);
            }
        }
    }
}
