@echo off
rem Launcher: pick the build that matches the running PE.
rem A 32-bit PE reports x86 and cannot load an amd64 image at all
rem ("this app can't run on your PC"); conversely many cut-down x64 PEs ship
rem no WoW64. So the architecture has to be chosen at launch, not at build.
rem
rem Two directory layouts are accepted, because both occur in practice:
rem   x64\   + x86\       the layout the tool ISO is assembled with
rem   win-x64\ + win-x86\  the layout dotnet build produces under net8.0-windows\
rem
rem NOTE: this file must keep CRLF line endings. cmd.exe mis-parses an
rem LF-only batch file (it silently loses characters and runs nothing).
setlocal
set ARCH=x86
if /i "%PROCESSOR_ARCHITECTURE%"=="AMD64" set ARCH=x64
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set ARCH=x64

set EXE=%~dp0%ARCH%\EpsBootRepair.exe
if exist "%EXE%" goto run
set EXE=%~dp0win-%ARCH%\EpsBootRepair.exe
if exist "%EXE%" goto run

echo [GO.CMD] no %ARCH% build found next to this script.
echo          PROCESSOR_ARCHITECTURE=%PROCESSOR_ARCHITECTURE%  ^(looked for %ARCH%\ and win-%ARCH%\^)
pause
exit /b 1

:run
echo PROCESSOR_ARCHITECTURE=%PROCESSOR_ARCHITECTURE%  --^>  %ARCH%
start "" "%EXE%"
