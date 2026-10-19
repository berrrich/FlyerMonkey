[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PreCommitChecks.psm1') -Force
$root = Split-Path $PSScriptRoot -Parent
$failed = 0

function Show-Finding {
    param($Finding)
    # Never emit matching source text or values, including native stderr.
    $path = $Finding.Path -replace '[\r\n\t]', '?'
    Write-Host "  $($Finding.Source): $path : $($Finding.Line) [$($Finding.Rule)] $($Finding.Action)"
}
function Invoke-Check {
    param([string]$Name, [scriptblock]$Work)
    Write-Host "Checking $Name..."
    try { & $Work; Write-Host "PASS $Name" }
    catch {
        $script:failed++
        # Exception messages may contain credentials. Failures use fixed guidance; details are emitted safely by each check.
        Write-Host "FAIL $Name - resolve the diagnostics above; unexpected exceptions are suppressed to protect secrets."
    }
}
function Invoke-DotnetCheck {
    param([string]$Name, [string[]]$DotnetArguments, [string]$Expected = '', [int]$MinimumChecks = 0)
    Invoke-Check $Name {
        $result = Invoke-GateProcess -File dotnet -Arguments $DotnetArguments -Root $root
        $output = $result.Output + "`n" + $result.Error
        # Print only structured compiler locations/codes and counts, never raw tool output or test exception values.
        $diagnostics = @([regex]::Matches($output, '(?m)^(?<location>[^\r\n]*?)\s*:\s*(?<severity>warning|error)\s+(?<code>[A-Z]+\d+)\s*:') |
            ForEach-Object { "$($_.Groups['severity'].Value) $($_.Groups['code'].Value) at $($_.Groups['location'].Value)" } | Sort-Object -Unique)
        foreach ($diagnostic in $diagnostics) {
            if (@(Get-CredentialFindings $diagnostic 'tool-output' 'process').Count -eq 0) { Write-Host "  $diagnostic" }
            else { Write-Host '  Compiler diagnostic location suppressed (potential credential).' }
        }
        Write-Host "  Exit code: $($result.ExitCode); unique compiler diagnostics: $($diagnostics.Count)"
        if ($result.ExitCode -ne 0) {
            Write-Host '  Verify SDK/workloads and restored assets, or inspect the named test/build locally. Raw output is withheld.'
            throw 'Native command failed.'
        }
        $completion = if ($Expected) { [regex]::Match($output, $Expected) } else { $null }
        if ($Expected -and (!$completion.Success -or [int]$completion.Groups['count'].Value -lt $MinimumChecks)) {
            Write-Host '  Expected check-completion summary was missing. Inspect the test runner.'
            throw 'Incomplete checks.'
        }
        if ($Expected) { Write-Host "  $($completion.Value)" }
    }
}

try {
    $repo = Invoke-GateGit $root @('rev-parse', '--show-toplevel')
    if ($repo.ExitCode -ne 0) { throw 'Git repository required.' }
    Write-Host 'FlyerMonkey pre-commit gate (offline; no Azure or generation calls)'
    Invoke-Check 'repository credentials and accidental files (worktree + index)' {
        $findings = @(Get-RepositoryFindings $root)
        foreach ($finding in $findings) { Show-Finding $finding }
        if ($findings.Count) { throw 'Repository findings.' }
    }
    Invoke-Check 'ignore protection' {
        $findings = @(Get-IgnoreFindings $root)
        foreach ($finding in $findings) { Show-Finding $finding }
        if ($findings.Count) { throw 'Ignore findings.' }
    }
    Invoke-Check 'tracked and staged whitespace' {
        foreach ($arguments in @(@('diff','--check'), @('diff','--cached','--check'))) {
            $result = Invoke-GateGit $root $arguments
            if ($result.ExitCode -ne 0) {
                Write-Host '  Whitespace/conflict check failed. Review git diff --check and git diff --cached --check privately.'
                throw 'Whitespace failure.'
            }
        }
    }
    Invoke-Check 'repository/scanner regression checks' {
        $hostFile = (Get-Process -Id $PID).Path
        $result = Invoke-GateProcess -File $hostFile -Arguments @('-NoProfile','-File', (Join-Path $root 'tests/PreCommitGate.Checks.ps1')) -Root $root
        Write-Host "  Exit code: $($result.ExitCode)"
        if ($result.ExitCode -ne 0 -or $result.Output -notmatch '\d+ repository checks passed') {
            Write-Host '  Run tests/PreCommitGate.Checks.ps1 to inspect the named failing assertion (values are suppressed).'
            throw 'Scanner checks failed.'
        }
        Write-Host "  $($Matches[0])"
    }
    Invoke-DotnetCheck 'CraterIt.Checks' @('run','--project','tests/CraterIt.Checks/CraterIt.Checks.csproj','--no-restore','--no-launch-profile','-p:UseAppHost=false') '(?m)^(?<count>\d+) checks passed' 100
    Invoke-DotnetCheck 'ApprovedArtwork.Checks (offline publication boundary and import)' @('run','--project','tests/ApprovedArtwork.Checks/ApprovedArtwork.Checks.csproj','--no-restore','--no-launch-profile') '(?m)^(?<count>\d+) approved artwork checks passed' 60
    # This suite also tests container isolation, private access, create-only writes and non-Development denial.
    Invoke-DotnetCheck 'CraterArtwork.AzureChecks and canonical-container isolation' @('run','--project','tests/CraterArtwork.AzureChecks/CraterArtwork.AzureChecks.csproj','--no-restore','--no-launch-profile') '(?m)^(?<count>\d+) offline Azure checks passed' 54
    Invoke-DotnetCheck 'API build' @('build','FlyerMonkey.Api/FlyerMonkey.Api.csproj','--no-restore','-v','minimal')
    Invoke-DotnetCheck 'Web build' @('build','FlyerMonkey/FlyerMonkey.Web/FlyerMonkey.Web.csproj','--no-restore','-v','minimal')
    Invoke-DotnetCheck 'Windows MAUI build' @('build','FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj','--no-restore','-f','net10.0-windows10.0.19041.0','-v','minimal')
    Invoke-DotnetCheck 'Windows Reviewer build' @('build','FlyerMonkey.Reviewer.Windows/FlyerMonkey.Reviewer.Windows.csproj','--no-restore','-v','minimal')
    Invoke-DotnetCheck 'Android Debug build' @('build','FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj','--no-restore','-f','net10.0-android','-c','Debug','-v','minimal')
    if ($failed) { Write-Host "FAIL quality gate: $failed required check(s) failed. Do not commit."; exit 1 }
    Write-Host 'PASS quality gate: all required checks passed. Review and stage intended changes, then rerun before committing.'
    exit 0
}
catch { Write-Host 'FAIL quality gate: cannot initialize verification. Check Git, PowerShell and repository paths; no secret values were printed.'; exit 1 }
