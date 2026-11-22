MINIBAR 1.7.2
Home for your drink apps: Echoa, Fizz, Juice, Pour and Pulp in one window.

INSTALL (two ways, both update themselves the same way)
- Setup: run Minibar-Setup-x.y.z.exe and click Install. It installs just for you (no
  admin) into %LOCALAPPDATA%\Programs\Minibar, adds Minibar to Start (and the desktop
  if you want), and lists it in Settings > Apps > Installed apps so it uninstalls
  cleanly. Running a newer setup later updates it in place.
- Portable: put Minibar.exe anywhere and run it. Nothing gets installed.
- Already using the portable Minibar? Run the setup: it closes Minibar, installs it and
  opens it again. Your settings carry over. The old Minibar.exe can be deleted afterwards.
- Uninstall (setup copy only): Settings > Apps > Installed apps > Minibar > Uninstall.
  Your drink apps stay installed. Minibar's settings stay too unless you tick "Also delete".

OPEN IT
- Double-click Minibar.exe (or Start > Minibar). It's a normal window: open it, use it, close it.
  No tray icon, nothing running in the background once it's closed.
- First run only: if Windows SmartScreen shows up, click "More info" then "Run anyway".
- Opening Minibar again while it's open just brings the window back.
- Optional: Settings > "Minimize to tray". Then minimizing hides Minibar to a tray
  icon (left-click it to bring Minibar back; right-click for Check for updates / Exit).
  The icon only exists while Minibar is hidden. Off by default.

EACH APP CARD
- Status: Running (and for how long), Not running, or Not installed (with an Install button).
- Version of the exe Minibar found, and a line on what the app is best at.
- Start / Stop. Stop closes the app and anything it started (Pour's downloads,
  Fizz's PresentMon) and clears its tray icon. Fizz runs as admin, so stopping it
  shows one admin prompt.
- Start with Windows: the same switch the app has in its own settings, so the two
  always agree. Minibar remembers the app's own startup command while it's off and
  puts it back exactly when you turn it on again (Juice keeps its --autostart).
  Fizz starts through its "Fizz" logon task; switching that needs one admin prompt,
  and the task has to be set up in Fizz itself the first time.
- Folder: shows the exe in File Explorer.  Data: the app's settings folder.
  Log: opens crash.log (Juice: juice.log). A red dot on Log means the app logged a
  crash in the last 7 days.
- Locate: point Minibar at the exe yourself if it picked the wrong copy or can't find it.

FIZZ
Before you start Fizz the card shows what it needs: the PawnIO driver (with an
"Install PawnIO" button when it's missing and the installer is in Fizz's tools
folder) and the anti-cheat note: the overlay doesn't touch games, but FACEIT's
anti-cheat may block the PawnIO driver, so close Fizz before FACEIT matches.

UPDATES (new in 1.1)
- When Minibar opens it asks GitHub (github.com/iiiPingo/<app>) what the newest
  release is, at most once an hour. "Check now" on the Updates page asks right away.
- A newer version shows as an "Update to vX" button on the app's card and on the
  Updates page. One click: Minibar downloads it, checks it really contains the app,
  keeps a copy of the old files and puts the new ones in place of the old exe.
- If the app is open, Minibar closes it (asking it to close cleanly first), updates it
  and opens it again the way it was running (Pour and Pulp in the tray, and so on).
- Pour in the middle of a download is the exception: the button turns into "Updates
  after downloads finish", and the update goes in by itself once the download is done.
  Click it again to cancel.
- Leftover <App>.old-*.exe files in an app's folder are deleted once the app is closed.
- If anything goes wrong halfway, every file is put back and the app is started
  again as it was. Your settings live in %APPDATA% and are never touched; .ini, .log
  and database files in the app's folder are skipped too.
- "Restore vX" goes back to the version you had before (the last 3 are kept in
  %LOCALAPPDATA%\Minibar\backup, "Old versions" opens that folder).
- "Auto" per app (off by default): install that app's updates by itself whenever
  Minibar finds one.
- "What's new" opens the release page.
- Minibar can update itself the same way once it's on GitHub.
- Juice's releases are on GitHub too; Echoa's live in the old "sip" repo.

HOW MINIBAR FINDS THE APPS
In this order: a file you picked with Locate, the running app, its
start-with-Windows entry, then the newest copy in C:\Claude Projects or
Downloads\<App> (or loose in Downloads, like "Pulp (2).exe"). Backups folders and
*.old.exe files are skipped. A renamed exe counts when its version info says it's the app.
Refresh (or F5) looks again.

KEYBOARD
F5 refresh  ·  Ctrl+1 Apps  ·  Ctrl+2 Updates  ·  Ctrl+3 Settings  ·  Esc close

SETTINGS
Accent colour (the family swatches, or your own), Minimize to tray, Check for
updates when Minibar opens, Look again, Forget picked locations, Open data folder.

FILES
%APPDATA%\Minibar\settings.ini   accent, window position, tray, checkupdates,
                                 path.<App>= (Locate), run.<App>= (startup command
                                 kept while it's off), auto.<App>=, rel.<App>= (last
                                 release seen on GitHub), skip.<App>= (a release you went
                                 back from: Auto skips it)
%APPDATA%\Minibar\crash.log      if something goes wrong, look here
%LOCALAPPDATA%\Minibar\backup   old versions kept by updates (3 per app)
%LOCALAPPDATA%\Programs\Minibar Minibar itself, when installed with the setup

NEW IN 1.7.2
- Renamed downloads work: "Pulp (2).exe" or "Minibar (1).exe" (what Discord and browsers
  save) is found, shown as running, stopped and updated like the normal name. Minibar checks
  what the exe really is, not what it's called. Locate accepts them too.
- Updating a renamed Minibar no longer fails with "The system cannot find the file
  specified". It keeps its name, and if the new version can't start, the old one is put back.
- A loose Minibar.exe (in Downloads or on the Desktop) only replaces itself when it updates.
  Your other files there, like a README.txt, are never touched.
- Opened Minibar straight from the zip? It says so, installs apps into
  %LOCALAPPDATA%\Programs instead of a temporary folder, and asks you to extract it before it
  updates itself.
- Restore + Auto: after "Restore vX", Auto no longer puts the version you left straight back.
  It waits for the next release (Update still works if you click it).
- Pour's "updates after downloads finish" now also goes in while Minibar is minimized.
- Fizz closes cleanly when you press Stop or update it (one admin prompt), instead of being
  force-closed.
- Stopping an app no longer shows a surprise admin prompt.
- Closing Minibar while an update is running: it finishes the update first (hidden), then
  closes. The setup waits for it too, instead of closing Minibar halfway.
- Smaller fixes: leftover tray icons after a force-close, a settings restore that could
  delete saved old versions, settings that could be lost when switching Start with Windows,
  less work while the window is open.

NEW IN 1.7.1
- Settings shows the plain version number (no build code after it).

NEW IN 1.7.0
- Update closes the app for you: one click closes it, updates it and opens it again.
  No more "Updates when you close it". Auto updates work the same way. Pour waits
  until its downloads are done, so nothing gets cut off.
- Minibar-Setup: an installer for anyone who'd rather have Minibar in Start and in
  Installed apps (with a proper uninstall). The plain Minibar.exe still works exactly
  as before, and both kinds keep updating themselves from GitHub.
- Minibar remembers where it last found each app (seen.<App>= in settings.ini), so moving
  Minibar to a new folder, or installing it with the setup, doesn't lose your apps.
- When installed with the setup, the version in Installed apps follows Minibar's updates.

NEW IN 1.6.3
- The line under the title on Apps and Updates is no longer cut off at the bottom.
- The accent colours in Settings stay visible when you make the window wider.
- Simpler highlights on the cards: Fizz "easy to customize", Echoa "quick mic mute",
  Pour without the yt-dlp line.

NEW IN 1.6.2
- Apps that aren't installed show their real logo (a little faded) instead of a letter,
  and say "Not installed" instead of "Not found".

NEW IN 1.6.1
- Minibar.exe works on its own (no zip needed, e.g. sent over Discord). If it sits
  loose in a folder like Downloads, installed apps go right beside it (Downloads\Pulp).

NEW IN 1.6
- Install: an app that isn't on this PC gets an Install button on its card. Minibar
  downloads it from GitHub, puts it in a folder next to Minibar's own (e.g.
  Downloads\Minibar -> Downloads\Pulp) and starts it. Fizz is installed but waits for
  you to read its notice and press Start. So a new user only needs Minibar.

NEW IN 1.5
- Updates wait until you close the app: Minibar never closes an app to update it.
  Close it and the new version goes in by itself (old exe out, new exe in).
- Leftover .old exe files are cleaned up once an app is closed.
- Your apps show up in Windows search. Minibar adds a Start menu shortcut for each app it
  finds (and for itself), with the app's icon, and keeps it pointing at the right exe.
  Settings > "Show the apps in Windows search" turns it off and removes them. Shortcuts
  you made yourself are never touched.

NEW IN 1.4.5
- The scroll bar works on the Apps and Updates pages: drag it, or click along the right
  edge to jump there. It widens a little under the mouse.
- Touchpad scrolling follows your fingers directly (the glide stays for mouse wheels).

NEW IN 1.4.4
- The memory number is gone from the cards and the header. It never matched Task
  Manager closely enough to be useful; the uptime stays.

NEW IN 1.4.3
- Every app icon is the same size with the same soft shadow. Juice and Pour have a
  small transparent border in their icon files, which made them look smaller with a
  dark ring around them; Minibar now trims that border.

NEW IN 1.4.2
- Fizz is the last card (its notice sits at the end). The notice explains FACEIT in a line and has
  buttons for the official PawnIO and PresentMon downloads.
- The (i) on Juice's card is drawn centred.
- Smoother scrolling: each wheel notch glides for 220 ms, based on time, so a slow frame never jumps.

NEW IN 1.4.1
- Under each app's name: what it's best at, instead of the exe's path (Folder still opens it).
- The Running pill also shows how long the app has been running.

NEW IN 1.4
- Juice's card has an (i) next to its name: click it for the list of devices Juice can read.

NEW IN 1.3
- Settings > Back up settings: "Back up..." saves one zip with the settings of every
  app and Minibar (Pulp's link rules, Echoa's device names and volumes, Fizz's layout,
  Juice's devices...). Pulp's clipboard history, logs and downloaded tools aren't in it.
  "Restore..." shows what's in a backup, then puts it back: open apps close for a
  moment and start again. The settings it replaces are kept in
  %LOCALAPPDATA%\Minibar\settings-before-restore.

NEW IN 1.2
- Smooth scrolling (touchpads scroll by the pixel).
- Stop and updates ask an app to close cleanly ("<App>.exe --exit") when the app is new
  enough to understand it (Pulp 1.3, Echoa 1.6, Pour 1.2, Fizz 1.1, Juice 1.1), and only
  force-close it if that doesn't work.

BUILD
source\ is C# for .NET Framework 4.8. On Windows: dotnet build -c Release
