$ErrorActionPreference = 'Stop'
$statusUrl = 'https://localhost:7255/api/status'
$portMapping = 'tcp:7255'

function Get-ApiStatus {
    try {
        $status = Invoke-RestMethod -Uri $statusUrl -Method Get -TimeoutSec 3 -MaximumRedirection 0
        if ($status.application -eq 'FlyerMonkey.Api' -and $status.status -eq 'OK') { return $status }
    }
    catch {
        return $null
    }
}

function Write-Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $result = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host ('{0}  {1} - {2}' -f $result, $Name, $Detail)
}

$apiReady = $false
$httpsReady = $false
$apiDetail = 'Status check failed.'
$apiProcess = $null
$launchLock = [System.Threading.Mutex]::new($false, 'Local\FlyerMonkeyDevApi7255')
$hasLock = $false
try {
    try {
        $hasLock = $launchLock.WaitOne([TimeSpan]::FromSeconds(65))
    }
    catch [System.Threading.AbandonedMutexException] {
        $hasLock = $true
    }
    if (!$hasLock) { throw 'Another launcher is still checking port 7255.' }

    $status = Get-ApiStatus
    if ($status) {
        $httpsReady = $true
        $apiReady = $status.androidGenerationReady -is [bool] -and $status.androidGenerationReady
        $apiDetail = if ($apiReady) {
            'Reused the existing customer generation API.'
        } else {
            'Existing API did not report customer generation ready. Restart it with the operator-local profile.'
        }
    }
    else {
        # Include both IPv4 and IPv6 listeners, even if HTTPS validation failed.
        $listener = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().
            GetActiveTcpListeners() | Where-Object Port -eq 7255
        if ($listener) { throw 'Port 7255 is occupied, but the FlyerMonkey HTTPS status check failed.' }

        $dotnet = (Get-Command dotnet -CommandType Application).Source
        $log = Join-Path ([System.IO.Path]::GetTempPath()) ('FlyerMonkeyDev-' + [guid]::NewGuid())
        $apiProcess = Start-Process -FilePath $dotnet -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput "$log.out.log" -RedirectStandardError "$log.err.log" -ArgumentList @(
                'run', '--no-restore', '--project', 'FlyerMonkey.Api/FlyerMonkey.Api.csproj',
                '--launch-profile', 'operator-local'
            )
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        do {
            $status = Get-ApiStatus
            if (!$status -and !$apiProcess.HasExited) { Start-Sleep -Milliseconds 500 }
        } until ($status -or $apiProcess.HasExited -or [DateTime]::UtcNow -ge $deadline)
        $httpsReady = [bool]$status
        $apiReady = $status.androidGenerationReady -is [bool] -and $status.androidGenerationReady
        $apiDetail = if ($apiReady) {
            'Started operator-local with customer generation enabled.'
        } elseif ($httpsReady) {
            'API did not report customer generation ready. Check the local customer allowlist.'
        } else {
            "API did not pass its startup check. Logs: $log.out.log and $log.err.log"
        }
    }
}
catch {
    $apiDetail = $_.Exception.Message
}
finally {
    if ($hasLock) { $launchLock.ReleaseMutex() }
    $launchLock.Dispose()
}

$serial = $null
$reverseReady = $false
$deviceDetail = 'Start one Android emulator and wait for ADB to show it as device.'
$reverseDetail = 'Skipped because no single emulator was selected.'
try {
    $adb = (Get-Command adb -CommandType Application).Source
    $devices = @(& $adb devices)
    if ($LASTEXITCODE -ne 0) { throw 'adb devices failed.' }
    $emulators = @($devices | Select-String '^\s*(emulator-\d+)\s+device\s*$')
    if ($emulators.Count -eq 1) {
        $serial = $emulators[0].Matches[0].Groups[1].Value
        $deviceDetail = $serial
        $null = & $adb -s $serial reverse $portMapping $portMapping
        if ($LASTEXITCODE -ne 0) { throw 'adb reverse failed.' }
        $mappings = @(& $adb -s $serial reverse --list)
        $reverseReady = ($LASTEXITCODE -eq 0 -and ($mappings -match '\btcp:7255\s+tcp:7255\s*$').Count -gt 0)
        $reverseDetail = if ($reverseReady) { 'tcp:7255 -> tcp:7255' } else { 'Mapping could not be verified.' }
    }
    elseif ($emulators.Count -gt 1) {
        $deviceDetail = 'Multiple emulators are connected. Leave one emulator connected and rerun.'
    }
}
catch {
    if ($serial) { $reverseDetail = $_.Exception.Message }
    else { $deviceDetail = $_.Exception.Message }
}

Write-Check 'Local API' $apiReady $apiDetail
Write-Check 'HTTPS endpoint' $httpsReady $statusUrl
Write-Check 'Android device' ([bool]$serial) $deviceDetail
Write-Check 'ADB reverse' $reverseReady $reverseDetail
if (!$apiReady -or !$reverseReady) { exit 1 }
exit 0
