[CmdletBinding()]
param([switch]$LiveReadOnly, [ValidateRange(1024,65535)][int]$Port = 5146)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (!$LiveReadOnly) { Write-Host 'NOT RUN: -LiveReadOnly explicitly starts two local API instances and performs Azure-backed GET requests only.'; exit 2 }
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $root 'scripts/PreCommitChecks.psm1') -Force
$apiDirectory = Join-Path $root 'FlyerMonkey.Api'
$apiDll = Join-Path $apiDirectory 'bin/Debug/net10.0/FlyerMonkey.Api.dll'
$probeDll = Join-Path $root 'tests/CraterArtwork.ApiReadOnly/bin/Debug/net10.0/CraterArtwork.ApiReadOnly.dll'
try {
    if (!(Test-Path -LiteralPath $apiDll) -or !(Test-Path -LiteralPath $probeDll)) { throw 'Build prerequisites missing' }
    if (![Environment]::GetEnvironmentVariable('FLYERMONKEY_SQL_CONNECTION','Process')) { throw 'API SQL startup prerequisite missing' }
    foreach ($credential in @('AZURE_CLIENT_SECRET', 'AZURE_CLIENT_CERTIFICATE_PATH', 'AZURE_FEDERATED_TOKEN_FILE', 'AZURE_PASSWORD')) {
        if ([Environment]::GetEnvironmentVariable($credential, 'Process')) { throw 'Nondeveloper credential configuration present; resolve manually' }
    }
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $alreadyListening = $false
        try { $tcp.Connect('localhost', $Port); $alreadyListening = $true } catch { } finally { $tcp.Dispose() }
        if ($alreadyListening) { Write-Host "BLOCKED: localhost:$Port is already occupied; existing processes left untouched."; throw 'Port already occupied' }
        $info = New-Object System.Diagnostics.ProcessStartInfo
        $info.FileName = 'dotnet'
        $info.Arguments = '"' + $apiDll + '" --urls http://localhost:' + $Port
        $info.WorkingDirectory = $apiDirectory
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.EnvironmentVariables['DOTNET_ENVIRONMENT'] = 'Development'
        $info.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Development'
        $info.EnvironmentVariables['CraterIt__Enabled'] = 'true'
        $info.EnvironmentVariables['CraterArt__Provider'] = 'Azure'
        $info.EnvironmentVariables['CraterArt__Azure__ServiceUri'] = 'https://flyermonkey.blob.core.windows.net/'
        $info.EnvironmentVariables['CraterArt__Azure__Container'] = 'crater-artwork'
        $api = New-Object System.Diagnostics.Process
        $api.StartInfo = $info
        $null = $api.Start()
        $stdout = $api.StandardOutput.ReadToEndAsync()
        $stderr = $api.StandardError.ReadToEndAsync()
        try {
            $ready = $false
            for ($poll = 0; $poll -lt 30; $poll++) {
                if ($api.HasExited) { throw 'API startup failed' }
                try {
                    # Missing-header request is rejected before Azure access and proves the local listener is ready.
                    $null = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:$Port/api/images/artwork?productId=2147483647" -TimeoutSec 1 -ErrorAction Stop
                } catch {
                    if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { $ready = $true; break }
                }
                Start-Sleep -Milliseconds 250
            }
            if (!$ready) { throw 'API listener unavailable' }
            Write-Host "API instance $attempt ready on localhost:$Port (owned PID $($api.Id)); running GET-only registered-client probe."
            $result = Invoke-GateProcess -File dotnet -Arguments @($probeDll, '--verify-existing-fixture', '--port', "$Port") -Root $root
            Write-Host $result.Output.TrimEnd()
            Write-Host "READ_ONLY_PROBE_${attempt}_EXIT=$($result.ExitCode)"
            if ($result.ExitCode -ne 0) { Write-Host 'Probe failed. Check existing Visual Studio sign-in, container access and API startup configuration. Raw server/credential logs withheld.'; throw 'Read-only probe failed' }
        }
        finally {
            # Only the process object created above is stopped; never stop a pre-existing API or unrelated process.
            if (!$api.HasExited) { $api.Kill(); $api.WaitForExit() }
            $api.Dispose()
        }
    }
    Write-Host 'PASS existing Azure fixture is retrievable across a complete local API process restart. Actual Web UI/Android device and new API writes remain untested.'
    exit 0
}
catch {
    Write-Host "FAIL read-only API verification. Confirm built binaries, free localhost:$Port, existing SQL startup configuration and developer Azure access. No writes or retries of paid operations occurred; raw details withheld."
    exit 1
}
