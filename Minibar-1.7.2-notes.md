# Minibar 1.7.2

All 13 fixes are in as Minibar 1.7.2, pushed to `claude/awesome-carson-ckvizy` (commit `2c7b4d4`). There's no GitHub release or PR yet, so nobody gets it until you release it. I tested what I could on Linux, but anything touching Windows itself still needs a check on your PC.

## What users will notice

- **Renamed downloads work.** `Pulp (2).exe` or `Minibar (1).exe` is found, shows as running, and can be stopped, updated and restored. Minibar now checks what the exe says it is, not its file name, and Locate accepts renamed files too.
- **The "cannot find the file specified" update error is fixed.** Minibar keeps its own file name when it updates. If the new version won't start, the old one is put back.
- **A loose `Minibar.exe` only replaces itself.** Other files next to it in Downloads or on the Desktop, like a README.txt, are never touched.
- **Opened from inside the zip:** Minibar says so and installs apps into `%LOCALAPPDATA%\Programs` instead of Temp. It asks to be extracted before it updates itself.
- **Restore and Auto:** after "Restore vX", Auto won't reinstall the version you just left. It waits for the next release; clicking Update still works.
- **Pour's queued update** now also goes in while Minibar is minimized or in the tray.
- **Stopping apps:** Fizz is asked to close cleanly from one admin prompt instead of being force-killed, and other apps no longer trigger a surprise admin prompt.
- **Closing Minibar during an update** finishes the update first (window hidden), then closes. The setup also waits for a running update instead of killing Minibar halfway through.
- **Smaller fixes:** leftover tray icons, a settings restore that could delete saved old versions, a settings race on the Start with Windows switch, and less work on every repaint.

## Build and tests

`./build.sh` builds with 0 warnings and 0 errors. Both test suites pass: Minibar 43 checks (23 new ones for these fixes) and setup 32.

## Still to check on real Windows

- **Fizz's clean close:** Stop on Fizz should show one admin prompt and close it cleanly. This assumes Fizz's manifest requires admin.
- **Tray icons:** stop an old app that gets force-closed and see that its tray icon disappears.
- **Zip detection:** open Minibar from inside the zip and check the warning appears, then try Install.
- **Renamed exes:** a real `Pulp (2).exe` in Downloads should show as found and running, and a renamed `Minibar (1).exe` should update to a newer test release.
- **Close during an update:** start an update, close Minibar, and check the app restarts. Running the setup while an update is going should show "Minibar is finishing an update…".
- **The window itself:** I didn't render it to check how it looks.

Files to test with: `Minibar.exe`, `Minibar-Setup-1.7.2.exe` and `Minibar-1.7.2.zip`. If something misbehaves, `%APPDATA%\Minibar\crash.log` is the first place to look.
