[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$Test
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".tools\dotnet\dotnet.exe"

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

& $dotnet build (Join-Path $root "MorseDecoder.sln") --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($Test) {
    & $dotnet run `
        --project (Join-Path $root "tests\MorseDecoder.SmokeTests\MorseDecoder.SmokeTests.csproj") `
        --configuration $Configuration `
        --no-build
    exit $LASTEXITCODE
}

