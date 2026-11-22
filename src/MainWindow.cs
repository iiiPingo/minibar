using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Minibar
{
    class MainWindow : Form
    {
        readonly Settings st;
        readonly List<AppInfo> apps = Catalog.All.Select(d => new AppInfo(d)).ToList();
        readonly float s;
        readonly string[] pages = { "Apps", "Updates", "Settings" };
        readonly List<Panel> pagePanels = new List<Panel>();
        readonly Font fBody, fSmall, fTitle, fNav, fH2, fNavIcon;
        readonly string[] navGlyphs = { Theme.GApps, "\uE895", Theme.GSettings };   // Sync
        readonly AppInfo self = new AppInfo(Catalog.Minibar);
        UpdateList ulist;
        Label updSub;
        FlatBtn checkBtn;
        NotifyIcon tray;
        bool checking;
        IEnumerable<AppInfo> AllRows => apps.Concat(new[] { self });
        readonly Timer timer;
        int page, hoverNav = -1;
        AppList list;
        Label appsSub;
        FlatBtn forget;
        Label backupNote;

        int S(float v) => (int)Math.Round(v * s);
        int NavW => S(176);
        int W => S(880);
        int H => S(660);
        Rectangle Content => new Rectangle(NavW + S(28), S(22), W - NavW - S(52), H - S(44));

        public MainWindow(Settings settings)
        {
            st = settings;
            Text = "Minibar";
            Icon = Theme.AppIcon;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            ClientSize = new Size(W, H);
            fBody = Theme.Ui(10f);
            fSmall = Theme.Ui(9f);
            fTitle = Theme.UiTitle(15f);
            fH2 = Theme.UiSemi(10.5f);
            fNav = Theme.Ui(10.5f);
            fNavIcon = new Font(Theme.IconFont, 11f);

            self.Exe = Application.ExecutablePath;
            self.Version = Apps.VersionText(self.Exe) ?? Application.ProductVersion;
            BuildApps();
            BuildUpdates();
            BuildSettings();
            ShowPage(0);

            // first look: find every app, read start-with-Windows, then keep the running state fresh while the window is open
            foreach (var a in apps) Apps.Locate(a, st, null);
            Rescan();
            foreach (var a in apps) Apps.ReadStartup(a, st, false);
            QueryTasks();
            foreach (var a in AllRows) if (st.Releases.TryGetValue(a.Def.Name, out var packed)) a.Rel = Release.Unpack(packed);
            RefreshBackups();
            timer = new Timer { Interval = 1500 };
            timer.Tick += (o, e) => { if (WindowState != FormWindowState.Minimized) Rescan(); };
            timer.Start();
        }

        // ------------------------------------------------------------ window chrome
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                int cap = Theme.Nav.R | (Theme.Nav.G << 8) | (Theme.Nav.B << 16);
                Native.DwmSetWindowAttribute(Handle, 35, ref cap, 4);   // caption colour (Win11)
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_SHOWMINIBAR && Native.WM_SHOWMINIBAR != 0)
            {
                // Minibar was opened again: bring this window up instead
                if (!Visible) RestoreFromTray();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show();
                Activate();
                try { Native.SetForegroundWindow(Handle); } catch { }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (st.CheckUpdates && DateTime.UtcNow - st.LastCheck > TimeSpan.FromHours(1)) CheckForUpdates(false);
            UpdateCheckedText();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // back from an installer or another app: things may have changed
            var fizz = apps.FirstOrDefault(a => a.Def.Name == "Fizz");
            if (fizz != null && fizz.Found) Apps.CheckFizz(fizz);
            Rescan();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { LookAgain(); e.Handled = true; }
            else if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D3) { ShowPage(e.KeyCode - Keys.D1); e.Handled = true; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            MinimumSize = SizeFromClientSize(new Size(S(780), S(520)));
            var r = st.Window;
            if (r.IsEmpty) return;
            bool max = r.Width < 0;
            r = new Rectangle(r.X, r.Y, Math.Abs(r.Width), Math.Abs(r.Height));
            if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(r.X, r.Y, Math.Min(r.Width, 200), 60))))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = r;
            }
            if (max) WindowState = FormWindowState.Maximized;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            timer?.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            st.Window = WindowState == FormWindowState.Maximized ? new Rectangle(b.Location, new Size(-b.Width, -b.Height)) : b;
            st.Save();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && st.MinimizeToTray && Visible) HideToTray();
            Invalidate();
        }

        // ------------------------------------------------------------ minimize to tray (optional; the icon only exists while hidden)
        void HideToTray()
        {
            try
            {
                if (tray == null)
                {
                    var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = Theme.Ui(10f), ShowImageMargin = false };
                    menu.Items.Add("Open Minibar", null, (o, e) => RestoreFromTray());
                    menu.Items.Add("Check for updates", null, (o, e) => { RestoreFromTray(); ShowPage(1); CheckForUpdates(true); });
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Exit", null, (o, e) => Close());
                    foreach (ToolStripItem it in menu.Items) it.ForeColor = Theme.Text;
                    tray = new NotifyIcon { Text = "Minibar", ContextMenuStrip = menu };
                    try { tray.Icon = new Icon(Theme.AppIcon, SystemInformation.SmallIconSize); } catch { tray.Icon = Theme.AppIcon; }
                    tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
                }
                tray.Visible = true;
                ShowInTaskbar = false;
                Hide();
            }
            catch (Exception ex) { Log.Crash(ex); }
        }

        void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = st.Window.Width < 0 ? FormWindowState.Maximized : FormWindowState.Normal;
            Activate();
            try { Native.SetForegroundWindow(Handle); } catch { }
            if (tray != null) tray.Visible = false;
            Rescan();
            if (st.CheckUpdates && DateTime.UtcNow - st.LastCheck > TimeSpan.FromHours(6)) CheckForUpdates(false);
        }

        // ------------------------------------------------------------ pages
        Panel NewPage(string title, string subtitle, out Label sub)
        {
            var c = Content;
            var p = new Panel { Location = c.Location, Size = c.Size, BackColor = Theme.Bg, Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            p.Controls.Add(new Label { Text = title, Font = fTitle, ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, 0), BackColor = Theme.Bg });
            sub = new Label { Text = subtitle ?? "", Font = fSmall, ForeColor = Theme.Sub, AutoSize = false, Size = new Size(c.Width, S(28)), Location = new Point(0, S(39)), AutoEllipsis = true, BackColor = Theme.Bg, UseMnemonic = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            p.Controls.Add(sub);
            Controls.Add(p);
            pagePanels.Add(p);
            return p;
        }

        Label Lbl(Control p, string text, int x, int y, int w, int h, Color? color = null, Font f = null)
        {
            var l = new Label { Text = text, Font = f ?? fBody, ForeColor = color ?? Theme.Text, BackColor = Theme.Bg, AutoSize = false, Location = new Point(x, y), Size = new Size(w, h), UseMnemonic = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            p.Controls.Add(l);
            return l;
        }

        void BuildApps()
        {
            var p = NewPage("Apps", "Looking for your apps…", out appsSub);
            list = new AppList(s) { Location = new Point(0, S(70)), Size = new Size(p.Width, p.Height - S(70)), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            list.SetApps(apps);
            list.Act += OnAct;
            p.Controls.Add(list);
            var again = new FlatBtn("Refresh", s) { Location = new Point(p.Width - S(112) - S(12), S(6)), Width = S(112), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            again.Click += (o, e) => LookAgain();
            p.Controls.Add(again);
        }

        void BuildSettings()
        {
            var p = NewPage("Settings", "Everything here takes effect right away.", out _);
            p.AutoScroll = true;
            Ux.Dark(p);
            int y = S(70), w = p.Width;
            var accL = Lbl(p, "Accent colour", 0, y, S(120), S(36), Theme.Text, fBody);
            accL.TextAlign = ContentAlignment.MiddleLeft;
            accL.Anchor = AnchorStyles.Top | AnchorStyles.Left;   // stretching with the window made it cover the swatches
            var sw = new SwatchRow(s) { Location = new Point(S(120), y), Width = w - S(120), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Theme.TryHex(st.Accent, out var cur);
            sw.Value = st.Accent.Length == 0 ? Theme.DefaultAccent : cur;
            sw.Picked += c =>
            {
                Theme.SetAccent(c);
                st.Accent = c.ToArgb() == Theme.DefaultAccent.ToArgb() ? "" : Theme.Hex(c);
                st.Save();
                RefreshAll(this);
            };
            p.Controls.Add(sw);
            sw.BringToFront();
            y += S(60);

            var trayT = new Toggle("Minimize to tray", s) { Location = new Point(0, y), Width = w, Hint = "Minimizing hides Minibar to a tray icon. The icon is only there while it's hidden.", Height = S(50), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            trayT.On = st.MinimizeToTray;
            trayT.Changed += () => { st.MinimizeToTray = trayT.On; st.Save(); };
            p.Controls.Add(trayT);
            y += S(56);
            var updT = new Toggle("Check for updates when Minibar opens", s) { Location = new Point(0, y), Width = w, Hint = "Asks GitHub at most once an hour. Nothing installs unless you click Update or turn on Auto.", Height = S(50), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            updT.On = st.CheckUpdates;
            updT.Changed += () => { st.CheckUpdates = updT.On; st.Save(); };
            p.Controls.Add(updT);
            y += S(56);
            var smT = new Toggle("Show the apps in Windows search", s) { Location = new Point(0, y), Width = w, Hint = "Start menu shortcuts for each app, so typing \"Pulp\" in search opens it. Off removes them.", Height = S(50), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            smT.On = st.StartMenu;
            smT.Changed += () => { st.StartMenu = smT.On; st.Save(); StartMenu.Reset(); SyncStartMenu(); };
            p.Controls.Add(smT);
            y += S(68);

            Lbl(p, "Finding your apps", 0, y, w, S(24), Theme.Text, fH2);
            Lbl(p, "Minibar uses, in order: a file you picked with Locate, the running app, its start-with-Windows entry, " +
                   "then the newest copy in your Claude Projects folder or Downloads. Backups are skipped.", 0, y + S(26), w, S(40), Theme.Dim, fSmall);
            y += S(74);
            var again = new FlatBtn("Look again", s) { Location = new Point(0, y), Width = S(130) };
            again.Click += (o, e) => LookAgain();
            p.Controls.Add(again);
            forget = new FlatBtn("Forget picked locations", s) { Location = new Point(S(140), y), Width = S(200) };
            forget.Click += (o, e) => { st.Located.Clear(); st.Save(); LookAgain(); UpdateForget(); };
            p.Controls.Add(forget);
            UpdateForget();
            y += S(62);

            Lbl(p, "Back up settings", 0, y, w, S(24), Theme.Text, fH2);
            Lbl(p, "One zip with the settings of every app and Minibar (link rules, device names, volumes, hotkeys...). " +
                   "Pulp's clipboard history isn't included. Restore puts them back and restarts the apps that were open.", 0, y + S(26), w, S(40), Theme.Dim, fSmall);
            y += S(74);
            var bk = new FlatBtn("Back up…", s) { Location = new Point(0, y), Width = S(130) };
            bk.Click += (o, e) => BackupSettings();
            p.Controls.Add(bk);
            var rs = new FlatBtn("Restore…", s) { Location = new Point(S(140), y), Width = S(130) };
            rs.Click += (o, e) => RestoreSettings();
            p.Controls.Add(rs);
            backupNote = Lbl(p, "", S(284), y, w - S(284), S(32), Theme.Sub, fSmall);
            backupNote.TextAlign = ContentAlignment.MiddleLeft;
            y += S(62);

            Lbl(p, "Minibar's own files", 0, y, w, S(24), Theme.Text, fH2);
            Lbl(p, "settings.ini and crash.log in %APPDATA%\\Minibar; old app versions in %LOCALAPPDATA%\\Minibar\\backup. Closing Minibar quits it, so nothing runs in the background.", 0, y + S(26), w, S(40), Theme.Dim, fSmall);
            y += S(74);
            var folder = new FlatBtn("Open data folder", s) { Location = new Point(0, y), Width = S(150) };
            folder.Click += (o, e) => { try { Directory.CreateDirectory(Settings.Dir); Process.Start("explorer.exe", "\"" + Settings.Dir + "\"")?.Dispose(); } catch { } };
            p.Controls.Add(folder);
        }

        void BuildUpdates()
        {
            var p = NewPage("Updates", "", out updSub);
            checkBtn = new FlatBtn("Check now", s) { Location = new Point(p.Width - S(124) - S(12), S(6)), Width = S(124), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            checkBtn.Click += (o, e) => CheckForUpdates(true);
            p.Controls.Add(checkBtn);
            var backups = new FlatBtn("Old versions", s) { Location = new Point(p.Width - S(124) - S(12) - S(132), S(6)), Width = S(124), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            backups.Click += (o, e) => { try { Directory.CreateDirectory(Updater.BackupRoot); Process.Start("explorer.exe", "\"" + Updater.BackupRoot + "\"")?.Dispose(); } catch { } };
            p.Controls.Add(backups);
            ulist = new UpdateList(s) { Location = new Point(0, S(70)), Size = new Size(p.Width, p.Height - S(70)), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            ulist.AutoOn = a => st.AutoUpdate.Contains(a.Def.Name);
            ulist.SetRows(AllRows.ToList());
            ulist.Act += OnUpdateAct;
            p.Controls.Add(ulist);
        }

        void UpdateCheckedText()
        {
            if (updSub == null) return;
            if (checking) { updSub.Text = "Checking GitHub…"; return; }
            int n = AllRows.Count(a => a.Found && Updater.State(a) == UpdState.Available);
            string when = st.LastCheck == DateTime.MinValue ? "Not checked yet" : "Checked " + Ago(DateTime.UtcNow - st.LastCheck);
            updSub.Text = when + (n > 0 ? "  ·  " + n + (n == 1 ? " update" : " updates") + " ready" : st.LastCheck == DateTime.MinValue ? "" : "  ·  everything's current")
                + "  ·  your settings are never touched by an update";
        }

        static string Ago(TimeSpan t) =>
            t.TotalMinutes < 1 ? "just now" : t.TotalMinutes < 60 ? (int)t.TotalMinutes + " min ago" : t.TotalHours < 24 ? (int)t.TotalHours + " h ago" : (int)t.TotalDays + " days ago";

        void RefreshBackups()
        {
            foreach (var a in AllRows) a.Backup = a.Def.Self ? null : Updater.LatestBackup(a);
        }

        void CheckForUpdates(bool manual)
        {
            if (checking) return;
            checking = true;
            checkBtn.Enabled = false;
            UpdateCheckedText();
            var defs = AllRows.Select(a => a.Def).ToList();
            Task.Run(() =>
            {
                var results = new Dictionary<string, Release>();
                foreach (var d in defs)
                {
                    try { results[d.Name] = Updater.Check(d); }
                    catch (Exception ex) { Log.Crash(ex); }
                }
                Ui(() =>
                {
                    checking = false;
                    checkBtn.Enabled = true;
                    bool anyOk = false;
                    foreach (var a in AllRows)
                    {
                        if (!results.TryGetValue(a.Def.Name, out var r)) continue;
                        // a failed check keeps what was known before (offline shouldn't hide a known update)
                        if (r.Problem != null && r.Problem != "missing" && a.Rel != null && a.Rel.Problem == null) continue;
                        a.Rel = r;
                        st.Releases[a.Def.Name] = r.Pack();
                        if (r.Problem == null || r.Problem == "missing") anyOk = true;
                    }
                    if (anyOk) st.LastCheck = DateTime.UtcNow;
                    st.Save();
                    UpdateCheckedText();
                    list.Invalidate();
                    ulist.Invalidate();
                    // Auto: install by itself, one app after another (Minibar itself last, since it restarts)
                    foreach (var a in apps.Where(x => st.AutoUpdate.Contains(x.Def.Name) && Updater.CanUpdate(x))) StartInstall(a);
                    if (st.AutoUpdate.Contains(self.Def.Name) && Updater.CanUpdate(self) && !apps.Any(x => x.Updating)) StartSelfUpdate();
                });
            });
        }

        // an app that isn't on this PC: download it from GitHub, put it next to Minibar, start it (Fizz waits for its notice)
        void InstallNew(AppInfo a)
        {
            if (a.Found || a.Busy != null || a.Updating) return;
            a.Updating = true;
            a.Busy = "Waiting…";
            a.Error = null;
            list.Invalidate(); ulist.Invalidate();
            string self = null;
            try { self = Application.ExecutablePath; } catch { }
            var rel = a.Rel;
            var th = new System.Threading.Thread(() =>
            {
                Exception err = null;
                string exe = null;
                try { exe = Updater.InstallNew(a.Def, rel, self, msg => Ui(() => { a.Busy = msg; list.Invalidate(); ulist.Invalidate(); })); }
                catch (Exception ex) { err = ex; Log.Crash(ex); }
                Ui(() =>
                {
                    a.Busy = null;
                    a.Updating = false;
                    if (err != null) { Fail(a, err); list.Invalidate(); return; }
                    st.Located[a.Def.Name] = exe;
                    st.Save();
                    Apps.Locate(a, st, null);
                    a.Icon?.Dispose(); a.Icon = null;
                    if (a.Def.Name != "Fizz")
                    {
                        try { Apps.Start(a, ""); } catch (Exception ex) { Log.Crash(ex); }
                        a.Note = a.Def.Name + " is installed and running.";
                    }
                    else a.Note = "Fizz is installed. Read the note below, then press Start.";
                    a.NoteUntil = DateTime.Now.AddSeconds(10); RepaintLater(10200);
                    Rescan();
                    UpdateForget();
                    ulist.Invalidate();
                });
            }) { IsBackground = false, Name = "Minibar install " + a.Def.Name };
            th.Start();
        }

        // installs run on a foreground thread: closing Minibar mid-update still lets it finish (or undo) cleanly
        void StartInstall(AppInfo a)
        {
            if (a.Updating || a.Busy != null || !Updater.CanUpdate(a)) return;
            // a running app is closed (cleanly first), updated and opened again the way it was running (Updater.Apply).
            // Only Pour in the middle of a download waits: the update goes in by itself once the download is done.
            if (Apps.BusyWithWork(a))
            {
                a.Queued = true;
                list.Invalidate(); ulist.Invalidate();
                return;
            }
            a.Queued = false;
            a.Updating = true;
            a.Busy = "Waiting…";
            a.Error = null;
            list.Invalidate(); ulist.Invalidate();
            string target = a.Rel.Label;
            var th = new System.Threading.Thread(() =>
            {
                Exception err = null;
                try { Updater.Install(a, msg => Ui(() => { a.Busy = msg; list.Invalidate(); ulist.Invalidate(); })); }
                catch (Exception ex) { err = ex; Log.Crash(ex); }
                Ui(() =>
                {
                    a.Busy = null;
                    a.Updating = false;
                    a.Version = a.Exe != null ? Apps.VersionText(a.Exe) : null;
                    a.Icon?.Dispose(); a.Icon = null;   // the new exe may have a new icon
                    if (err != null) Fail(a, err);
                    else { a.Note = a.Def.Name + " updated to v" + target + ". Your settings were kept."; a.NoteUntil = DateTime.Now.AddSeconds(10); RepaintLater(10200); }
                    RefreshBackups();
                    Rescan();
                    ulist.Invalidate();
                    UpdateCheckedText();
                });
            }) { IsBackground = false, Name = "Minibar update " + a.Def.Name };
            th.Start();
        }

        void StartSelfUpdate()
        {
            if (self.Updating || !Updater.CanUpdate(self)) return;
            self.Updating = true;
            self.Busy = "Downloading…";
            ulist.Invalidate();
            var th = new System.Threading.Thread(() =>
            {
                Exception err = null;
                try { Updater.SelfUpdate(self, msg => Ui(() => { self.Busy = msg; ulist.Invalidate(); })); }
                catch (Exception ex) { err = ex; Log.Crash(ex); }
                Ui(() =>
                {
                    self.Updating = false;
                    self.Busy = null;
                    if (err != null) { Fail(self, err); ulist.Invalidate(); return; }
                    Close();   // the new Minibar is already starting and waits for this one to exit
                });
            }) { IsBackground = false, Name = "Minibar self-update" };
            th.Start();
        }

        void RollBack(AppInfo a)
        {
            if (a.Busy != null || a.Backup == null) return;
            string label = Updater.BackupLabel(a.Backup) ?? "the previous version";
            a.Busy = "Restoring…";
            a.Updating = true;
            list.Invalidate(); ulist.Invalidate();
            var th = new System.Threading.Thread(() =>
            {
                Exception err = null;
                try { Updater.Rollback(a, msg => Ui(() => { a.Busy = msg; list.Invalidate(); ulist.Invalidate(); })); }
                catch (Exception ex) { err = ex; Log.Crash(ex); }
                Ui(() =>
                {
                    a.Busy = null;
                    a.Updating = false;
                    a.Version = a.Exe != null ? Apps.VersionText(a.Exe) : null;
                    a.Icon?.Dispose(); a.Icon = null;
                    if (err != null) Fail(a, err);
                    else { a.Note = a.Def.Name + " is back on " + label + "."; a.NoteUntil = DateTime.Now.AddSeconds(10); RepaintLater(10200); }
                    RefreshBackups();
                    Rescan();
                    ulist.Invalidate();
                    UpdateCheckedText();
                });
            }) { IsBackground = false, Name = "Minibar rollback " + a.Def.Name };
            th.Start();
        }

        void RepaintLater(int ms)
        {
            var t = new Timer { Interval = ms };
            t.Tick += (o, e) => { t.Stop(); t.Dispose(); list.Invalidate(); ulist?.Invalidate(); };
            t.Start();
        }

        void OnUpdateAct(AppInfo a, string id)
        {
            try
            {
                switch (id)
                {
                    case "update": if (a.Def.Self) StartSelfUpdate(); else if (a.Queued) { a.Queued = false; list.Invalidate(); ulist.Invalidate(); } else StartInstall(a); break;
                    case "auto":
                        if (!st.AutoUpdate.Remove(a.Def.Name)) st.AutoUpdate.Add(a.Def.Name);
                        st.Save();
                        ulist.Invalidate();
                        if (st.AutoUpdate.Contains(a.Def.Name) && Updater.CanUpdate(a)) { if (a.Def.Self) StartSelfUpdate(); else StartInstall(a); }
                        break;
                    case "notes": if (a.Rel != null) Process.Start(new ProcessStartInfo(a.Rel.PageUrl) { UseShellExecute = true })?.Dispose(); break;
                    case "rollback": RollBack(a); break;
                }
            }
            catch (Exception ex) { Fail(a, ex); ulist.Invalidate(); }
        }

        // ------------------------------------------------------------ settings backup
        IEnumerable<AppInfo> BackupRows => apps.Concat(new[] { self });

        void BackupSettings()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            using (var dlg = new SaveFileDialog
            {
                Title = "Save a settings backup",
                Filter = "Zip file (*.zip)|*.zip",
                FileName = "Minibar settings " + DateTime.Now.ToString("yyyy-MM-dd") + ".zip",
                InitialDirectory = docs,
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                st.Save();
                string path = dlg.FileName;
                backupNote.ForeColor = Theme.Sub;
                backupNote.Text = "Saving…";
                Task.Run(() =>
                {
                    string msg; bool ok = true;
                    try { int n = SettingsBackup.Create(BackupRows, path); msg = "Saved " + n + " apps' settings to " + Path.GetFileName(path); }
                    catch (Exception ex) { Log.Crash(ex); msg = "Couldn't save: " + ex.Message; ok = false; }
                    Ui(() => { backupNote.ForeColor = ok ? Theme.Accent : Theme.Red; backupNote.Text = msg; });
                });
            }
        }

        void RestoreSettings()
        {
            string file;
            using (var dlg = new OpenFileDialog { Title = "Pick a Minibar settings backup", Filter = "Zip file (*.zip)|*.zip", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                file = dlg.FileName;
            }
            SettingsBackup.Contents c;
            try { c = SettingsBackup.Open(file); }
            catch (Exception ex) { backupNote.ForeColor = Theme.Red; backupNote.Text = ex.Message; return; }
            try
            {
                string list = string.Join(", ", c.Apps);
                using (var dlg = new DarkConfirm("Restore settings?", "From the backup made " + (c.Made ?? "earlier") + ": " + list + ".\n\n" +
                    "Their current settings are replaced (a copy is kept in %LOCALAPPDATA%\\Minibar). Apps that are open close for a moment.", "Restore", s))
                    if (dlg.ShowDialog(this) != DialogResult.OK) { try { Directory.Delete(c.Dir, true); } catch { } return; }
                backupNote.ForeColor = Theme.Sub;
                backupNote.Text = "Restoring…";
                bool self = c.Apps.Contains(self_Name);
                Task.Run(() =>
                {
                    string msg; bool ok = true;
                    try
                    {
                        var done = SettingsBackup.Restore(c, apps, m => Ui(() => backupNote.Text = m));
                        if (self)
                        {
                            File.Copy(Path.Combine(c.Dir, self_Name, "settings.ini"), Settings.FilePath, true);
                            done.Add("Minibar");
                        }
                        msg = "Restored: " + string.Join(", ", done);
                    }
                    catch (Exception ex) { Log.Crash(ex); msg = "Couldn't restore: " + ex.Message; ok = false; }
                    finally { try { Directory.Delete(Path.Combine(Updater.Root, Path.GetFileName(c.Dir.TrimEnd('\\', '/'))), true); } catch { } try { Directory.Delete(c.Dir, true); } catch { } }
                    Ui(() =>
                    {
                        if (ok && self)
                        {
                            var fresh = Settings.Load();
                            st.CopyFrom(fresh);
                            if (Theme.TryHex(st.Accent, out var col) && st.Accent.Length > 0) Theme.SetAccent(col); else Theme.SetAccent(Theme.DefaultAccent);
                            RefreshAll(this);
                        }
                        backupNote.ForeColor = ok ? Theme.Accent : Theme.Red;
                        backupNote.Text = msg;
                        LookAgain();
                    });
                });
            }
            catch (Exception ex) { Log.Crash(ex); }
        }

        const string self_Name = "Minibar";

        void UpdateForget() { if (forget != null) forget.Enabled = st.Located.Count > 0; }

        // ------------------------------------------------------------ live state
        void Rescan()
        {
            try
            {
                Apps.Scan(apps, st);
                foreach (var a in apps) if (a.Def.RunName != null) Apps.ReadStartup(a, st, false);
                SyncStartMenu();
                foreach (var a in apps.Where(x => x.Queued && !x.Updating && !Apps.BusyWithWork(x)).ToList())
                {
                    if (Updater.CanUpdate(a)) StartInstall(a); else a.Queued = false;
                }
                int found = apps.Count(a => a.Found), running = apps.Count(a => a.Running);
                appsSub.Text = found == 0 ? "None of your apps are on this PC yet. Press Install on a card to get one from GitHub."
                    : running + " of " + found + " running"
                      + (found < apps.Count ? "  ·  " + (apps.Count - found) + " not installed" : "");
                list.Invalidate();
                if (page == 1) { ulist.Invalidate(); UpdateCheckedText(); }
            }
            catch (Exception ex) { Log.Crash(ex); }
        }

        void LookAgain()
        {
            foreach (var a in apps) Apps.Locate(a, st, null);
            Rescan();
            QueryTasks();
        }

        // Fizz starts through a scheduled task; asking schtasks takes a moment, so it runs in the background
        void QueryTasks()
        {
            foreach (var a in apps.Where(x => x.Def.TaskName != null))
            {
                var app = a;
                Task.Run(() =>
                {
                    try { Apps.ReadStartup(app, st, true); } catch (Exception ex) { Log.Crash(ex); }
                    Ui(() => list.Invalidate());
                });
            }
        }

        void Ui(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => { try { a(); } catch (Exception ex) { Log.Crash(ex); } })); }
            catch { }
        }

        void Fail(AppInfo a, Exception ex)
        {
            if (!(ex is OperationCanceledException) && !(ex is InvalidOperationException)) Log.Crash(ex);
            a.Error = ex.Message;
            a.ErrorUntil = DateTime.Now.AddSeconds(7);
            list.Invalidate();
            var t = new Timer { Interval = 7200 };
            t.Tick += (o, e) => { t.Stop(); t.Dispose(); list.Invalidate(); ulist?.Invalidate(); };
            t.Start();
        }

        // run an action off the UI thread, showing "busy" on the card meanwhile
        void Background(AppInfo a, string busy, Action work, Action after = null)
        {
            if (a.Busy != null) return;
            a.Busy = busy;
            a.Error = null;
            list.Invalidate();
            Task.Run(() =>
            {
                Exception err = null;
                try { work(); } catch (Exception ex) { err = ex; }
                Ui(() =>
                {
                    a.Busy = null;
                    if (err != null) Fail(a, err);
                    after?.Invoke();
                    Rescan();
                });
            });
        }

        void OnAct(AppInfo a, string id)
        {
            try
            {
                switch (id)
                {
                    case "start":
                        Background(a, "Starting…", () => { Apps.Start(a); System.Threading.Thread.Sleep(900); });
                        break;
                    case "stop":
                        Background(a, "Stopping…", () => Apps.Stop(a));
                        break;
                    case "startup":
                        {
                            bool on = a.Startup != true;
                            Background(a, on ? "Turning on…" : "Turning off…", () => { Apps.SetStartup(a, st, on); Apps.ReadStartup(a, st, true); });
                            break;
                        }
                    case "folder": Apps.ShowFile(a.Exe); break;
                    case "data": Apps.OpenFolder(a.DataDir); break;
                    case "log": Apps.OpenText(a.LogPath); break;
                    case "locate": PickExe(a); break;
                    case "install": InstallNew(a); break;
                    case "rescan": Apps.Locate(a, st, null); Rescan(); if (a.Def.TaskName != null) QueryTasks(); if (!a.Found) Fail(a, new InvalidOperationException("Still not found. Use Locate to pick " + a.Def.Name + ".exe.")); break;
                    case "update": if (a.Queued) { a.Queued = false; list.Invalidate(); ulist.Invalidate(); } else StartInstall(a); break;
                    case "info": list.ToggleInfo(a); break;
                    case "link:pawnio": Process.Start(new ProcessStartInfo("https://pawnio.eu/") { UseShellExecute = true })?.Dispose(); break;
                    case "link:presentmon": Process.Start(new ProcessStartInfo("https://github.com/GameTechDev/PresentMon/releases") { UseShellExecute = true })?.Dispose(); break;
                    case "pawn":
                        Background(a, "Installing…", () => Apps.InstallPawnIO(a));
                        break;
                }
            }
            catch (Exception ex) { Fail(a, ex); }
        }

        void PickExe(AppInfo a)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Where is " + a.Def.Name + ".exe?",
                Filter = a.Def.Name + "|" + a.Def.Name + ".exe|Programs (*.exe)|*.exe",
                InitialDirectory = a.Exe != null ? Path.GetDirectoryName(a.Exe) : Directory.Exists(Apps.ProjectsRoot) ? Apps.ProjectsRoot : Apps.Downloads,
                CheckFileExists = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!Path.GetFileName(dlg.FileName).Equals(a.Def.Name + ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    Fail(a, new InvalidOperationException("That's not " + a.Def.Name + ".exe. Pick the file called " + a.Def.Name + ".exe."));
                    return;
                }
                st.Located[a.Def.Name] = dlg.FileName;
                st.Save();
                UpdateForget();
                Apps.Locate(a, st, null);
                if (a.Def.TaskName == null) Apps.ReadStartup(a, st, false); else QueryTasks();
                Rescan();
            }
        }

        // ------------------------------------------------------------ nav
        Rectangle NavItem(int i) => new Rectangle(S(10), S(70) + i * S(40), NavW - S(20), S(36));

        void ShowPage(int i)
        {
            page = i;
            for (int k = 0; k < pagePanels.Count; k++) pagePanels[k].Visible = k == i;
            if (i == 1) { RefreshBackups(); UpdateCheckedText(); }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            using (var b = new SolidBrush(Theme.Nav)) g.FillRectangle(b, 0, 0, NavW, ClientSize.Height);
            Theme.DrawLogo(g, new RectangleF(S(18), S(20), S(30), S(30)));
            TextRenderer.DrawText(g, "Minibar", Theme.UiTitle(13f), new Rectangle(S(56), S(18), S(116), S(34)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            for (int i = 0; i < pages.Length; i++)
            {
                var r = NavItem(i);
                if (i == page) Theme.Fill(g, r, Theme.Card, S(6));
                else if (i == hoverNav) Theme.Fill(g, r, Color.FromArgb(36, 36, 36), S(6));
                if (i == page) Theme.Fill(g, new Rectangle(r.X, r.Y + S(9), S(3), r.Height - S(18)), Theme.Accent, S(1.5f));
                Color nc = i == page ? Theme.Text : Theme.Sub;
                TextRenderer.DrawText(g, navGlyphs[i], fNavIcon, new Rectangle(r.X + S(14), r.Y, S(20), r.Height), nc, Ux.C);
                TextRenderer.DrawText(g, pages[i], fNav, new Rectangle(r.X + S(44), r.Y, r.Width - S(44), r.Height), nc, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, "Version " + Application.ProductVersion, fSmall, new Rectangle(S(18), ClientSize.Height - S(40), NavW - S(20), S(20)), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < pages.Length; i++) if (NavItem(i).Contains(e.Location)) h = i;
            if (h != hoverNav) { hoverNav = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hoverNav != -1) { hoverNav = -1; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < pages.Length; i++) if (NavItem(i).Contains(e.Location)) ShowPage(i);
        }

        static void RefreshAll(Control c) { c.Invalidate(); foreach (Control k in c.Controls) RefreshAll(k); }

        // for the offscreen layout check
        public void ShowPageForTest(int i) => ShowPage(i);
        public List<AppInfo> AppsForTest => apps;
        void SyncStartMenu()
        {
            string self = null;
            try { self = Application.ExecutablePath; } catch { }
            if (self != null && Path.GetFileName(self).IndexOf(".old", StringComparison.OrdinalIgnoreCase) >= 0) self = null;   // mid self-update
            StartMenu.Sync(apps, self, st.StartMenu);
        }

        public AppList ListForTest => list;
        public AppInfo SelfForTest => self;
        public UpdateList UpdatesForTest => ulist;
    }
}
