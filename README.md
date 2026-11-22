# Minibar

One window for the drink apps: **Echoa**, **Fizz**, **Juice**, **Pour** and **Pulp**.

- See which apps are running and for how long, start or stop them, and switch "Start with Windows" on or off.
- Open each app's folder, data folder or log.
- Updates page: checks each app's GitHub releases and updates it in one click (with a backup and rollback).
- Back up and restore every app's settings in one zip.
- Install button: anything not on this PC installs from GitHub in one click (so friends only need Minibar).
- Fizz notice: what Fizz needs installed (PawnIO) and the anti-cheat note, with download links.

## Install

From [Releases](../../releases), pick one:

- **`Minibar-Setup-x.y.z.exe`**: installs Minibar just for you (no admin) into `%LOCALAPPDATA%\Programs\Minibar`, with a Start menu entry and a clean uninstall in Installed apps.
- **`Minibar.exe`** (or `Minibar-x.y.z.zip`): portable, run it from any folder.

Both update themselves from here (Minibar downloads the zip). If SmartScreen appears: More info > Run anyway (it isn't signed).

Full notes: [docs/README.txt](docs/README.txt)

## Build

C# WinForms, .NET Framework 4.8, no packages.

```
cd src
dotnet build -c Release
```

The setup carries the built Minibar.exe inside it, so build `src` first, then `dotnet build setup -c Release`.
On Linux, `./build.sh` does both and writes the release files (zip, setup, exe) into `release/`.
