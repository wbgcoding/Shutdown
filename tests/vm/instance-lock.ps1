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

# The guest's original window should display the timer sent by the next launch.
function Send-Command([string[]]$CommandArgs) {
    $sender = Start-Process -FilePath $Exe -ArgumentList $CommandArgs -WindowStyle Hidden -PassThru
    if (-not $sender.WaitForExit(8000)) {
        Stop-Process -Id $sender.Id -Force -ErrorAction SilentlyContinue
        throw "Command launcher did not exit: $CommandArgs"
    }
    if ($sender.ExitCode -ne 0) { throw "Command launcher failed: $CommandArgs" }
}

function HasPendingCard {
    $p.Refresh()
    $root = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $abort = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $pendingAbortId)
    return $null -ne $abort -and -not $abort.Current.IsOffscreen
}

function WaitPendingCard([bool]$Visible) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 3000) {
        if ((HasPendingCard) -eq $Visible) { return $true }
        Start-Sleep -Milliseconds 50
    }
    return $false
}

if (-not $p -or $all.Count -ne 1 -or $all[0].Id -ne $p.Id) { throw "The original instance did not stay open." }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$pendingAbortId = [Windows.Automation.PropertyCondition]::new(
    [Windows.Automation.AutomationElement]::AutomationIdProperty, 'Abort')
if (HasPendingCard) { throw "The guest already has a pending schedule." }
Send-Command @('--shutdown')
try {
    Send-Command @('--in', '30')
    if (-not (WaitPendingCard $true)) { throw "The original window did not show the pending card." }
    Send-Command @('--abort')
    if (-not (WaitPendingCard $false)) { throw "The original window did not clear the pending card." }
    "command handover: original window showed and cleared the pending card"
}
finally {
    # Cancel the guest timer even if the acknowledgement or card check failed.
    $shutdownExe = Join-Path ([Environment]::SystemDirectory) 'shutdown.exe'
    Start-Process -FilePath $shutdownExe -ArgumentList '/a' -WindowStyle Hidden -Wait
}

Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Builds of the same version are ordered by the exe's write time: a copy dated later counts as newer.
Add-Type -Namespace W -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);'
function ForegroundPid { $fp = 0; [W.U]::GetWindowThreadProcessId([W.U]::GetForegroundWindow(), [ref]$fp) | Out-Null; $fp }
$dir = Split-Path $Exe
$newName = "$name-newer"
$newer = Join-Path $dir "$newName.exe"
Copy-Item $Exe $newer -Force
(Get-Item $newer).LastWriteTime = (Get-Item $Exe).LastWriteTime.AddHours(1)
function WaitWindowOf([string]$n, [int]$ms) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) {
        $p = Get-Process -Name $n -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }
        if ($p) { return $p | Select-Object -First 1 }
        Start-Sleep -Milliseconds 50
    }
    return $null
}

# Case 4: a newer build replaces the running older one.
Start-Process $Exe | Out-Null
$old = WaitWindowOf $name 8000
Start-Process $newer | Out-Null
$new = WaitWindowOf $newName 8000
Start-Sleep -Milliseconds 1000
$oldLeft = @(Get-Process -Name $name -ErrorAction SilentlyContinue).Count
"newer build: old processes left=$oldLeft, new window=$([bool]$new), foreground is new=$((ForegroundPid) -eq $new.Id)"

# Case 5: an older build started while the newer one runs only brings the newer one to the front.
Start-Process notepad | Out-Null
Start-Sleep -Milliseconds 1500
Start-Process $Exe | Out-Null
Start-Sleep -Milliseconds 3000
$oldLeft = @(Get-Process -Name $name -ErrorAction SilentlyContinue).Count
$newLeft = @(Get-Process -Name $newName -ErrorAction SilentlyContinue).Count
"older build: old processes=$oldLeft, newer processes=$newLeft, foreground is newer=$((ForegroundPid) -eq $new.Id)"
Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force

# Case 6: the same build started again comes to the front over another window.
Start-Process notepad | Out-Null
Start-Sleep -Milliseconds 1500
Start-Process $newer | Out-Null
Start-Sleep -Milliseconds 3000
"same build: newer processes=$(@(Get-Process -Name $newName -ErrorAction SilentlyContinue).Count), foreground is running=$((ForegroundPid) -eq $new.Id)"
Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force

Get-Process -Name $name, $newName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-Item $newer -Force -ErrorAction SilentlyContinue
