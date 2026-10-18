@echo off
chcp 65001 >nul

rem 规避部分 .NET SDK 在进程退出时抛 ObjectDisposedException 的问题：
rem Microsoft.DotNet.Cli.Installer.Windows.TimestampedFileLogger.Dispose
rem 该异常会让 dotnet 以非 0 退出码结束，但编译/发布其实已经成功完成。
set DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

echo ========================================
echo   WC4MapEditor 导出工具
echo   框架依赖模式 (不打包运行时)
echo ========================================
echo.

set PROJECT=WC4MapEditor.csproj
set OUTPUT=publish
set CONFIG=Release

echo [1/4] 清理所有编译缓存...
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
if exist "src\WC4MapEditor.Core\bin" rmdir /s /q "src\WC4MapEditor.Core\bin"
if exist "src\WC4MapEditor.Core\obj" rmdir /s /q "src\WC4MapEditor.Core\obj"
if exist "src\WC4MapEditor.Rendering\bin" rmdir /s /q "src\WC4MapEditor.Rendering\bin"
if exist "src\WC4MapEditor.Rendering\obj" rmdir /s /q "src\WC4MapEditor.Rendering\obj"
if exist "src\WC4MapEditor.GUI\bin" rmdir /s /q "src\WC4MapEditor.GUI\bin"
if exist "src\WC4MapEditor.GUI\obj" rmdir /s /q "src\WC4MapEditor.GUI\obj"
if exist "src\WC4MapEditor.AI\bin" rmdir /s /q "src\WC4MapEditor.AI\bin"
if exist "src\WC4MapEditor.AI\obj" rmdir /s /q "src\WC4MapEditor.AI\obj"
if exist "src\WC4MapEditor.Scripting\bin" rmdir /s /q "src\WC4MapEditor.Scripting\bin"
if exist "src\WC4MapEditor.Scripting\obj" rmdir /s /q "src\WC4MapEditor.Scripting\obj"
if exist "src\WC4MapEditor.Cli\bin" rmdir /s /q "src\WC4MapEditor.Cli\bin"
if exist "src\WC4MapEditor.Cli\obj" rmdir /s /q "src\WC4MapEditor.Cli\obj"

echo [2/4] 还原依赖...
dotnet restore "%PROJECT%"
if %errorlevel% neq 0 (
    echo 还原失败！
    pause
    exit /b 1
)

echo [3/4] 编译解决方案...
dotnet build "%PROJECT%" -c %CONFIG% --no-restore
rem 不依赖 errorlevel：SDK 退出时的 TextWriter 异常会让退出码非 0，
rem 但编译其实已经成功。改以产物是否存在作为判据。
if not exist "bin\%CONFIG%\net10.0-windows\WC4MapEditor.dll" (
    echo 编译失败！未生成 bin\%CONFIG%\net10.0-windows\WC4MapEditor.dll
    pause
    exit /b 1
)

echo [4/4] 发布 (框架依赖, 单文件, WinExe)...
dotnet publish "%PROJECT%" ^
    -c %CONFIG% ^
    -o "%OUTPUT%" ^
    --no-restore ^
    -p:SelfContained=false ^
    -p:PublishSingleFile=true ^
    -p:PublishTrimmed=false ^
    -p:PublishReadyToRun=false ^
    -p:IncludeNativeLibrariesForSelfExtract=false

rem 同上：不依赖 errorlevel，改以产物是否存在作为判据
if not exist "%OUTPUT%\WC4MapEditor.exe" (
    echo 发布失败！未生成 %OUTPUT%\WC4MapEditor.exe
    pause
    exit /b 1
)

echo.
echo ========================================
echo   发布成功！
echo   输出目录: %OUTPUT%
echo   运行需要安装 .NET 10 运行时
echo ========================================
echo.

explorer "%OUTPUT%"
pause