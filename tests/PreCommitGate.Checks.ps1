Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts/PreCommitChecks.psm1') -Force
$count = 0
function Assert-Check {
    param([bool]$Condition, [string]$Name)
    if (!$Condition) { throw $Name }
    $script:count++
    Write-Host "PASS: $Name"
}
try {
    $dummy = 'synthetic-gate-fixture-value'
    $cases = @(
        @(('"ApiKey": "' + $dummy + '"'), 'SECRET_ASSIGNMENT'),
        @(('"password": "' + $dummy + '"'), 'SECRET_ASSIGNMENT'),
        @(('"ClientSecret": "' + $dummy + '"'), 'SECRET_ASSIGNMENT'),
        @(('"OpenAI__ApiKey": "' + $dummy + '"'), 'SECRET_ASSIGNMENT'),
        @(('<' + 'Password>' + $dummy + '</Password>'), 'XML_SECRET'),
        @(('userPWD="' + $dummy + '"'), 'SECRET_ASSIGNMENT'),
        @(('Pwd' + '=' + $dummy + ';'), 'CONNECTION_CREDENTIAL'),
        @(('AccountKey' + '=' + $dummy + ';'), 'CONNECTION_CREDENTIAL'),
        @(('https://example.invalid/?' + 'sig=' + $dummy), 'SAS_SIGNATURE'),
        @(('sk-' + ('A' * 40)), 'OPENAI_KEY'),
        @(('ghp_' + ('A' * 30)), 'OTHER_API_TOKEN'),
        @(('AKIA' + ('A' * 16)), 'AWS_ACCESS_KEY'),
        @(('-----BEGIN ' + 'PRIVATE KEY-----'), 'PRIVATE_KEY'),
        @(('Server=' + 'example.invalid;Database=Example;'), 'CONNECTION_STRING'),
        @(('eyJ' + ('A' * 10) + '.' + ('B' * 15) + '.' + ('C' * 15)), 'JWT_TOKEN')
    )
    foreach ($case in $cases) {
        $found = @(Get-CredentialFindings $case[0] 'fixture.txt' 'fixture')
        Assert-Check ($found.Rule -contains $case[1]) "Detect $($case[1])"
        $serialized = $found | ConvertTo-Json -Compress
        Assert-Check (!$serialized.Contains($dummy)) 'Findings never contain the matched value'
    }
    Assert-Check (@(Get-CredentialFindings '"ApiKey": "YOUR_API_KEY"' 'fixture.txt' 'fixture').Count -eq 0) 'Unmistakable placeholder is allowed'
    Assert-Check (@(Get-CredentialFindings 'configuration["OpenAI:ApiKey"]' 'fixture.txt' 'fixture').Count -eq 0) 'Configuration lookup is allowed'
    $integrated = 'Server' + '=example.invalid;Database=Example;Integrated Security=True;'
    Assert-Check (@(Get-CredentialFindings $integrated 'fixture.txt' 'fixture').Count -eq 0) 'Windows-integrated SQL string without embedded credentials is allowed'
    Assert-Check (@(Get-CredentialFindings ($integrated + 'Pwd' + '=' + $dummy) 'fixture.txt' 'fixture').Count -gt 0) 'Integrated-security label cannot hide an embedded password'

    # Disposable repository only; no commits, original index changes, or cleanup/deletion.
    $fixture = Join-Path ([IO.Path]::GetTempPath()) ('FlyerMonkey-GateChecks-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $fixture
    $init = Invoke-GateGit $fixture @('init','--quiet')
    if ($init.ExitCode -ne 0) { throw 'Cannot initialize isolated Git fixture' }
    $repoRoot = Split-Path $PSScriptRoot -Parent
    [IO.File]::WriteAllText((Join-Path $fixture '.gitignore'), [IO.File]::ReadAllText((Join-Path $repoRoot '.gitignore')))
    Assert-Check (@(Get-IgnoreFindings $fixture).Count -eq 0) 'Secret/artifact ignore probes pass'
    $configPath = Join-Path $fixture 'appsettings.Development.json'
    [IO.File]::WriteAllText($configPath, '{"Logging":{"LogLevel":{"Default":"Information"}}}')
    $stage = Invoke-GateGit $fixture @('add','-f','--','appsettings.Development.json')
    if ($stage.ExitCode -ne 0) { throw 'Cannot stage isolated template' }
    Assert-Check (@(Get-RepositoryFindings $fixture).Count -eq 0) 'Tracked logging-only Development template is accepted'
    [IO.File]::WriteAllText($configPath, '{"CraterIt":{"Enabled":true}}')
    $findings = @(Get-RepositoryFindings $fixture)
    Assert-Check (@($findings | Where-Object { $_.Rule -eq 'TRACKED_DEVELOPMENT_CONFIG' -and $_.Source -eq 'worktree' }).Count -eq 1) 'Tracked development configuration beyond logging is rejected'
    [IO.File]::WriteAllText($configPath, '{"Logging":{"LogLevel":{"Default":"Information"}}}')
    $file = Join-Path $fixture 'staged.json'
    [IO.File]::WriteAllText($file, '"ApiKey": "' + $dummy + '"')
    $stage = Invoke-GateGit $fixture @('add','--','staged.json')
    if ($stage.ExitCode -ne 0) { throw 'Cannot stage isolated synthetic fixture' }
    [IO.File]::WriteAllText($file, '{}')
    $unicodePath = Join-Path $fixture 'unicode.json'
    [IO.File]::WriteAllText($unicodePath, ('"ApiKey": "' + $dummy + '"'), [Text.Encoding]::Unicode)
    $stage = Invoke-GateGit $fixture @('add','--','unicode.json')
    if ($stage.ExitCode -ne 0) { throw 'Cannot stage isolated Unicode fixture' }
    $findings = @(Get-RepositoryFindings $fixture | Where-Object { $_.Path -eq 'unicode.json' -and $_.Rule -eq 'SECRET_ASSIGNMENT' })
    Assert-Check ($findings.Count -eq 2) 'UTF-16 BOM text is scanned in both index and worktree'
    [IO.File]::WriteAllText($unicodePath, '{}')
    $stage = Invoke-GateGit $fixture @('add','--','unicode.json')
    if ($stage.ExitCode -ne 0) { throw 'Cannot update isolated Unicode fixture' }
    $findings = @(Get-RepositoryFindings $fixture)
    Assert-Check (@($findings | Where-Object { $_.Source -eq 'index' -and $_.Rule -eq 'SECRET_ASSIGNMENT' }).Count -eq 1) 'Index secret is detected even when worktree is safe'
    $stage = Invoke-GateGit $fixture @('add','--','staged.json')
    if ($stage.ExitCode -ne 0) { throw 'Cannot update isolated synthetic fixture' }
    [IO.File]::WriteAllText($file, '"ApiKey": "' + $dummy + '"')
    $findings = @(Get-RepositoryFindings $fixture)
    Assert-Check (@($findings | Where-Object { $_.Source -eq 'worktree' -and $_.Rule -eq 'SECRET_ASSIGNMENT' }).Count -eq 1) 'Worktree secret is detected even when index is safe'
    [IO.File]::WriteAllText($file, '{}')
    $data = Join-Path $fixture 'DATA'
    $null = New-Item -ItemType Directory -Path $data
    [IO.File]::WriteAllText((Join-Path $data 'fixture.db'), 'synthetic database fixture')
    $stage = Invoke-GateGit $fixture @('add','-f','--','DATA/fixture.db')
    if ($stage.ExitCode -ne 0) { throw 'Cannot stage isolated artifact fixture' }
    Assert-Check (@(Get-RepositoryFindings $fixture | Where-Object { $_.Rule -eq 'FORBIDDEN_FILE' }).Count -ge 1) 'Forced staging cannot bypass ignored-artifact check'
    [IO.File]::WriteAllText((Join-Path $fixture '.gitignore'), '')
    Assert-Check (@(Get-IgnoreFindings $fixture).Count -gt 0) 'Missing ignore rules fail'
    # Evaluate real build items: the localhost HTTP policy must not ship in Release.
    $mauiProject = 'FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj'
    foreach ($configuration in @('Debug', 'Release')) {
        $evaluation = Invoke-GateProcess -File dotnet -Arguments @('msbuild', $mauiProject,
            '-p:TargetFramework=net10.0-android', "-p:Configuration=$configuration", '-getItem:AndroidManifestOverlay,AndroidResource') -Root $repoRoot
        if ($evaluation.ExitCode -ne 0) { throw 'Cannot evaluate Android network-policy build items' }
        $items = ($evaluation.Output | ConvertFrom-Json).Items
        $overlayCount = @($items.AndroidManifestOverlay | Where-Object { $_.Identity -match 'AndroidManifest\.Debug\.xml$' }).Count
        $resourceCount = @($items.AndroidResource | Where-Object { $_.Identity -match 'crater_debug_network_security_config\.xml$' }).Count
        $expected = if ($configuration -eq 'Debug') { 1 } else { 0 }
        Assert-Check ($overlayCount -eq $expected) "Android $configuration selects only the appropriate manifest overlay"
        Assert-Check ($resourceCount -eq $expected) "Android $configuration includes/excludes localhost HTTP resource"
    }
    [xml]$policy = [IO.File]::ReadAllText((Join-Path $repoRoot 'FlyerMonkey/FlyerMonkey/Platforms/Android/Resources/xml/crater_debug_network_security_config.xml'))
    Assert-Check ($policy.'network-security-config'.'base-config'.cleartextTrafficPermitted -eq 'false') 'Android Debug denies general cleartext traffic'
    $domains = @($policy.SelectNodes('/network-security-config/domain-config[@cleartextTrafficPermitted="true"]/domain'))
    Assert-Check ($domains.Count -eq 2 -and @($domains | Where-Object { $_.InnerText -notin @('localhost', '127.0.0.1') -or $_.includeSubdomains -ne 'false' }).Count -eq 0) 'Android cleartext allowance covers exact loopback names only'
    Assert-Check ($policy.SelectNodes('//trust-anchors|//debug-overrides').Count -eq 0) 'Android policy does not override certificate trust'
    Write-Host "$count repository checks passed. Fixtures remain in the system temporary directory; no commits or cleanup performed."
    exit 0
}
catch {
    # Assertion names are controlled above; never print exception objects, source lines or fixture values.
    Write-Host "FAIL repository regression checks after $count assertions. Inspect the assertion immediately following the last PASS."
    exit 1
}
