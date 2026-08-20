@echo off
setlocal
cd /d "%~dp0"
dotnet build src\CcfEditor.WinForms\CcfEditor.WinForms.csproj -c Release
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
)
echo.
echo WINFORMS APP BUILD PASSED
pause
