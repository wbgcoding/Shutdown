<h1 align="center">Shutdown</h1>

<p align="center">
  A tiny portable Windows tool that schedules a shutdown, restart or hibernate.<br>
  No more seconds math for <code>shutdown /s /t 5400</code>.
</p>

<p align="center">
  <a href="../../releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/wbgcoding/Shutdown?label=release&cacheSeconds=300"></a>
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4">
  <img alt=".NET Framework 4.8" src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4">
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-green"></a>
</p>

<p align="center">
  <img src="docs/screenshot.png" width="437" alt="Shutdown in dark mode with a countdown of 1 hour 30 minutes">
</p>

## Features

- **Shut down, restart or hibernate** after a countdown (hours and minutes, quick picks from 15 minutes to 3 hours) or at an exact date and time.
- **Always know what happens:** right above the time fields you see the exact end time of a countdown, or the time left until a chosen date, and a live countdown runs once scheduled.
- **Cancel with one click** whatever the app scheduled.
- **Fits into Windows:** English or German from the Windows display language, light or dark mode and the accent colour from your Windows settings.
- **Small and private:** one small `.exe` (about 110 KB) to run from any folder, or a setup if you prefer; no network access, no telemetry.

## Download

Choose an installer or a portable copy from the [latest release](../../releases/latest).

- Run `Shutdown-Setup-<version>.exe` to install for your Windows account or for all users. Setup follows the Windows display language by default. Pass `/LANG=en` or `/LANG=de` to select English or German.
- Put `Shutdown-<version>-windows.exe` in any folder and run it directly.

Requires Windows 10 (version 1903 or newer) or Windows 11. The .NET Framework 4.8 it needs is already part of Windows.

The exe is not code signed, so Windows SmartScreen may warn before the first start. Click **More info**, then **Run anyway**. You can compare the file with `SHA256SUMS.txt` from the release:

```bat
certutil -hashfile Shutdown-<version>-windows.exe SHA256
```

## How it works

| Setting | What runs | Can the app be closed? |
|---|---|---|
| Shut down or restart | Windows itself: `shutdown /s /f /t <seconds>` (`/r` for restart) | Yes |
| Hibernate | The app counts down, then runs `shutdown /h /f` | No, keep it open |

Shutting down or restarting always closes open apps, even with unsaved work: Windows forces this whenever a timeout is set. Windows has no timer for hibernation, so the app times it itself. Closing the window then asks first, and the PC is kept from going to sleep on its own until the time is up. If you put the PC to sleep by hand and it wakes more than two minutes after the target time, nothing runs and the app tells you so.

Starting the app a second time brings the open window to the front.

## Files

Settings (`Shutdown.settings.json`) and a small log (rotated at 512 KB, one old copy kept) live in `%APPDATA%\Shutdown`. For an installed copy, use Windows Settings to uninstall Shutdown and optionally delete that data. For a portable copy, delete the exe and optionally delete the data folder.

## Build from source

Requires the .NET SDK 8 or newer (it builds for .NET Framework 4.8 through reference assemblies).

```bat
build.bat
```

This builds `dist\Shutdown.exe` without starting it. Set `NOPAUSE=1` to never wait for a key on errors. A second run with nothing changed reports "up to date" and builds nothing; `build.bat --force` or `FORCE=1` rebuilds.

`release.bat` uses Inno Setup 6 to build `Shutdown-Setup-<version>.exe`. It writes the installer, portable exe and `SHA256SUMS.txt` to `dist\release`.

Run the tests:

```bat
dotnet test tests\Shutdown.Tests
```

## Security

Please report security problems privately, as described in [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE)

<p align="right">© 2026 BGCoding · Made with Claude</p>
