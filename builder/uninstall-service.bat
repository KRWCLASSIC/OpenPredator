@echo off
:: OpenPredator - Manual Service Removal Script
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [Error] Administrator privileges required. Please right-click and 'Run as administrator'.
    pause
    exit /b 1
)

echo Stopping and removing OpenPredator Service...
sc.exe stop OpenPredator >nul 2>&1
sc.exe delete OpenPredator

echo.
echo OpenPredator Service has been removed.
pause
