@echo off
rem Builds the release files: dist\release\Shutdown-<version>-windows.exe and SHA256SUMS.txt.
rem The version comes from src\Shutdown\Shutdown.csproj. Publishing stays a manual step.
setlocal
cd /d "%~dp0"

set NOSTART=1
set NOPAUSE=1
call build.bat
if errorlevel 1 exit /b 1

for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "([xml](Get-Content 'src\Shutdown\Shutdown.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set VERSION=%%V
if not defined VERSION (
  echo Version not found in Shutdown.csproj.
  exit /b 1
)

set OUT=dist\release
if exist %OUT% rmdir /s /q %OUT%
mkdir %OUT%
set NAME=Shutdown-%VERSION%-windows.exe
copy /y dist\Shutdown.exe %OUT%\%NAME% >nul
if errorlevel 1 exit /b 1

powershell -NoProfile -Command "$h = (Get-FileHash '%OUT%\%NAME%' -Algorithm SHA256).Hash.ToLower(); [IO.File]::WriteAllText('%OUT%\SHA256SUMS.txt', \"$h  %NAME%`n\")"
if errorlevel 1 exit /b 1

echo Release files in %OUT%:
type %OUT%\SHA256SUMS.txt
exit /b 0
