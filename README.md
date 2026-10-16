# Shutdown

A tiny portable Windows tool that schedules a shutdown, restart or hibernate without doing the math for `shutdown /s /t <seconds>` yourself.

![Shutdown scheduled in 1 hour 30 minutes](docs/screenshot.png)

## Features

- Pick a countdown (hours and minutes, quick picks from 15 minutes to 4 hours) or an exact date and time.
- Shows when the PC turns off, the time left and the exact `shutdown.exe` command it runs.
- Cancel what it scheduled with one click.
- English or German from the Windows language; light or dark mode and the accent colour from Windows.
- One small `.exe`, no install, no network access. Runs on Windows 10 (1903 or newer) and Windows 11 with the .NET Framework 4.8 that ships with Windows.

## Download

Get `Shutdown.exe` from the [latest release](../../releases/latest) and run it from any folder. The exe is not code signed, so Windows SmartScreen may ask once before the first start ("More info", then "Run anyway").

## How it works

With **Force** on, shutdown and restart are handed to Windows (`shutdown /s /f /t <seconds>`), so the app can be closed afterwards.

Windows always forces apps closed when a timeout is set and has no timer for hibernate. So with **Force** off, or for **Hibernate**, the app counts down itself and then runs the command with `/t 0`. Keep the window open in that case; closing it asks first. While such a countdown runs, the app keeps the PC from going to sleep.

Settings (`Shutdown.settings.json`) and a small log (rotated at 512 KB, one old copy kept) live in `%APPDATA%\Shutdown`; nothing is written next to the exe. Deleting the exe leaves that folder behind; remove it by hand if you like.

## Build

Requires the .NET SDK 8 or newer.

```
build.bat
```

Builds `dist\Shutdown.exe` and starts it. `NOSTART=1` skips the start, `NOPAUSE=1` never waits for a key on errors. Tests: `dotnet test tests\Shutdown.Tests`.

© 2026 BGCoding
