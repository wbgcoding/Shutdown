@echo off
title Shutdown
rem Builds the portable dist\Shutdown.exe (C# WPF, .NET Framework 4.8 built into Windows 10/11).
rem Toolchain: .NET SDK 8 or newer (dotnet build targets net48 through reference assemblies).
rem Env: NOSTART=1 skips starting the fresh build, NOPAUSE=1 never pauses on error.
setlocal
cd /d "%~dp0"

rem Double-click run: CMDCMDLINE carries /c and NOPAUSE is unset. Only then shrink the window,
rem keep it open on error and close it 10 s after success. Automated runs never block.
set "DBL="
if not defined NOPAUSE echo %CMDCMDLINE% | find /i "/c" >nul && set "DBL=1"
if defined DBL mode con cols=100 lines=25
rem === build freshness: check ===
set "BS=%~dp0tools\build-stamp.ps1"
set "BSKEY=exe"
set "BSEXTRA="
set "BSART=dist\Shutdown.exe"
set "BSFRESH="
set "BSFORCE="
if "%FORCE%"=="1" set "BSFORCE=1"
for %%A in (%*) do if /i "%%~A"=="--force" set "BSFORCE=1"
if defined BSFORCE goto :bs_start
if not exist "%BS%" goto :bs_start
for /f "usebackq delims=" %%A in (`powershell -NoProfile -ExecutionPolicy RemoteSigned -File "%BS%" -Check -Project "%~dp0." -Key "%BSKEY%" -Extra "%BSEXTRA%"`) do set "BSFRESH=%%A"
if defined BSFRESH goto :bs_uptodate
:bs_start

powershell -NoProfile -Command "Get-Process testhost -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith('%~dp0', [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force" >nul 2>&1

dotnet build src\Shutdown\Shutdown.csproj -c Release -nologo -v q
if errorlevel 1 goto :fail

if not exist dist mkdir dist
rem A running copy keeps the exe locked: move it aside instead of failing.
if exist dist\Shutdown.exe (
  del /f /q dist\Shutdown.exe >nul 2>&1
  if exist dist\Shutdown.exe ren dist\Shutdown.exe "Shutdown.old-%RANDOM%.exe"
)
for %%F in (dist\Shutdown.old-*.exe) do del /f /q "%%F" >nul 2>&1

copy /y src\Shutdown\bin\Release\net48\Shutdown.exe dist\Shutdown.exe >nul
if errorlevel 1 goto :fail

rem === build freshness: save ===
if exist "%BS%" powershell -NoProfile -ExecutionPolicy RemoteSigned -File "%BS%" -Save -Project "%~dp0." -Key "%BSKEY%" -Extra "%BSEXTRA%" -Artifact "%BSART%" >nul 2>&1
echo Built dist\Shutdown.exe
if not defined NOSTART start "" "dist\Shutdown.exe"
if defined DBL timeout /t 10
exit /b 0

:fail
echo Build failed.
if defined DBL pause
exit /b 1

:bs_uptodate
for %%F in ("%BSFRESH%") do echo Build is up to date (last build %%~tF), nothing to do: %%~fF
if defined DBL "%SystemRoot%\System32\timeout.exe" /t 10
exit /b 0
