param([string]$Exe)
# Runs instance-lock.ps1 in the signed-in desktop session (a remote shell has no desktop) and prints its output.
$out = Join-Path $env:TEMP "instance-lock-out.txt"
$script = Join-Path $PSScriptRoot "instance-lock.ps1"
Remove-Item $out -ErrorAction SilentlyContinue
$arg = "-ep bypass -WindowStyle Hidden -command `"& '$script' -Exe '$Exe' *> '$out'`""
$a = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arg
$p = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive
Register-ScheduledTask -TaskName "ShutdownInstanceLock" -Action $a -Principal $p -Force | Out-Null
Start-ScheduledTask -TaskName "ShutdownInstanceLock"
Start-Sleep -Seconds 2
while ((Get-ScheduledTask -TaskName "ShutdownInstanceLock").State -eq "Running") { Start-Sleep -Seconds 1 }
Unregister-ScheduledTask -TaskName "ShutdownInstanceLock" -Confirm:$false
Get-Content $out
