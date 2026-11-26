@echo off

dotnet build -c Debug

if %errorlevel% neq 0 (
    echo.
    echo Compilr error!
    exit /b %errorlevel%
)
