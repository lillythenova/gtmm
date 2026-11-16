@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%gtmm\gtmm.csproj"

call :publish win-x64
if errorlevel 1 exit /b %errorlevel%

call :publish win-arm64
if errorlevel 1 exit /b %errorlevel%

echo.
echo Windows single-file builds completed under "%SCRIPT_DIR%dist".
exit /b 0

:publish
set "RUNTIME=%~1"
set "OUTPUT=%SCRIPT_DIR%dist\%RUNTIME%"
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"

echo.
echo Publishing %RUNTIME%...
dotnet publish "%PROJECT%" --configuration Release --runtime "%RUNTIME%" --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false --output "%OUTPUT%"
if errorlevel 1 exit /b %errorlevel%

del /q "%OUTPUT%\*.pdb" 2>nul
if not exist "%OUTPUT%\gtmm.exe" (
    echo Expected executable was not created: "%OUTPUT%\gtmm.exe"
    exit /b 1
)
exit /b 0
