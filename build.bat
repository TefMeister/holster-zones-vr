@echo off
rem Builds the Holster Zones window into dist\ (C#, the compiler that ships with Windows' .NET Framework 4), icon beside it.
rem Then `dist\HolsterZones.exe --selftest build\selftest` checks the holster file rules without the game, and install.ps1 puts
rem the icon on the desktop. The game half is Visceral RE8 itself (its holster script), installed with that mod.
setlocal
set HERE=%~dp0
if not exist "%HERE%dist" mkdir "%HERE%dist"
if not exist "%HERE%build" mkdir "%HERE%build"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /win32icon:"%HERE%app\holster_zones.ico" ^
   /main:Program /out:"%HERE%dist\HolsterZones.exe" "%HERE%app\*.cs"
if errorlevel 1 (echo APP BUILD FAILED & exit /b 1)
copy /y "%HERE%app\holster_zones.ico" "%HERE%dist\" >nul
echo BUILD OK: %HERE%dist
