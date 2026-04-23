@echo off
cd /d "%~dp0"

echo [NariMeter] Restoring packages...
dotnet restore NariMeter.csproj
if errorlevel 1 (
    echo Restore failed.
    pause
    exit /b 1
)

echo [NariMeter] Publishing...
dotnet publish NariMeter.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o dist

if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Build complete: dist\NariMeter.exe
pause
