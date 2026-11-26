@echo off

dotnet build -c Release

if %errorlevel% neq 0 (
    echo.
    echo Compilr error!
    exit /b %errorlevel%
)
