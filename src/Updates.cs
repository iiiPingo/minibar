using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Minibar
{
    // the newest release of one app on GitHub
    class Release
    {
        public string Repo, Tag, AssetUrl, AssetName, Published;   // Published = yyyy-MM-dd
        public string Problem;    // null = fine; otherwise why there's nothing to show ("Not on GitHub yet", "Offline", ...)
        public DateTime Checked;

        public Version Ver => Updater.ParseVersion(Tag);
        public string PageUrl => "https://github.com/iiiPingo/" + Repo + "/releases" + (Tag != null ? "/tag/" + Uri.EscapeDataString(Tag) : "");
        public string Label => Tag == null ? null : Tag.TrimStart('v', 'V');

        // rel.<App>= repo|tag|asset url|asset name|published|problem
        public string Pack() => string.Join("|", new[] { Repo, Tag, AssetUrl, AssetName, Published, Problem }.Select(x => (x ?? "").Replace("|", "/")));
        public static Release Unpack(string s)
        {
            var p = (s ?? "").Split('|');
            if (p.Length < 6) return null;
            string N(string x) => x.Length == 0 ? null : x;
            return new Release { Repo = N(p[0]), Tag = N(p[1]), AssetUrl = N(p[2]), AssetName = N(p[3]), Published = N(p[4]), Problem = N(p[5]) };
        }
    }

    enum UpdState { Unknown, UpToDate, Available, Ahead, NoVersion, NotOnGitHub, Problem }

    static class Updater
    {
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Minibar");
        public static string BackupRoot => Path.Combine(Root, "backup");

        // test hooks
        public static Func<string, string, bool> ExtractOverride;   // (zip, dir) -> ok
        public static Action AfterFirstCopy;                         // throw here to test the undo
        public static Func<string, string, Action<int>, string> DownloadOverride;
        public static Action<string, string> SelfStartOverride;      // (exe, args)

        static readonly object installGate = new object();

        // ------------------------------------------------------------ versions
        public static Version ParseVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var m = Regex.Match(tag, @"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?");
            if (!m.Success) return null;
            int G(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
            return new Version(G(1), G(2), G(3), G(4));
        }

        // the version shown on the card (from the exe's version resource); null when the exe has none (Juice)
        public static Version Installed(AppInfo a) => a.Exe == null ? null : ParseVersion(a.Version);

        public static string Nice(string isoDate) =>
            DateTime.TryParseExact(isoDate ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString(d.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture) : isoDate;

        public static UpdState State(AppInfo a)
        {
            var r = a.Rel;
            if (r == null) return UpdState.Unknown;
            if (r.Problem == "missing") return UpdState.NotOnGitHub;
            if (r.Problem != null || r.Ver == null) return UpdState.Problem;
            var have = Installed(a);
            if (have == null) return UpdState.NoVersion;
            int c = r.Ver.CompareTo(have);
            return c > 0 ? (r.AssetUrl != null ? UpdState.Available : UpdState.Problem) : c < 0 ? UpdState.Ahead : UpdState.UpToDate;
        }

        public static bool CanUpdate(AppInfo a) => a.Found && !a.Updating && State(a) == UpdState.Available;

        // ------------------------------------------------------------ asking GitHub
        static HttpWebRequest Req(string url, int timeoutMs)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
            var q = (HttpWebRequest)WebRequest.Create(url);
            q.UserAgent = "Minibar/" + System.Windows.Forms.Application.ProductVersion + " (+https://github.com/iiiPingo)";
            q.Accept = "application/vnd.github+json";
            q.Timeout = timeoutMs;
            q.ReadWriteTimeout = timeoutMs;
            q.AllowAutoRedirect = true;
            q.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return q;
        }

        public static Release Check(AppDef d)
        {
            Release last = null;
            foreach (var repo in d.Repos ?? new string[0])
            {
                var r = CheckRepo(d, repo);
                if (r.Problem != "missing") return r;
                last = last ?? r;   // keep the first repo's name for the "not on GitHub" message
            }
            return last ?? new Release { Problem = "missing", Checked = DateTime.UtcNow };
        }

        static Release CheckRepo(AppDef d, string repo)
        {
            var r = new Release { Repo = repo, Checked = DateTime.UtcNow };
            try
            {
                string json;
                var q = Req("https://api.github.com/repos/iiiPingo/" + repo + "/releases/latest", 15000);
                using (var resp = (HttpWebResponse)q.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    json = rd.ReadToEnd();
                Parse(json, d.Name, r);
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse hr)
            {
                int code = (int)hr.StatusCode;
                string remaining = hr.Headers["X-RateLimit-Remaining"];
                if (code == 404) r.Problem = "missing";
                else if ((code == 403 || code == 429) && remaining == "0") r.Problem = "GitHub asked Minibar to wait a bit (too many checks). Try again later.";
                else r.Problem = "GitHub answered " + code + ".";
                hr.Dispose();
            }
            catch (WebException) { r.Problem = "Couldn't reach GitHub. Are you online?"; }
            catch (Exception ex) { r.Problem = "Couldn't check: " + ex.Message; }
            return r;
        }

        static string Json(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }

        public static void Parse(string json, string app, Release r)
        {
            r.Tag = Json(json, "tag_name");
            string pub = Json(json, "published_at");
            if (pub != null && DateTime.TryParse(pub, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                r.Published = dt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var urls = Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"").Cast<Match>().Select(m => Regex.Unescape(m.Groups[1].Value)).ToList();
            string Name(string u) { try { return Uri.UnescapeDataString(u.Substring(u.LastIndexOf('/') + 1)); } catch { return u; } }
            string pick =
                urls.FirstOrDefault(u => Name(u).EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Name(u).IndexOf(app, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? urls.FirstOrDefault(u => Name(u).EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ?? urls.FirstOrDefault(u => Name(u).Equals(app + ".exe", StringComparison.OrdinalIgnoreCase));
            r.AssetUrl = pick;
            r.AssetName = pick == null ? null : Name(pick);
            if (r.Tag == null) r.Problem = "GitHub's answer had no version.";
        }

        // ------------------------------------------------------------ downloading
        static string Download(string url, string dir, string name, Action<int> pct)
        {
            if (DownloadOverride != null) return DownloadOverride(url, dir, pct);
            string file = Path.Combine(dir, name);
            var q = Req(url, 30000);
            q.Accept = "application/octet-stream";
            using (var resp = (HttpWebResponse)q.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = File.Create(file))
            {
                long total = resp.ContentLength, done = 0;
                var buf = new byte[81920];
                int n, last = -1;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    done += n;
                    if (done > 300L * 1024 * 1024) throw new Exception("The download is far bigger than any of these apps.");
                    if (total > 0) { int p = (int)(done * 100 / total); if (p != last) { last = p; pct(p); } }
                }
                if (total > 0 && done != total) throw new Exception("The download was cut off. Try again.");
            }
            return file;
        }

        // ------------------------------------------------------------ unpacking (Windows' own tar.exe, then PowerShell; no System.IO.Compression)
        static bool Extract(string zip, string dir)
        {
            if (ExtractOverride != null) return ExtractOverride(zip, dir);
            Directory.CreateDirectory(dir);
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            if (File.Exists(tar) && Run(tar, "-xf \"" + zip + "\" -C \"" + dir + "\"", 120000) == 0) return true;
            string ps = "Expand-Archive -LiteralPath '" + zip.Replace("'", "''") + "' -DestinationPath '" + dir.Replace("'", "''") + "' -Force";
            return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + ps + "\"", 180000) == 0;
        }

        static int Run(string exe, string args, int timeout)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    p.OutputDataReceived += (o, e) => { };
                    p.ErrorDataReceived += (o, e) => { };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeout)) { try { p.Kill(); } catch { } return -1; }
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        // downloads + unpacks + checks the release; returns the folder that holds <App>.exe
        static string Prepare(AppDef d, Release rel, string work, Action<string> progress)
        {
            progress("Downloading…");
            string name = rel.AssetName ?? (d.Name + ".zip");
            string file = Download(rel.AssetUrl, work, name, p => progress("Downloading " + p + "%"));
            progress("Checking…");
            string payload;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                payload = Path.Combine(work, "payload");
                Directory.CreateDirectory(payload);
                File.Move(file, Path.Combine(payload, d.Name + ".exe"));
            }
            else
            {
                if (!IsZip(file)) throw new Exception("The download isn't a zip file.");
                string x = Path.Combine(work, "x");
                if (!Extract(file, x)) throw new Exception("Couldn't unpack the update.");
                string root = Path.GetFullPath(x).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                var exes = Directory.GetFiles(x, d.Name + ".exe", SearchOption.AllDirectories)
                    .Where(f => Path.GetFullPath(f).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Count(c => c == '\\' || c == '/')).ToList();
                if (exes.Count == 0) throw new Exception("That release doesn't contain " + d.Name + ".exe, so nothing was changed.");
                payload = Path.GetDirectoryName(exes[0]);
            }
            string exe = Path.Combine(payload, d.Name + ".exe");
            if (!IsExe(exe)) throw new Exception("The " + d.Name + ".exe in that release looks broken, so nothing was changed.");
            return payload;
        }

        static bool IsZip(string f) { try { using (var s = File.OpenRead(f)) return s.ReadByte() == 'P' && s.ReadByte() == 'K'; } catch { return false; } }
        static bool IsExe(string f) { try { using (var s = File.OpenRead(f)) return s.Length > 1024 && s.ReadByte() == 'M' && s.ReadByte() == 'Z'; } catch { return false; } }

        // files that belong to him, not to the app: never overwritten by an update
        static bool Keep(string rel)
        {
            string ext = Path.GetExtension(rel).ToLowerInvariant();
            return ext == ".ini" || ext == ".log" || ext == ".db" || ext == ".db-wal" || ext == ".db-shm" || ext == ".csv"
                || Path.GetFileName(rel).Equals("status.txt", StringComparison.OrdinalIgnoreCase);
        }

        static List<string> Files(string dir)
        {
            string root = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetFullPath(f).Substring(root.Length))
                .Where(r => !Keep(r)).ToList();
        }

        // ------------------------------------------------------------ first install (an app that isn't on this PC yet)
        // Goes next to Minibar's own folder (Downloads\Minibar -> Downloads\Pulp), where Minibar looks anyway;
        // if that folder can't be written, %LOCALAPPDATA%\Programs\<App>. Returns the new exe.
        public static string InstallNew(AppDef d, Release rel, string selfExe, Action<string> progress)
        {
            progress("Asking GitHub…");
            if (rel?.AssetUrl == null) rel = Check(d);
            if (rel?.AssetUrl == null)
                throw new Exception(rel?.Problem == "missing" ? d.Name + " has no download on GitHub yet." : "Couldn't reach GitHub. Are you online?");
            lock (installGate)
            {
                string work = Path.Combine(Root, "updates", d.Name + "-new-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                try
                {
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                    Directory.CreateDirectory(work);
                    string payload = Prepare(d, rel, work, progress);
                    progress("Installing…");
                    string dir = InstallDir(d, selfExe);
                    foreach (var relPath in Files(payload))
                    {
                        string dest = Path.Combine(dir, relPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        if (Keep(relPath) && File.Exists(dest)) continue;
                        CopyRetry(Path.Combine(payload, relPath), dest);
                    }
                    string exe = Path.Combine(dir, d.Name + ".exe");
                    if (!File.Exists(exe)) throw new Exception("Something went wrong copying " + d.Name + ".");
                    return exe;
                }
                finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { } }
            }
        }

        static string InstallDir(AppDef d, string selfExe)
        {
            var tries = new List<string>();
            try
            {
                tries.Add(Path.Combine(Apps.AppsHome(selfExe), d.Name));
            }
            catch { }
            tries.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", d.Name));
            foreach (var dir in tries)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string probe = Path.Combine(dir, ".minibar-write-test");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    return dir;
                }
                catch { }
            }
            throw new Exception("Minibar couldn't find a folder it may write to for " + d.Name + ".");
        }

        // ------------------------------------------------------------ installing
        public static void Install(AppInfo a, Action<string> progress)
        {
            var rel = a.Rel;
            if (a.Exe == null) throw new Exception("Minibar can't find " + a.Def.Name + ".exe.");
            if (rel?.AssetUrl == null) throw new Exception("There's nothing to download for " + a.Def.Name + ".");
            lock (installGate)
            {
                string work = Path.Combine(Root, "updates", a.Def.Name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                try
                {
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                    Directory.CreateDirectory(work);
                    string payload = Prepare(a.Def, rel, work, progress);
                    var have = Apps.FileVer(a.Exe);
                    var got = Apps.FileVer(Path.Combine(payload, a.Def.Name + ".exe"));
                    if (have.Major + have.Minor + Math.Max(0, have.Build) > 0 && got.Major + got.Minor + Math.Max(0, got.Build) > 0 && got < have)
                        throw new Exception("The release on GitHub is older than the " + a.Def.Name + " you have, so nothing was changed.");
                    Apply(a, payload, progress, "v" + (Apps.VersionText(a.Exe) ?? "unknown"), null);
                }
                finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { } }
            }
        }

        // copies payload over the app's folder with a backup; any failure puts every file back.
        // removeOnSuccess: files to delete after copying (a rollback removes what the update had added)
        static void Apply(AppInfo a, string payload, Action<string> progress, string backupLabel, List<string> removeOnSuccess)
        {
            string appDir = Path.GetDirectoryName(a.Exe);
            var files = Files(payload);
            if (files.Count == 0) throw new Exception("Nothing to install.");

            var pids = Apps.PidsNow(a.Def);
            bool wasRunning = pids.Count > 0;
            string args = Apps.RestartArgs(a);
            if (wasRunning)
            {
                progress("Closing " + a.Def.Name + "…");
                a.Pids = pids;
                Apps.Stop(a);
            }
            try
            {
                WaitUnlocked(a.Exe);
                progress("Installing…");
                string bdir = Path.Combine(BackupRoot, a.Def.Name, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Safe(backupLabel));
                var backed = new List<string>();
                var created = new List<string>();
                var removedBackedUp = new List<string>();
                try
                {
                    Directory.CreateDirectory(bdir);
                    foreach (var rel in files.Concat(removeOnSuccess ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        string dest = Path.Combine(appDir, rel);
                        if (!File.Exists(dest)) continue;
                        string b = Path.Combine(bdir, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(b));
                        File.Copy(dest, b, true);
                        backed.Add(rel);
                    }
                    WriteManifest(bdir, a, created, backupLabel);
                    bool first = true;
                    foreach (var rel in files.OrderBy(r => r.Equals(a.Def.Name + ".exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0))   // exe last
                    {
                        string dest = Path.Combine(appDir, rel);
                        bool existed = File.Exists(dest);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        CopyRetry(Path.Combine(payload, rel), dest);
                        if (!existed) created.Add(rel);
                        if (first) { first = false; AfterFirstCopy?.Invoke(); }
                    }
                    foreach (var rel in removeOnSuccess ?? new List<string>())
                    {
                        string dest = Path.Combine(appDir, rel);
                        if (File.Exists(dest)) { File.Delete(dest); removedBackedUp.Add(rel); }
                    }
                    WriteManifest(bdir, a, created, backupLabel);
                }
                catch (Exception ex)
                {
                    progress("Undoing…");
                    string undo = Restore(appDir, bdir, backed, created);
                    try { Directory.Delete(bdir, true); } catch { }
                    throw new Exception("The update didn't go through, so " + a.Def.Name + " was put back as it was. (" + ex.Message + ")" + (undo != null ? " " + undo : ""));
                }
                Prune(a.Def.Name, 3);
            }
            finally
            {
                if (wasRunning)
                {
                    progress("Starting " + a.Def.Name + "…");
                    try { Apps.Start(a, args); } catch (Exception ex) { Log.Crash(ex); }
                }
            }
        }

        static string Safe(string s) => Regex.Replace(s ?? "", @"[^\w.\-]", "_");

        static void CopyRetry(string src, string dest)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(src, dest, true); return; }
                catch (IOException) when (i < 6) { Thread.Sleep(500); }
                catch (UnauthorizedAccessException) when (i < 6) { Thread.Sleep(500); }
            }
        }

        static void WaitUnlocked(string exe)
        {
            for (int i = 0; i < 50; i++)
            {
                try { using (new FileStream(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return; }
                catch (FileNotFoundException) { return; }
                catch (IOException) { Thread.Sleep(200); }
                catch (UnauthorizedAccessException) { Thread.Sleep(200); }
            }
            throw new Exception(Path.GetFileName(exe) + " is still in use.");
        }

        static string Restore(string appDir, string bdir, List<string> backed, List<string> created)
        {
            var failed = new List<string>();
            foreach (var rel in backed)
                try { CopyRetry(Path.Combine(bdir, rel), Path.Combine(appDir, rel)); } catch { failed.Add(rel); }
            foreach (var rel in created)
                try { File.Delete(Path.Combine(appDir, rel)); } catch { }
            return failed.Count == 0 ? null : "Couldn't put back: " + string.Join(", ", failed) + ". The old files are in " + bdir;
        }

        // ------------------------------------------------------------ backups and rolling back
        static void WriteManifest(string bdir, AppInfo a, List<string> created, string label)
        {
            var sb = new StringBuilder();
            sb.AppendLine("app=" + a.Def.Name);
            sb.AppendLine("folder=" + Path.GetDirectoryName(a.Exe));
            sb.AppendLine("was=" + label);
            foreach (var c in created) sb.AppendLine("added=" + c);
            File.WriteAllText(Path.Combine(bdir, "minibar-backup.txt"), sb.ToString(), Encoding.UTF8);
        }

        public static string LatestBackup(AppInfo a)
        {
            try
            {
                string d = Path.Combine(BackupRoot, a.Def.Name);
                if (!Directory.Exists(d)) return null;
                return Directory.GetDirectories(d).Where(x => File.Exists(Path.Combine(x, "minibar-backup.txt")))
                    .Where(x => a.Exe != null && Apps.SameFile(ReadKey(x, "folder"), Path.GetDirectoryName(a.Exe)))
                    .OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal).FirstOrDefault();
            }
            catch { return null; }
        }

        public static string BackupLabel(string bdir) => ReadKey(bdir, "was");

        static string ReadKey(string bdir, string key)
        {
            try
            {
                foreach (var l in File.ReadAllLines(Path.Combine(bdir, "minibar-backup.txt")))
                    if (l.StartsWith(key + "=")) return l.Substring(key.Length + 1);
            }
            catch { }
            return null;
        }

        public static void Rollback(AppInfo a, Action<string> progress)
        {
            lock (installGate)
            {
                string bdir = LatestBackup(a) ?? throw new Exception("There's no earlier version to go back to.");
                var added = File.ReadAllLines(Path.Combine(bdir, "minibar-backup.txt")).Where(l => l.StartsWith("added=")).Select(l => l.Substring(6)).ToList();
                // copy the backup into a scratch folder first: Apply makes a fresh backup of the current files
                string work = Path.Combine(Root, "updates", a.Def.Name + "-rollback");
                try
                {
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                    foreach (var f in Directory.GetFiles(bdir, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(bdir.Length).TrimStart('\\', '/');
                        if (rel == "minibar-backup.txt") continue;
                        string t = Path.Combine(work, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(t));
                        File.Copy(f, t, true);
                    }
                    Apply(a, work, progress, "v" + (Apps.VersionText(a.Exe) ?? "unknown"), added);
                    try { Directory.Delete(bdir, true); } catch { }   // used up: back on that version now
                }
                finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { } }
            }
        }

        static void Prune(string app, int keep)
        {
            try
            {
                string d = Path.Combine(BackupRoot, app);
                foreach (var old in Directory.GetDirectories(d).OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal).Skip(keep))
                    try { Directory.Delete(old, true); } catch { }
            }
            catch { }
        }

        // ------------------------------------------------------------ Minibar updating itself
        // A running exe can be renamed but not overwritten: Minibar.exe -> Minibar.old.exe, new files in, start the new one, quit.
        public static void SelfUpdate(AppInfo self, Action<string> progress)
        {
            var rel = self.Rel;
            if (rel?.AssetUrl == null) throw new Exception("There's nothing to download.");
            lock (installGate)
            {
                string work = Path.Combine(Root, "updates", "Minibar-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(work);
                try
                {
                    string payload = Prepare(self.Def, rel, work, progress);
                    string exe = self.Exe, dir = Path.GetDirectoryName(exe), old = Path.Combine(dir, "Minibar.old.exe");
                    progress("Installing…");
                    try { if (File.Exists(old)) File.Delete(old); } catch { }
                    File.Move(exe, old);
                    var done = new List<Tuple<string, string>>();   // (dest, backup or null)
                    try
                    {
                        foreach (var r in Files(payload))
                        {
                            string dest = Path.Combine(dir, r);
                            if (Apps.SameFile(dest, exe)) { File.Copy(Path.Combine(payload, r), dest, false); done.Add(Tuple.Create(dest, (string)null)); continue; }
                            string bak = File.Exists(dest) ? dest + ".minibar-bak" : null;
                            if (bak != null) File.Copy(dest, bak, true);
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            File.Copy(Path.Combine(payload, r), dest, true);
                            done.Add(Tuple.Create(dest, bak));
                        }
                    }
                    catch
                    {
                        foreach (var t in done) try { if (t.Item2 != null) File.Copy(t.Item2, t.Item1, true); else File.Delete(t.Item1); } catch { }
                        try { if (!File.Exists(exe)) File.Move(old, exe); } catch { }
                        throw;
                    }
                    foreach (var t in done) try { if (t.Item2 != null) File.Delete(t.Item2); } catch { }
                    string args = "--after-update " + Process.GetCurrentProcess().Id;
                    if (SelfStartOverride != null) SelfStartOverride(exe, args);
                    else Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = dir })?.Dispose();
                }
                finally { try { Directory.Delete(work, true); } catch { } }
            }
        }

        public static void CleanAfterSelfUpdate()
        {
            try
            {
                string dir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                foreach (var old in Directory.GetFiles(dir, "Minibar.old*.exe"))
                    for (int i = 0; i < 10 && File.Exists(old); i++) { try { File.Delete(old); } catch { Thread.Sleep(300); } }
            }
            catch { }
        }
    }
}
