[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path
$dotnet = Join-Path $root ".tools\dotnet\dotnet.exe"
$project = Join-Path $root "tools\MorseDecoder.Screenshot\MorseDecoder.Screenshot.csproj"
$outputPath = Join-Path $root "docs\MorseDecoder-demo.png"

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "Local .NET SDK was not found at $dotnet."
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

& $dotnet run --project $project --configuration Release -- "$outputPath"
exit $LASTEXITCODE
