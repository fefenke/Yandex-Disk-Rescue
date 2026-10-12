@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "rescueBuild="
set "rescueFramework="
for /f "tokens=3" %%V in ('reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v CurrentBuildNumber 2^>nul') do set "rescueBuild=%%V"
if not defined rescueBuild goto unsupported
if %rescueBuild% LSS 16299 goto unsupported
if /i "%PROCESSOR_ARCHITECTURE%"=="AMD64" goto architecture_ok
if /i "%PROCESSOR_ARCHITEW6432%"=="AMD64" goto architecture_ok
echo This portable build requires Windows x64. ARM64 and 32-bit builds are not provided.
goto failure
:architecture_ok
for /f "tokens=3" %%V in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release /reg:64 2^>nul') do set /a "rescueFramework=%%V" >nul
if not defined rescueFramework goto missing_framework
if %rescueFramework% LSS 461308 goto missing_framework
set "rescueExe=%~dp0YandexDiskRescue.exe"
if not exist "%rescueExe%" if exist "%~dp0dist\YandexDiskRescue-release\YandexDiskRescue.exe" set "rescueExe=%~dp0dist\YandexDiskRescue-release\YandexDiskRescue.exe"
if not exist "%rescueExe%" if exist "%~dp0dist\YandexDiskRescue-updated\YandexDiskRescue.exe" set "rescueExe=%~dp0dist\YandexDiskRescue-updated\YandexDiskRescue.exe"
if not exist "%rescueExe%" if exist "%~dp0dist\YandexDiskRescue-fixed\YandexDiskRescue.exe" set "rescueExe=%~dp0dist\YandexDiskRescue-fixed\YandexDiskRescue.exe"
if not exist "%rescueExe%" if exist "%~dp0dist\YandexDiskRescue\YandexDiskRescue.exe" set "rescueExe=%~dp0dist\YandexDiskRescue\YandexDiskRescue.exe"
if not exist "%rescueExe%" (
  echo YandexDiskRescue.exe is missing. Extract the complete portable ZIP.
  echo In a source checkout, run build.ps1 first.
  goto failure
)
if /i "%~1"=="--check" (
  echo PASS: Windows x64, build %rescueBuild%, .NET Framework release %rescueFramework%.
  echo Executable: "%rescueExe%"
  exit /b 0
)
start "" "%rescueExe%"
exit /b 0
:unsupported
echo Windows 10 version 1709 or later, or Windows 11, is required.
echo Windows 7/8 cannot run this Cloud Files downloader.
goto failure
:missing_framework
echo .NET Framework 4.7.1 or newer is required.
echo It is included in supported Windows versions. If missing or damaged, repair or install it:
echo https://dotnet.microsoft.com/download/dotnet-framework/net48
:failure
echo.
echo See README.md or README.en.md for setup instructions.
if /i not "%~1"=="--check" pause
exit /b 1
