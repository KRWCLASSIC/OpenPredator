@echo off
:: OpenPredator - Manual Service Registration Script
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [Error] Administrator privileges required. Please right-click and 'Run as administrator'.
    pause
    exit /b 1
)

set SERVICE_EXE=%~dp0openpredator-service.exe
if not exist "%SERVICE_EXE%" (
    echo [Error] Could not find openpredator-service.exe in %~dp0
    pause
    exit /b 1
)

echo Registering OpenPredator Service...
sc.exe stop OpenPredator >nul 2>&1
sc.exe create OpenPredator binPath= "\"%SERVICE_EXE%\" --run" start= auto DisplayName= "OpenPredator Service"
sc.exe description OpenPredator "High-performance, zero-bloat hardware control daemon for Acer gaming laptops"
sc.exe start OpenPredator

echo.
echo OpenPredator Service installed and started successfully.
pause
