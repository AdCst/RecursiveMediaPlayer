$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$savedCliHome = $env:DOTNET_CLI_HOME
$savedAppData = $env:APPDATA
try {
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
    $env:APPDATA = Join-Path $PSScriptRoot '.dotnet/AppData'
    dotnet restore RecursiveMediaPlayer.csproj --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet build RecursiveMediaPlayer.csproj --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally {
    $env:DOTNET_CLI_HOME = $savedCliHome
    $env:APPDATA = $savedAppData
    Pop-Location
}
