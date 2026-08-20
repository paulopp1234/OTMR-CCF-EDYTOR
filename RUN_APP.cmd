@echo off
setlocal
cd /d "%~dp0"
dotnet run --project src\CcfEditor.WinForms\CcfEditor.WinForms.csproj
