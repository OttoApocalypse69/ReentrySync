@echo off
title ReentrySync Automated Installer
echo Launching ReentrySync + BepInEx Automated Installer...
if exist "%~dp0install.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/OttoApocalypse69/ReentrySync/main/scripts/install.ps1 | iex"
)
echo.
pause
