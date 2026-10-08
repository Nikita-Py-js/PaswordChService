@echo off
chcp 65001 > nul
setlocal

set SERVICE_NAME=PaswordChService
set INSTALL_DIR=%ProgramFiles%\PaswordCh

echo === 1. Копирование файлов в %INSTALL_DIR% ===
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

copy /Y "%~dp0PaswordChService.exe"         "%INSTALL_DIR%\"
copy /Y "%~dp0PaswordChService.exe.config"  "%INSTALL_DIR%\"
copy /Y "%~dp0EmailUser.html"               "%INSTALL_DIR%\"
copy /Y "%~dp0EmailAdmin.html"              "%INSTALL_DIR%\"

if not exist "%INSTALL_DIR%\my.ini" (
    copy /Y "%~dp0my.ini" "%INSTALL_DIR%\"
    echo Создан %INSTALL_DIR%\my.ini. Отредактируйте его перед запуском службы!
) else (
    echo my.ini уже существует — не трогаем.
)

if not exist "%INSTALL_DIR%\emails.csv" (
    copy /Y "%~dp0emails.csv" "%INSTALL_DIR%\"
    echo Создан %INSTALL_DIR%\emails.csv.
) else (
    echo emails.csv уже существует — не трогаем.
)

echo === 2. Создание каталогов ===
if not exist "%INSTALL_DIR%\Logs"       mkdir "%INSTALL_DIR%\Logs"
if not exist "%INSTALL_DIR%\Archive"    mkdir "%INSTALL_DIR%\Archive"
if not exist "%INSTALL_DIR%\ErrorUsers" mkdir "%INSTALL_DIR%\ErrorUsers"

echo === 3. Права доступа ===
icacls "%INSTALL_DIR%" /grant "NT AUTHORITY\SYSTEM:(OI)(CI)F" >nul
icacls "%INSTALL_DIR%" /grant "BUILTIN\Administrators:(OI)(CI)F" >nul

echo === 4. Установка службы ===
"%INSTALL_DIR%\PaswordChService.exe" install
if errorlevel 1 (
    echo Ошибка установки службы.
    exit /b 1
)

sc config %SERVICE_NAME% start= auto
sc description %SERVICE_NAME% "Periodic password change service (Local/AD)."
sc failure %SERVICE_NAME% reset= 86400 actions= restart/60000/restart/120000/restart/300000

echo === 5. Запуск службы ===
sc start %SERVICE_NAME%

echo.
echo Установлено в: %INSTALL_DIR%
endlocal
pause