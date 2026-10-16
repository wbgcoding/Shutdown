# Changelog

All notable changes to this project are documented here. The format follows Keep a Changelog; versions follow semantic versioning.

## [0.2.0] - 2026-10-16

### Changed
- Cancelling no longer shows a confirmation message; the countdown card simply disappears.
- An app-run countdown (Force off, or Hibernate) keeps the PC from going to sleep on its own, so it no longer misses its time while unattended. Sleep started by hand still pauses it.
- A date and time must be at least 1 minute ahead.
- The "in ..." summary rounds up to the next minute.
- Always one window: a second start brings the running one to the front.
- Clearer English and German texts; keyboard focus is visible on the command line and messages, and screen readers announce why scheduling is not possible.

### Fixed
- A settings file from a newer version is no longer overwritten.
- A normal start while a copy runs as administrator exits quietly instead of showing an error.
- Starting the app again right after closing it no longer fails silently while the old copy is still exiting.
- A schedule from before a restart no longer shows up as still pending.
- If `shutdown.exe` hangs while scheduling, it is stopped and the schedule is cancelled, so no hidden forced shutdown remains.
- A shutdown handed to Windows stays cancellable after its time, in case Windows runs it late (for example after sleep).
- Typing a minute above 59 or an hour above 23 no longer wraps around silently.
- The command line can be selected and copied in date mode.
- Displayed times follow a time zone change while the app is open.
- Error messages are announced by screen readers.
- `"Theme"` in the settings file is no longer case sensitive.

## [0.1.0] - 2026-10-16

### Added
- First version: schedule shutdown, restart or hibernate by countdown or by date and time, cancel a scheduled shutdown.
- Force switch; without it, and for hibernate, the app runs the countdown itself and asks before closing.
- English or German picked from the Windows language; light/dark mode and accent colour follow Windows; settings in `%APPDATA%\Shutdown`.
