@echo off
setlocal
cd /d "%~dp0"
echo Building Arden's Solver (uses the .NET compiler that is already part of Windows)...
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

"%CSC%" /nologo /target:winexe /optimize+ /win32icon:ArdensSolver.ico /out:ArdensSolver.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ArdensSolver.cs
if errorlevel 1 goto fail

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make_shortcut.ps1"
echo.
echo DONE. ArdensSolver.exe is in this folder and a Desktop shortcut was created.
echo Starting the app...
start "" "%~dp0ArdensSolver.exe"
timeout /t 3 >nul
exit /b 0

:nocsc
echo Could not find the .NET Framework 4 compiler on this PC.
echo Turn on ".NET Framework 4.8 Advanced Services" in Windows Features, then run this again.
pause
exit /b 1

:fail
echo.
echo Build failed - please send me the error lines printed above.
pause
exit /b 1
