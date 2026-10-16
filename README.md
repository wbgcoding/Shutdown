# Shutdown

A tiny portable Windows tool that schedules a shutdown, restart or hibernate without doing the math for `shutdown -s -t <seconds>` yourself.

- Pick a countdown (hours and minutes, quick picks from 15 minutes to 4 hours) or an exact date and time.
- Shows when the PC turns off, the time left and the exact `shutdown.exe` command it runs.
- Cancel a scheduled shutdown with one click, also one started outside the app.
- English and German, light and dark mode following Windows.
- One small `.exe`, no install. Runs on Windows 10 and 11 (uses the .NET Framework 4.8 that ships with Windows).

## How it works

With **Force** on, shutdown and restart are handed to Windows (`shutdown /s /f /t <seconds>`), so the app can be closed afterwards.

Windows always forces apps closed when a timeout is set and has no timer for hibernate. So with **Force** off, or for **Hibernate**, the app counts down itself and then runs the command with `/t 0`. Keep the window open in that case; closing it asks first.

Settings are stored in `Shutdown.settings.json` next to the exe (or in `%APPDATA%\Shutdown` if that folder is read-only). A small log lives in `%APPDATA%\Shutdown\logs`. Deleting the exe leaves that folder behind; remove it by hand if you like.

## Build

Requires the .NET SDK 8 or newer.

```
build.bat
```

Builds `out\Shutdown.exe` and starts it. `NOSTART=1` skips the start, `NOPAUSE=1` never waits for a key on errors. Tests: `dotnet test tests\Shutdown.Tests`.

© 2026 BGCoding
