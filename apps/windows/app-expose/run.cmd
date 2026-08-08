@echo off
setlocal
pushd "%~dp0"
dotnet build AppExpose.csproj --configuration Release --nologo
set "APP_EXPOSE_EXIT=%ERRORLEVEL%"
if not "%APP_EXPOSE_EXIT%"=="0" goto done
start "" "%~dp0bin\Release\net9.0-windows\AppExpose.exe"
:done
popd
exit /b %APP_EXPOSE_EXIT%
