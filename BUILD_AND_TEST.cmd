@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo CCF Editor / Creator - WinForms .NET 8
echo ============================================================
echo.

echo [1/3] Restoring packages...
dotnet restore CcfEditor.sln
if errorlevel 1 goto :fail

echo.
echo [2/3] Building solution...
dotnet build CcfEditor.sln -c Release --no-restore
if errorlevel 1 goto :fail

echo.
echo [3/3] Running xUnit tests...
dotnet test tests\CcfEditor.Tests\CcfEditor.Tests.csproj -c Release --no-build
if errorlevel 1 goto :fail

echo.
echo ============================================================
echo PASS - build and tests completed successfully.
echo ============================================================
pause
exit /b 0

:fail
echo.
echo ============================================================
echo FAILED - see compiler/test errors above.
echo ============================================================
pause
exit /b 1
