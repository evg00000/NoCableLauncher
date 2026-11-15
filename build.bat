@echo off
rem Builds NoCableLauncher.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No Visual Studio needed.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found: %CSC%
  echo Install .NET Framework 4.8 ^(it is built into Windows 10/11^).
  if not defined CI pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /codepage:65001 ^
  /out:NoCableLauncher.exe /win32icon:src\rs14.ico /win32manifest:src\app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /recurse:src\*.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED - send the text above to whoever helps you.
  if not defined CI pause
  exit /b 1
)
if not exist RS_ASIO mkdir RS_ASIO
echo.
echo OK: NoCableLauncher.exe
if not defined CI pause
