param([int]$Minutes = 10, [string]$SetUtc)
# Moves the guest clock (admin shell) for the clock-change tests: -Minutes 10 forward, -Minutes -10 back.
# -SetUtc <ISO time from the host> puts it right again and turns time sync back on
# (w32tm /resync on the test VM can land minutes off).
if ($SetUtc) {
    Set-Date -Date ([DateTime]::Parse($SetUtc).ToUniversalTime().ToLocalTime()) | Out-Null
    Start-Service w32time -ErrorAction SilentlyContinue
    "clock set: $(Get-Date -Format o)"
    return
}
Stop-Service w32time -ErrorAction SilentlyContinue
Set-Date -Adjust ([TimeSpan]::FromMinutes($Minutes)) | Out-Null
"clock now: $(Get-Date -Format o)"
