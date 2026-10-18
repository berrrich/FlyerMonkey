[CmdletBinding()]
param([switch]$EnableFixtureImportForProduct102, [ValidateRange(1024,65535)][int]$Port = 5145)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$names = @('DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT', 'CraterArt__Provider',
    'CraterArt__Azure__ServiceUri', 'CraterArt__Azure__Container', 'CraterIt__Enabled',
    'CraterIt__FixtureImportEnabled', 'CraterIt__FixtureProductId')
$original = @{}
foreach ($name in $names) { $original[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    # Process-local, nonsecret settings only. Never set user/machine environment or edit configuration files.
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:CraterArt__Provider = 'Azure'
    $env:CraterArt__Azure__ServiceUri = 'https://flyermonkey.blob.core.windows.net/'
    $env:CraterArt__Azure__Container = 'crater-artwork'
    $env:CraterIt__Enabled = 'true'
    $env:CraterIt__FixtureImportEnabled = $(if ($EnableFixtureImportForProduct102) { 'true' } else { 'false' })
    $env:CraterIt__FixtureProductId = $(if ($EnableFixtureImportForProduct102) { '102' } else { '' })
    Write-Host "Starting Development API on http://localhost:$Port using private crater-artwork storage."
    if ($EnableFixtureImportForProduct102) { Write-Host 'Fixture import enabled for ProductID 102 only. Submit no live write without explicit approval.' }
    Write-Host 'Generation endpoint retains its existing behavior: do not invoke it for a fixture test.'
    Write-Host 'Existing developer authentication/server prerequisites must already be available. No credentials are configured by this script.'
    & dotnet run --project (Join-Path $root 'FlyerMonkey.Api/FlyerMonkey.Api.csproj') --no-restore --no-launch-profile -- --urls "http://localhost:$Port"
    $result = $LASTEXITCODE
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $original[$name], 'Process') }
}
exit $result
