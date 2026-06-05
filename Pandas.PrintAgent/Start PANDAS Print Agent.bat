@echo off
setlocal

set "ROOT=%~dp0"
set "LOCAL_EXE=%ROOT%Pandas.PrintAgent.exe"
set "PUBLISH_DIR=%ROOT%publish\win-x64"
set "PUBLISH_EXE=%PUBLISH_DIR%\Pandas.PrintAgent.exe"

if exist "%LOCAL_EXE%" (
  cd /d "%ROOT%"
  "%LOCAL_EXE%"
  goto done
)

if exist "%PUBLISH_EXE%" (
  cd /d "%PUBLISH_DIR%"
  "%PUBLISH_EXE%"
  goto done
)

echo No se encontro Pandas.PrintAgent.exe.
echo.
echo Si estas en la carpeta fuente, publica primero el agente con:
echo powershell -ExecutionPolicy Bypass -File "%ROOT%publish-win-x64.ps1"
echo.
echo Luego abre este archivo en:
echo "%PUBLISH_DIR%\Start PANDAS Print Agent.bat"
echo.

:done
pause
