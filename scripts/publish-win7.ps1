[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path
$distRoot = Join-Path $root "dist"
$packageName = "MorseDecoder-Win7-x86"
$publishDirectory = Join-Path $distRoot $packageName
$zipPath = Join-Path $distRoot "$packageName.zip"
$hashPath = Join-Path $distRoot "$packageName.sha256.txt"
$dotnet = Join-Path $root ".tools\dotnet\dotnet.exe"
$project = Join-Path $root "legacy\MorseDecoder.Win7.App\MorseDecoder.Win7.App.csproj"

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

foreach ($filePath in @($zipPath, $hashPath)) {
    if (Test-Path -LiteralPath $filePath) {
        Remove-Item -LiteralPath $filePath -Force
    }
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

& $dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x86 `
    --self-contained true `
    --output $publishDirectory `
    --nologo `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Copy-Item `
    -LiteralPath (Join-Path $root "packaging\WIN7_README.txt") `
    -Destination (Join-Path $publishDirectory "使用说明.txt")

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
Write-Host "Windows 7 portable package created:"
Write-Host "  Runtime   : win-x86"
Write-Host "  Framework : netcoreapp3.1"
Write-Host "  Directory : $publishDirectory"
Write-Host "  Archive   : $zipPath"
Write-Host "  Files     : $($files.Count)"
Write-Host ("  Raw size  : {0:N1} MB" -f ($publishSize / 1MB))
Write-Host ("  ZIP size  : {0:N1} MB" -f ($zipSize / 1MB))
Write-Host "  SHA-256   : $zipHash"
Write-Host "  Hash file : $hashPath"
