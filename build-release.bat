@echo off
chcp 65001 >nul
title Dong goi TBCO cho Windows
echo Dang chay kich ban dong goi TBCO...
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0build-release.ps1"
if %errorlevel% neq 0 (
    echo.
    echo [LOI] Co loi xay ra trong qua trinh dong goi!
)
echo.
pause
