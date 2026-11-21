# Minibar

One window for the drink apps: **Echoa**, **Fizz**, **Juice**, **Pour** and **Pulp**.

- See which apps are running and for how long, start or stop them, and switch "Start with Windows" on or off.
- Open each app's folder, data folder or log.
- Updates page: checks each app's GitHub releases and updates it in one click (with a backup and rollback).
- Back up and restore every app's settings in one zip.
- Fizz notice: what Fizz needs installed (PawnIO) and the anti-cheat note, with download links.

## Install

Download `Minibar-x.y.z.zip` from [Releases](../../releases), unzip it anywhere (e.g. `C:\Claude Projects\Minibar`) and run `Minibar.exe`.
If SmartScreen appears: More info > Run anyway (it isn't signed).

Full notes: [docs/README.txt](docs/README.txt)

## Build

C# WinForms, .NET Framework 4.8, no packages.

```
cd src
dotnet build -c Release
```
