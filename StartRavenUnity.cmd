@echo off
setlocal
set "UNITY_EDITOR=C:\Engine\Unity\6000.3.23f1\Editor\Unity.exe"
if not exist "%UNITY_EDITOR%" (
  echo Unity Editor not found: %UNITY_EDITOR%
  pause
  exit /b 1
)
echo Save your work and close this project's Unity Editor before using this launcher.
set "GRAPHICS_ARGS=-gfx-ring-buffer-size 67108864"
if /I "%~1"=="--default-graphics-buffer" set "GRAPHICS_ARGS="
echo Starting Raven with budgeted grass uploads.
if defined GRAPHICS_ARGS echo Graphics ring buffer: 64 MiB for Editor rendering transitions.
start "" "%UNITY_EDITOR%" -projectPath "%~dp0." %GRAPHICS_ARGS%
