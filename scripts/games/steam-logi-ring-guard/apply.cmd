@echo off
setlocal
where py >nul 2>nul
if %errorlevel% equ 0 (
  py -3 "%~dp0steam_logi_ring_guard.py" --apply
  goto done
)
where python >nul 2>nul
if %errorlevel% equ 0 (
  python "%~dp0steam_logi_ring_guard.py" --apply
  goto done
)
echo Python 3.10 or newer is required.
echo Download it from https://www.python.org/downloads/windows/
:done
set "result=%errorlevel%"
echo.
pause
exit /b %result%
