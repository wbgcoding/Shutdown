[CmdletBinding()]
param(
    [string] $AppVersion,
    [switch] $LocalInstall
)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $project

$languages = @{}
foreach ($language in 'en', 'de') {
    $languages[$language] = @{}
    $resources = [xml](Get-Content -LiteralPath "src\Shutdown\Lang\$language.resx" -Encoding UTF8 -Raw)
    foreach ($entry in $resources.root.data) {
        if ($entry.name.StartsWith('Installer', [StringComparison]::Ordinal)) {
            $languages[$language][$entry.name] = $entry.value
        }
    }
}
$messages = $languages['en']
if ([Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'de') {
    $messages = $languages['de']
}

try {
    $failure = $messages['InstallerCompileFailed']
    if ($LocalInstall) {
        $env:NOPAUSE = '1'
        & "$project\build.bat"
        if ($LASTEXITCODE -ne 0) { exit 1 }
    }
    if (!$AppVersion) {
        $appProject = [xml](Get-Content -LiteralPath 'src\Shutdown\Shutdown.csproj' -Raw)
        $AppVersion = $appProject.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    }

    $compiler = foreach ($folder in @("$env:LOCALAPPDATA\Programs", ${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ($folder) {
            $candidate = Join-Path $folder 'Inno Setup 6\ISCC.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $candidate
                break
            }
        }
    }
    if (!$compiler) {
        $failure = $messages['InstallerCompilerMissing']
        throw $failure
    }

    $dist = Join-Path $project 'dist'
    $output = Join-Path $dist 'release'
    if ($LocalInstall) { $output = $dist }
    $strings = @('[CustomMessages]')
    foreach ($language in 'en', 'de') {
        foreach ($key in ($languages[$language].Keys | Sort-Object)) {
            $strings += "$language.$key=$($languages[$language][$key])"
        }
    }
    Set-Content -LiteralPath (Join-Path $dist 'installer-messages.iss') -Value $strings -Encoding UTF8

    $setup = Join-Path $output "Shutdown-Setup-$AppVersion.exe"
    & $compiler /Qp "/DAppVersion=$AppVersion" "/O$output" "$project\installer\Shutdown.iss"
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $setup -PathType Leaf)) { throw $failure }
    if (!$LocalInstall) { exit 0 }

    $failure = $messages['InstallerCloseFailed']
    if (Get-Process -Name Shutdown -ErrorAction SilentlyContinue) {
        $timer = [Diagnostics.Stopwatch]::StartNew()
        & "$env:SystemRoot\System32\taskkill.exe" /im Shutdown.exe
        while ((Get-Process -Name Shutdown -ErrorAction SilentlyContinue) -and $timer.ElapsedMilliseconds -lt 5000) {
            Start-Sleep -Milliseconds 100
        }
        if (Get-Process -Name Shutdown -ErrorAction SilentlyContinue) { throw $failure }
    }

    $failure = $messages['InstallerInstallFailed']
    $process = Start-Process -FilePath $setup -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw $failure }

    $failure = $messages['InstallerStartFailed']
    $installedExe = "$env:LOCALAPPDATA\Programs\Shutdown\Shutdown.exe"
    if ((Get-FileHash -LiteralPath $installedExe).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $dist 'Shutdown.exe')).Hash) { throw $failure }
    Start-Process -FilePath $installedExe
    exit 0
}
catch {
    [Console]::Error.WriteLine($failure)
    exit 1
}
