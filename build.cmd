@echo off
setlocal
cd /d "%~dp0"

REM Builds PhotoFrame.exe with the C# compiler that ships with Windows (.NET
REM Framework 4.8). No SDK needed. The exe is AnyCPU, so the same file runs
REM natively on x64 and Arm64 Windows.

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
  echo ERROR: no .NET Framework 4 compiler found.
  exit /b 1
)

if not exist out mkdir out
"%FW%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ ^
  /win32icon:src\PhotoFrame.ico /win32manifest:src\app.manifest ^
  "/lib:%FW%\WPF" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /out:out\PhotoFrame.exe src\*.cs
if errorlevel 1 exit /b 1
echo Built out\PhotoFrame.exe
