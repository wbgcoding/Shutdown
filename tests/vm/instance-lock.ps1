param([string]$Exe)
# A process that is still tearing down keeps the instance mutex alive without running a window.
# Simulate that state: hold an open handle (not owned) and start the app.
$name = [IO.Path]::GetFileNameWithoutExtension($Exe)
function WaitWindow([int]$ms) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) {
        $p = Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }
        if ($p) { return $p | Select-Object -First 1 }
        Start-Sleep -Milliseconds 50
    }
    return $null
}
Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Case 1: lingering handle, nobody owns the lock.
$m = New-Object System.Threading.Mutex($false, 'Local\BGCoding.Shutdown.Instance')
Start-Process $Exe | Out-Null
if (WaitWindow 8000) { "lingering handle: window OK" } else { "lingering handle: NO WINDOW" }
$m.Dispose()
Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Case 2: owner releases late (exit still running) after the new start already began.
$m = New-Object System.Threading.Mutex($true, 'Local\BGCoding.Shutdown.Instance')
Start-Process $Exe | Out-Null
Start-Sleep -Milliseconds 1200
$m.ReleaseMutex(); $m.Dispose()
if (WaitWindow 8000) { "late release: window OK" } else { "late release: NO WINDOW" }
Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Case 3: a real running instance still wins: second start must not open a second window.
Start-Process $Exe | Out-Null
$p = WaitWindow 8000
Start-Process $Exe | Out-Null
Start-Sleep -Milliseconds 3000
$all = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
"running instance: $($all.Count) process(es) after a second start"
Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
