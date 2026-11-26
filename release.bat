@echo off
rem Builds the portable exe, the installer and SHA256SUMS.txt in dist\release.
rem The version comes from src\Shutdown\Shutdown.csproj. Publishing stays a manual step.
setlocal
cd /d "%~dp0"

set NOPAUSE=1
call "%~dp0build.bat"
if errorlevel 1 exit /b 1

for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "([xml](Get-Content 'src\Shutdown\Shutdown.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set VERSION=%%V
if not defined VERSION (
  echo Version not found in Shutdown.csproj.
  exit /b 1
)

set "OUT=dist\release"
rem Start empty so no file of an older version is shipped by mistake.
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"
if errorlevel 1 exit /b 1
set "NAME=Shutdown.exe"
set "SETUP=Shutdown-Setup-%VERSION%.exe"
copy /y dist\Shutdown.exe "%OUT%\%NAME%" >nul
if errorlevel 1 exit /b 1

rem The shipped exe must not carry a debug path from the build machine.
findstr /m /i /c:".pdb" "%OUT%\%NAME%" >nul
if not errorlevel 1 (
  echo %NAME% contains a .pdb path. Check DebugType in Shutdown.csproj.
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0tools\build-installer.ps1" -AppVersion "%VERSION%"
if errorlevel 1 exit /b 1

powershell -NoProfile -Command "$ErrorActionPreference='Stop'; $lines = foreach ($name in '%NAME%', '%SETUP%') { (Get-FileHash -LiteralPath ('%OUT%\' + $name) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name }; Set-Content -LiteralPath '%OUT%\SHA256SUMS.txt' -Value $lines -Encoding ASCII"
if errorlevel 1 exit /b 1

echo Release files in %OUT%:
type "%OUT%\SHA256SUMS.txt"
exit /b 0
