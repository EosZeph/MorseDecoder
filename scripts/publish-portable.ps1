[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x64", "x86")]
    [string]$Architecture = "x64"
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path
$distRoot = Join-Path $root "dist"
$runtimeIdentifier = "win-$Architecture"
$packageName = "MorseDecoder-Portable-$runtimeIdentifier"
$publishDirectory = Join-Path $distRoot $packageName
$zipPath = Join-Path $distRoot "$packageName.zip"
$hashPath = Join-Path $distRoot "$packageName.sha256.txt"
$dotnet = Join-Path $root ".tools\dotnet\dotnet.exe"
$platformDescription = if ($Architecture -eq "x86") {
    "Windows 10 x86（也可在 64 位 Windows 上运行）"
}
else {
    "Windows 10/11 x64"
}

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "Local .NET SDK was not found at $dotnet."
}

$distRootFull = [System.IO.Path]::GetFullPath($distRoot)
$publishDirectoryFull = [System.IO.Path]::GetFullPath($publishDirectory)
if (-not $publishDirectoryFull.StartsWith($distRootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a publish directory outside $distRootFull."
}

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

if (Test-Path -LiteralPath $hashPath) {
    Remove-Item -LiteralPath $hashPath -Force
}

$cacheDirectories = @(
    (Join-Path $root ".tools\dotnet-home"),
    (Join-Path $root ".tools\nuget"),
    (Join-Path $root ".tools\temp")
)

foreach ($directory in $cacheDirectories) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$env:DOTNET_CLI_HOME = $cacheDirectories[0]
$env:NUGET_PACKAGES = $cacheDirectories[1]
$env:TEMP = $cacheDirectories[2]
$env:TMP = $cacheDirectories[2]
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

& $dotnet publish (Join-Path $root "src\MorseDecoder.App\MorseDecoder.App.csproj") `
    --configuration $Configuration `
    --runtime $runtimeIdentifier `
    --self-contained true `
    --output $publishDirectory `
    --nologo `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$readmePath = Join-Path $publishDirectory "使用说明.txt"
$readmeContent = Get-Content `
    -LiteralPath (Join-Path $root "packaging\PORTABLE_README.txt") `
    -Raw `
    -Encoding UTF8
$readmeContent = $readmeContent.Replace("{{PLATFORM}}", $platformDescription)
$readmeContent | Set-Content -LiteralPath $readmePath -Encoding utf8

$files = Get-ChildItem -LiteralPath $publishDirectory -File | Sort-Object Name
$hashLines = foreach ($file in $files) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}

$hashLines | Set-Content -LiteralPath $hashPath -Encoding utf8
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$publishSize = (Get-ChildItem -LiteralPath $publishDirectory -File | Measure-Object Length -Sum).Sum
$zipSize = (Get-Item -LiteralPath $zipPath).Length

Write-Host ""
Write-Host "Portable package created:"
Write-Host "  Runtime   : $runtimeIdentifier"
Write-Host "  Directory : $publishDirectory"
Write-Host "  Archive   : $zipPath"
Write-Host "  Files     : $($files.Count)"
Write-Host ("  Raw size  : {0:N1} MB" -f ($publishSize / 1MB))
Write-Host ("  ZIP size  : {0:N1} MB" -f ($zipSize / 1MB))
Write-Host "  SHA-256   : $zipHash"
Write-Host "  Hash file : $hashPath"

if (-not (Test-Path -LiteralPath $readmePath)) {
    throw "Portable README was not copied."
}
