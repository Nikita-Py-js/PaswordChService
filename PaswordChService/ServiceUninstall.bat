@echo off
chcp 65001 > nul
setlocal

set SERVICE_NAME=PaswordChService
set INSTALL_DIR=%ProgramFiles%\PaswordCh
set DATA_DIR=%ProgramData%\PaswordCh

echo === 1. Остановка службы ===
sc stop %SERVICE_NAME% >nul 2>&1
timeout /t 5 /nobreak >nul

echo === 2. Удаление службы ===
if exist "%INSTALL_DIR%\PaswordChService.exe" (
    "%INSTALL_DIR%\PaswordChService.exe" uninstall
) else (
    "%ProgramFiles%\PaswordCh\PaswordChService.exe" uninstall 2>nul
)

echo === 3. Файлы программы ===
choice /M "Удалить каталог программы %INSTALL_DIR%"
if errorlevel 2 goto skip_prog
rmdir /S /Q "%INSTALL_DIR%"
echo Каталог программы удалён.
:skip_prog

echo === 4. Каталог данных ===
echo ВНИМАНИЕ: %DATA_DIR% содержит логи, архив паролей и настройки.
choice /M "Удалить каталог данных %DATA_DIR%"
if errorlevel 2 goto skip_data
rmdir /S /Q "%DATA_DIR%"
echo Каталог данных удалён.
:skip_data

echo.
echo Готово.
endlocal
pause