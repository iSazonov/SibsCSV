@echo off
chcp 65001 > nul

set "REPORT_DIR=%~dp0distrib\TestResults"
if not exist "%REPORT_DIR%" mkdir "%REPORT_DIR%"

dotnet build -c Release

if %errorlevel% neq 0 (
    echo.
    echo Ошибка компиляции!
    exit /b %errorlevel%
)

pwsh.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Transcript -Path '%REPORT_DIR%\TestResults.log' -Force | Out-Null; Invoke-Pester -Path '%~dp0tests' -OutputFile '%REPORT_DIR%\TestResults.xml' -OutputFormat NUnitXml; $exitCode = $LASTEXITCODE; Stop-Transcript | Out-Null; exit $exitCode"

if %errorlevel% neq 0 (
    echo.
    echo Тесты завершились с ошибками! Отчеты сохранены в %REPORT_DIR%
    exit /b %errorlevel%
)

echo.
echo Тесты пройдены! Отчеты сохранены в %REPORT_DIR%
