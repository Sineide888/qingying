$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$dotnet = Join-Path $env:LOCALAPPDATA "QingyingBuild\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

$env:DOTNET_ROOT = Split-Path $dotnet
$env:PATH = "$(Split-Path $dotnet);$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_PACKAGES = Join-Path $env:LOCALAPPDATA "QingyingBuild\nuget"

& $dotnet new sln -n Qingying -o $root --force | Out-Null
& $dotnet sln "$root\Qingying.sln" add `
    "$root\src\Qingying.Core\Qingying.Core.csproj" `
    "$root\src\Qingying.Player\Qingying.Player.csproj" `
    "$root\src\Qingying.Smoke\Qingying.Smoke.csproj"

Write-Host "Checking playback engine..."
& $dotnet run --project "$root\src\Qingying.Smoke\Qingying.Smoke.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Smoke test failed" }

$publish = Join-Path $root "artifacts\publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "Publishing player..."
& $dotnet publish "$root\src\Qingying.Player\Qingying.Player.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

$libvlc = Join-Path $publish "libvlc\win-x64\libvlc.dll"
$plugins = Join-Path $publish "libvlc\win-x64\plugins"
if (-not (Test-Path $libvlc) -or -not (Test-Path $plugins)) {
    throw "Publish output is missing libvlc.dll or plugins."
}

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if (-not $iscc) {
    throw "ISCC.exe was not found."
}

Write-Host "Building installer..."
& $iscc "$root\installer\qingying.iss"
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }

Get-ChildItem "$root\dist\QingyingSetup-*.exe" | Select-Object FullName, Length
