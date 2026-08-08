@echo off
setlocal
pushd "%~dp0"
dotnet build MacControls.csproj --configuration Release --nologo
set "MAC_CONTROLS_EXIT=%ERRORLEVEL%"
if not "%MAC_CONTROLS_EXIT%"=="0" goto done
start "" "%~dp0bin\Release\net9.0-windows\MacControls.exe"
:done
popd
exit /b %MAC_CONTROLS_EXIT%
