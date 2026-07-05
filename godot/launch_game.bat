@echo off
set DOTNET_ROOT=C:\Users\59216\.dotnet
set DOTNET_MULTILEVEL_LOOKUP=0
set PATH=C:\Users\59216\.dotnet;%PATH%

cd /d "E:\projects\dead-signal\godot"

echo === 1. 构建依赖库 ===
call "C:\Users\59216\.dotnet\dotnet.exe" build "..\DeadSignal.sln" -c Release
if %ERRORLEVEL% NEQ 0 (
    echo 依赖库构建失败，退出码: %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo === 2. 构建 Godot 工程 ===
call "C:\Users\59216\.dotnet\dotnet.exe" build "DeadSignal.Godot.csproj" -c Release
if %ERRORLEVEL% NEQ 0 (
    echo Godot 工程构建失败，退出码: %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo === 3. 复制程序集 ===
if not exist ".godot\mono\assemblies\Release\" mkdir ".godot\mono\assemblies\Release"
copy /Y ".godot\mono\temp\bin\Release\DeadSignal.Godot.dll" ".godot\mono\assemblies\Release\DeadSignal.Godot.dll" >nul
copy /Y ".godot\mono\temp\bin\Release\DeadSignal.Combat.dll" ".godot\mono\assemblies\Release\DeadSignal.Combat.dll" >nul

if not exist ".godot\mono\assemblies\Release\DeadSignal.Godot.dll" (
    echo 错误：DLL 复制失败
    pause
    exit /b 1
)

echo === 4. 启动游戏 ===
start "" "E:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe" --path "E:\projects\dead-signal\godot"
