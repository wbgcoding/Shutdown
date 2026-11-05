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

taskkill /f /im testhost.exe >nul 2>&1

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

echo Built dist\Shutdown.exe
if not defined NOSTART start "" "dist\Shutdown.exe"
if defined DBL timeout /t 10
exit /b 0

:fail
echo Build failed.
if defined DBL pause
exit /b 1
