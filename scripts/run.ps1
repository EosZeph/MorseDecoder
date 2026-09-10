[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".tools\dotnet\dotnet.exe"

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "Local .NET SDK was not found at $dotnet."
}

$env:DOTNET_CLI_HOME = Join-Path $root ".tools\dotnet-home"
$env:NUGET_PACKAGES = Join-Path $root ".tools\nuget"
$env:TEMP = Join-Path $root ".tools\temp"
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

& $dotnet run `
    --project (Join-Path $root "src\MorseDecoder.App\MorseDecoder.App.csproj") `
    --configuration $Configuration

