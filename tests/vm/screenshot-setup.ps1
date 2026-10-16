param([string]$Theme = "dark")
# Pins the app settings for the README screenshot (theme, 1:30 countdown, Force on).
# Language and scale come from tools/ShotHost: en-US culture, client area rendered at 200 %.
$dir = Join-Path $env:APPDATA "Shutdown"
New-Item -ItemType Directory -Force $dir | Out-Null
$json = '{"Version":1,"Theme":"' + $Theme + '","Action":"Shutdown","Mode":"Countdown","Force":true,"Hours":1,"Minutes":30}'
[IO.File]::WriteAllText((Join-Path $dir "Shutdown.settings.json"), $json)
"Theme=$Theme"
