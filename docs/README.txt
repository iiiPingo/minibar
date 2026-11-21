MINIBAR 1.5
Home for your drink apps: Echoa, Fizz, Juice, Pour and Pulp in one window.

OPEN IT
- Double-click Minibar.exe. It's a normal window: open it, use it, close it.
  No tray icon, nothing running in the background once it's closed.
- First run only: if Windows SmartScreen shows up, click "More info" then "Run anyway".
- Opening Minibar again while it's open just brings the window back.
- Optional: Settings > "Minimize to tray". Then minimizing hides Minibar to a tray
  icon (left-click it to bring Minibar back; right-click for Check for updates / Exit).
  The icon only exists while Minibar is hidden. Off by default.

EACH APP CARD
- Status: Running (and for how long), Not running, or Not found.
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
- Minibar never closes an app to update it. If the app is running, the button turns
  into "Updates when you close it": close the app (Stop, or its own Exit) and the
  update goes in right away. Click it again to cancel.
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
Downloads\<App>. Backups folders and *.old.exe files are skipped.
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
                                 release seen on GitHub)
%APPDATA%\Minibar\crash.log      if something goes wrong, look here
%LOCALAPPDATA%\Minibar\backup   old versions kept by updates (3 per app)

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
