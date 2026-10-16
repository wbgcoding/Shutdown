@echo off
rem Builds the portable out\Shutdown.exe (C# WPF, .NET Framework 4.8 built into Windows 10/11).
rem Toolchain: .NET SDK 8 or newer (dotnet build targets net48 through reference assemblies).
rem Env: NOSTART=1 skips starting the fresh build, NOPAUSE=1 never pauses on error.
setlocal
cd /d "%~dp0"

taskkill /f /im testhost.exe >nul 2>&1

dotnet build src\Shutdown\Shutdown.csproj -c Release -nologo -v q
if errorlevel 1 goto :fail

if not exist out mkdir out
rem A running copy keeps the exe locked: move it aside instead of failing.
if exist out\Shutdown.exe (
  del /f /q out\Shutdown.exe >nul 2>&1
  if exist out\Shutdown.exe ren out\Shutdown.exe "Shutdown.old-%RANDOM%.exe"
)
for %%F in (out\Shutdown.old-*.exe) do del /f /q "%%F" >nul 2>&1

copy /y src\Shutdown\bin\Release\net48\Shutdown.exe out\Shutdown.exe >nul
if errorlevel 1 goto :fail

echo Built out\Shutdown.exe
if not defined NOSTART start "" "out\Shutdown.exe" --second-instance
exit /b 0

:fail
echo Build failed.
if not defined NOPAUSE echo %CMDCMDLINE% | find /i "/c" >nul && pause
exit /b 1
