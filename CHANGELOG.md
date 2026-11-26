# Changelog

All notable changes to this project are documented here. The format follows Keep a Changelog; versions follow semantic versioning.

## [1.3.0] - unreleased

### Fixed
- With a light Windows accent colour (for example yellow), the start button text is now black instead of hard-to-read white.
- A settings file that is only locked for a moment (for example by a virus scanner) is no longer replaced with defaults, so a saved schedule stays.
- Starting Shutdown while a copy already runs as administrator now says so instead of doing nothing.
- A hanging shutdown.exe blocks the window for at most 5 seconds instead of 15.
- When startup takes more than one second, the log shows how long each step took through the first frame.

### Changed
- Dark mode looks like Windows 11 itself: buttons and number fields stand out by a slightly lighter fill instead of bright outlines. Light mode is less glaring, with clearly outlined controls set slightly darker than their panel.
- Icons such as the stopwatch and the restart arrow are drawn smoothly instead of with a jagged, flattened bottom edge.
- The schedule button fills its row, with Cancel schedule on the pending card.
- The schedule button shows Reschedule while a schedule is pending.
- You can add 15 minutes or 1 hour to a schedule from its pending card.
- The window title shows the time left, and the taskbar button shows the countdown progress.
- In the last minute, the window comes into view and flashes without taking keyboard focus.
- Windows names Shutdown in its warning for a scheduled shutdown or restart.
- Countdown minutes carry into hours as you step or type, and the target time updates while you type.
- Press Enter to schedule the selected action.
- The preset that matches the countdown stays highlighted.
- Dates shown for the current day and the next day use Today and Tomorrow.
- Action and timing choices have tooltips that explain what they do.
- A badge explains when hibernation is unavailable and can be reached with the keyboard.
- Sleep is available as a fourth action, with a countdown that runs in the app.
- The command line can schedule or cancel an action, including when Shutdown is already open.
- The taskbar jump list can schedule a shutdown in 30 minutes or 1 hour, or cancel it.
- Minimizing moves Shutdown to the notification area, where its menu can reopen the window, add 15 minutes, cancel a schedule or exit.
- Releases include a setup program for the current user or all users, with an option to copy a portable executable.

## [1.2.2] - 2026-11-21

### Fixed
- If a new time became too close while the old schedule was being cancelled, the app now says that the old schedule is gone and no new one was set.
- A newer version no longer takes over while this window holds a schedule that could not be saved, since the new version would not show it.
- The default time on the date page is one real hour ahead, also on the days the clock changes.
- A settings file that cannot be read is kept as `Shutdown.settings.json.bad` before it is replaced with defaults.
- Number fields: a touchpad swipe steps once per wheel notch instead of jumping, Space is ignored, and pasted numbers with spaces or a line break around them are accepted.
- Keyboard focus stays in the window when the previous-day button or Cancel schedule turns itself off.
- Switching high contrast on or off applies the new colours at once.

### Changed
- `build.bat` only builds and no longer starts the app; `NOSTART` is gone.

## [1.2.1] - 2026-11-14

### Changed
- The window has its own title bar with a larger logo and title; only Minimize and Close remain, the unused Maximize button is gone.
- Starting a newer version while an older one is open closes the older one and opens the new one. Only while a hibernate countdown runs does the open window stay, and it says why.
- The app icon is redrawn: a shorter stem with a clear gap to the ring, sharp at small sizes.
- On the date page, Today sits on the left and Tomorrow on the right at the same width, so the date between the arrows is centred.
- The hibernate note has a clearer info badge and an accent border.
- The power symbol on the buttons is drawn as a vector, so its ring stays round at every display scale.
- The cross on Cancel schedule is larger and level with the text.

### Fixed
- Starting the app while it is already open brings the open window to the front instead of leaving it behind other windows.

## [1.2.0] - 2026-11-07

### Changed
- The right-click menu of the number fields and messages matches the light and dark look and the app language instead of the grey Windows menu.
- In dark mode, buttons and selected options use a deeper accent blue with white text.
- The target time in the middle uses a calm neutral colour instead of blue, and the hibernate note uses the same bright text colour.
- Restart uses a cleaner circular arrow icon.
- English texts say "hibernation" where a noun is meant, such as "Schedule hibernation".
- German texts write days out ("1 Tag", "2 Tage") instead of "Tg.".
- The time left on the scheduled card is shown in the normal text colour, turns yellow in the last 5 minutes and red in the last minute.
- The version and copyright line at the bottom of the window is gone; the window is a little smaller.
- Dates and times follow the Windows regional format, also when it differs from the display language.

### Fixed
- In high contrast mode, a button under the mouse pointer keeps readable text, and a disabled Start button no longer looks selected.
- A hibernate countdown counts on the time since Windows started, so setting the clock can no longer start it early.
- During the repeated hour when summer time ends, a time later in that hour can still be scheduled.
- If shutdown.exe hangs and the schedule cannot be taken back, the card and Cancel stay available.
- A schedule that could not be saved says so instead of disappearing after a restart of the app.
- The close warning disappears once the hibernate countdown has run, and comes to the front when the window is closed from the taskbar while minimized.
- Screen readers announce a new message also the first time it appears.
- The numbers in the time fields sit in the middle between the arrows, and the colon sits level with them.
- The window is no longer pulled onto the main screen when it sits on a screen below it.
- Native Windows libraries are loaded only from the system folder.

## [1.1.0] - 2026-10-29

### Changed
- The target date and time (or the time left, for a date) is shown larger, right above the time fields.
- Shutdown and restart are always forced (`/f`); the Force switch is gone. If you used them with Force off, open apps are now closed without asking to save, so save your work before the time is up. Only hibernate is still timed by the app and needs the window open.
- The countdown quick picks are 15 min, 30 min, 1 h, 2 h and 3 h, and the command line is gone.

### Fixed
- A typed value above a field's maximum (such as 75 minutes) now counts as the maximum at once, so the shown target time no longer lags behind the field.

## [1.0.0] - 2026-10-16

### Changed
- Cancelling no longer shows a confirmation message; the countdown card simply disappears.
- An app-run countdown (Force off, or Hibernate) keeps the PC from going to sleep on its own, so it no longer misses its time while unattended. Sleep started by hand still pauses it.
- A date and time must be at least 1 minute ahead.
- The "in ..." summary rounds up to the next minute.
- Always one window: a second start brings the running one to the front.
- Clearer English and German texts; keyboard focus is visible on the command line and messages, and screen readers announce why scheduling is not possible.

### Fixed
- A schedule handed to Windows stays visible and cancellable after the clock is set, and its time left stays right.
- After a Fast Startup boot, a schedule from before is no longer shown as still pending.
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
