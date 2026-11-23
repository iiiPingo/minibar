# Minibar

Hub window for the drink apps (Echoa, Fizz, Juice, Pour, Pulp). C# WinForms, .NET Framework 4.8, no packages. It finds each app's exe, shows cards (status, version, uptime, Start/Stop, Start with Windows), checks each app's GitHub releases and updates it with backup and rollback, installs apps that aren't on the PC, backs up and restores every app's settings, and updates itself. Window only; minimize to tray is optional (off by default).

## Layout
- `src/` the app: `Apps.cs` finding, start, stop, Run keys. `Updates.cs` GitHub checks, install, rollback, self-update. `MainWindow.cs`, `AppList.cs`, `UpdateList.cs`, `Controls.cs`, `Theme.cs` UI. `Backup.cs` settings backup/restore. `StartMenu.cs` Start menu shortcuts. `Native.cs` Win32. `Settings.cs` settings.ini.
- `setup/` the installer (`Minibar-Setup-x.y.z.exe`): per-user install to `%LOCALAPPDATA%\Programs\Minibar`, Start menu, clean uninstall in Installed apps. It embeds the built `Minibar.exe`, so build `src` first.
- `tests/minibar` and `tests/setup`: workspace tests, run with mono. `docs/README.txt` plain-words notes ("NEW IN x.y.z" sections). `logo/` logo generator.

## Build and test
- `./build.sh` (Linux: dotnet-sdk-8.0 + mono-devel) builds `Minibar.exe`, `Minibar-Setup-<version>.exe` and `Minibar-<version>.zip` into `release/`. On Windows run `dotnet build src -c Release`, then `dotnet build setup -c Release`.
- Bump `<Version>` in both `src/Minibar.csproj` and `setup/Setup.csproj`, and add a "NEW IN x.y.z" section to `docs/README.txt`.
- Run both test suites before every release. Tests use hooks: `Updater.DownloadOverride`, `ExtractOverride`, `AfterFirstCopy`, `SelfStartOverride`. The 7 CS0649 warnings for those hook fields are expected.

## Release rules (must never break)
- Every release ships three files: `Minibar-Setup-x.y.z.exe`, `Minibar-x.y.z.zip` (`Minibar/Minibar.exe`, `.exe.config`, `README.txt`) and a loose `Minibar.exe`. **The zip is what every Minibar downloads to update itself. Never drop it.** The updater picks a zip with "Minibar" in its name first and never picks the setup exe.
- Each other app needs a GitHub release whose zip contains `<App>.exe`, with the exe's version bumped, or Minibar shows an endless or missing Update button.

## Gotchas
- **Never rely on an exe's file name.** Downloads get renamed (`Minibar (1).exe`, `Pulp (2).exe`). Identify an app by its version resource (ProductName). Self-update must start the exe it actually wrote, check it exists first, and put the old exe back if starting fails.
- Updates never touch `.ini`, `.log`, `.db`, `.csv` or `status.txt`. Any failure mid-update must restore every backed-up file, delete added files and restart the app. Keep 3 backups per app.
- Stop asks apps to close with `<App>.exe --exit` when the installed version supports it (Echoa 1.6, Fizz 1.1, Juice 1.1, Pour 1.2, Pulp 1.3), then falls back to `taskkill`. Fizz needs no admin prompt for `--exit`; only the fallback elevates.
- Settings: `%APPDATA%\Minibar\settings.ini`, `crash.log`. Backups and downloads: `%LOCALAPPDATA%\Minibar`. Never touch another app's user data except the settings backup files it already lists.
- Don't mark the setup's Start menu shortcut with "Added by Minibar" (StartMenu.Sync would manage it).
- The live GitHub API, tar.exe/PowerShell unzip, UAC, shortcuts and tray behaviour can't be tested in the Linux workspace.

## Working rules (all Pingo apps)
- Apps: Echoa, Fizz, Juice, Pour, Pulp, Minibar. Minibar is the hub that finds, installs, starts, stops and updates the others from GitHub releases (iiiPingo/<app>). Don't change how an app is named, found, installed, updated or closed without checking it still works with Minibar.
- Never rely on the exe's file name. Identify an app by its version resource (ProductName), not by `<App>.exe`.
- Every release needs a zip with the app name in its file name, containing `<App>.exe`. Bump the exe's version every release.
- Never touch user data (settings.ini, logs, history, databases).
- This repo is public: never commit secrets, tokens, personal paths, build output (`bin/`, `obj/`, `release/`), crash logs or settings.ini.
- Work on a branch. Don't push, open a PR or publish a release until the owner says so. GitHub releases are published by the owner.
- Add a test for each fix that can be tested off Windows. At the end, list what can only be checked on real Windows.
- If unsure a fix is safe, report it instead of changing it.
- Keep README text in plain words; mention what's new each version.
- Explain changes simply, in plain language.
