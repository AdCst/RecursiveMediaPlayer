param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.2')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$releaseRoot = Join-Path $projectRoot 'release'
$packageName = "RecursiveMediaPlayer-win-x64-v$Version"
$packageDirectory = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
if ((Test-Path -LiteralPath $packageDirectory) -or (Test-Path -LiteralPath $zipPath)) {
    throw "Release already exists: $packageName. Use a new version or move the existing release first."
}

$savedCliHome = $env:DOTNET_CLI_HOME
$savedAppData = $env:APPDATA
$savedPackages = $env:NUGET_PACKAGES
$savedTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
Push-Location $PSScriptRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
    $env:APPDATA = Join-Path $PSScriptRoot '.dotnet/AppData'
    $env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.dotnet/nuget-packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    dotnet restore RecursiveMediaPlayer.csproj -r win-x64 -p:SelfContained=true --source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw 'Runtime restore failed' }
    dotnet publish RecursiveMediaPlayer.csproj --no-restore -c Release -r win-x64 --self-contained true -p:Version=$Version -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使用教程.md') -Destination $packageDirectory
    $instructions = @"
递归媒体播放器 v$Version — Windows 10/11 64 位

1. 如果收到的是 ZIP，请先完整解压。
2. 双击 RecursiveMediaPlayer.exe 启动。
3. 首次使用，选择媒体文件夹，点击“开始播放”。图片筛选或视频筛选选“禁用”，即可排除该类内容。
4. 点击画面或按 Esc 返回设置；按空格暂停/继续。
5. 设置窗口右上角的 i 按钮可查看完整教程，也可打开同目录的“使用教程.md”。
6. 文件夹内容变化未更新时，点击“清空配置和缓存”，确认后重新选择文件夹并开始播放；图片和视频不会被删除。

本目录已包含 .NET 运行时，无需另行安装 .NET、Python 或 VLC。
请保留本目录所有文件，不要只复制 exe。
视频解码依赖 Windows 媒体组件，部分编码可能不受支持。
设置与缓存保存在 %LOCALAPPDATA%\RecursiveMediaPlayer。
"@
    Set-Content -LiteralPath (Join-Path $packageDirectory '开始使用.txt') -Value $instructions -Encoding UTF8
    Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
    Set-Content -LiteralPath "$zipPath.sha256" -Value "$($hash.Hash.ToLowerInvariant())  $packageName.zip" -Encoding ASCII
    Write-Output "Release: $packageDirectory"
    Write-Output "Archive: $zipPath"
    Write-Output "SHA256: $($hash.Hash)"
} finally {
    $env:DOTNET_CLI_HOME = $savedCliHome
    $env:APPDATA = $savedAppData
    $env:NUGET_PACKAGES = $savedPackages
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $savedTelemetry
    Pop-Location
}
