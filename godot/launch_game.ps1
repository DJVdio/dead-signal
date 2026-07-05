param(
    [switch]$Build = $true,
    [switch]$KeepOpen
)

$projectRoot = Split-Path -Parent $PSCommandPath
$godotExe = "E:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe"
$dotnetDir = "C:\Users\59216\.dotnet"
$dotnetExe = "$dotnetDir\dotnet.exe"

if (-not (Test-Path -LiteralPath $godotExe)) {
    Write-Error "未找到 Godot 可执行文件：$godotExe"
    exit 1
}
if (-not (Test-Path -LiteralPath $dotnetExe)) {
    Write-Error "未找到 dotnet CLI：$dotnetExe"
    exit 1
}

# 强制设置环境变量（对当前进程及子进程生效）
# DOTNET_MULTILEVEL_LOOKUP=0 防止 hostfxr 回退到 C:\Program Files\dotnet\ 的 3.1 运行时
$env:DOTNET_ROOT = $dotnetDir
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:Path = "$dotnetDir;$env:Path"

if ($Build) {
    Write-Host "=== 1. 构建依赖库 ==="
    & $dotnetExe build "$projectRoot\..\DeadSignal.sln" -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "依赖库构建失败，退出码: $LASTEXITCODE"
        exit 1
    }

    Write-Host "=== 2. 构建 Godot 工程 ==="
    & $dotnetExe build "$projectRoot\DeadSignal.Godot.csproj" -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Godot 工程构建失败，退出码: $LASTEXITCODE"
        exit 1
    }

    $srcDir = "$projectRoot\.godot\mono\temp\bin\Release"
    $dstDir = "$projectRoot\.godot\mono\assemblies\Release"
    if (-not (Test-Path -LiteralPath $dstDir)) {
        New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
    }

    Get-ChildItem -Path $srcDir -Filter "DeadSignal*.dll" | Copy-Item -Destination $dstDir -Force
    Write-Host "程序集已复制到 assemblies/Release"
}

$targetDll = "$projectRoot\.godot\mono\assemblies\Release\DeadSignal.Godot.dll"
if (-not (Test-Path -LiteralPath $targetDll)) {
    Write-Error "启动前检查未通过：$targetDll 不存在"
    exit 1
}

Write-Host "启动游戏..."
$p = Start-Process -FilePath $godotExe -ArgumentList "--path $projectRoot" -PassThru -NoNewWindow
$p.WaitForExit()
Write-Host "游戏已退出，退出码: $($p.ExitCode)"
