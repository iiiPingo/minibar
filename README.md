# Minibar

One window for the drink apps: **Echoa**, **Fizz**, **Juice**, **Pour** and **Pulp**.

- See which apps are running and for how long, start or stop them, and switch "Start with Windows" on or off.
- Open each app's folder, data folder or log.
- Updates page: checks each app's GitHub releases and updates it in one click (with a backup and rollback).
- Back up and restore every app's settings in one zip.
- Install button: anything not on this PC installs from GitHub in one click (so new users only need Minibar).
- Fizz notice: what Fizz needs installed (PawnIO) and the anti-cheat note, with download links.
- Displays page (new in 2.1.0): every screen's real name, resolution, scale and refresh rate, with a brightness slider (WMI for a laptop screen, DDC/CI for external monitors).
- Side menu (new in 1.9.0): device batteries from Juice, Refill's last move with Undo and Pause, an update card with Update all, and a count on the Updates tab.

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

Side menu tests (safe to run on Windows, they use a temp folder): `dotnet build tests/sidemenu -c Release`, then `tests\sidemenu\bin\Release\net48\SideMenuTests.exe` (add `render <folder>` for pictures).

## Third-party

- **Lexend** (Medium and Bold), the font Minibar uses, built into Minibar.exe and the setup. Copyright 2018 The Lexend Project Authors, licensed under the [SIL Open Font License 1.1](src/fonts/OFL.txt). From [Google Fonts](https://fonts.google.com/specimen/Lexend).

## License
PolyForm Noncommercial License 1.0.0: free to use, copy and change for non-commercial purposes, not for selling or other commercial use. See `LICENSE`. Lexend keeps its own license (see above).
