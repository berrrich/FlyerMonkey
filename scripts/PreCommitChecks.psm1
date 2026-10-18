Set-StrictMode -Version Latest

function Invoke-GateProcess {
    param([string]$File, [string[]]$Arguments, [string]$Root)
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $File
    $info.WorkingDirectory = $Root
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $quoted = foreach ($argument in $Arguments) {
        '"' + [regex]::Replace([regex]::Replace($argument, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
    }
    $info.Arguments = $quoted -join ' '
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $info
    $buffer = New-Object System.IO.MemoryStream
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($buffer)
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $null = $stdout.GetAwaiter().GetResult()
        $buffer.Position = 0
        $reader = New-Object System.IO.StreamReader($buffer, [Text.Encoding]::UTF8, $true)
        try { $output = $reader.ReadToEnd() } finally { $reader.Dispose() }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output; Error = $stderr.GetAwaiter().GetResult() }
    }
    finally { $buffer.Dispose(); $process.Dispose() }
}

function Invoke-GateGit {
    param([string]$Root, [string[]]$GitArguments)
    Invoke-GateProcess -File git -Arguments (@('-c', 'core.quotepath=false') + $GitArguments) -Root $Root
}

function Test-Placeholder {
    param([string]$Value)
    $Value = $Value.Trim()
    # Only unmistakable placeholders/dynamic expressions; do not exempt whole files or ordinary words like "password".
    return $Value -match '^(?:<[^>]+>|\$\{[^}]+\}|\$\([^)]+\)|\{[^}]+\}|EXAMPLE_NOT_A_SECRET|YOUR_[A-Z0-9_]+|REPLACE_ME|\*{3,})$'
}

function Get-CredentialFindings {
    param([string]$Text, [string]$Path, [string]$Source)
    $rules = [ordered]@{
        'PRIVATE_KEY' = '-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'
        'OPENAI_KEY' = '\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{20,}\b'
        'OTHER_API_TOKEN' = '\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AIza[A-Za-z0-9_-]{30,})\b'
        'AWS_ACCESS_KEY' = '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'
        'SAS_SIGNATURE' = '(?i)(?:[?&]|&amp;)sig=(?<value>[^&\s"''<>]+)'
        'CONNECTION_CREDENTIAL' = '(?i)\b(?:Password|Pwd|UserPWD|AccountKey|SharedAccessKey)\s*=\s*(?<value>[^;\s"''<>]+)'
        'SECRET_ASSIGNMENT' = '(?i)(?<![\w])(?:["'']?(?:[A-Za-z0-9]+[:_]+)*(?:password|pwd|userpwd|accountkey|sharedaccesskey|apikey|api_key|clientsecret|client_secret|access_token|sas_token|connectionstring|connection_string|FLYERMONKEY_SQL_CONNECTION)["'']?)\s*(?:=|:)\s*["''](?<value>[^"''\r\n]+)["'']'
        'XML_SECRET' = '(?i)<(?:Password|Pwd|UserPWD|AccountKey|ApiKey|ClientSecret)>\s*(?<value>[^<\r\n]+)\s*</'
        ('CONNECTION' + '_STRING') = '(?i)\b(?:Server|Data Source|DefaultEndpointsProtocol)\s*=\s*(?<value>[^"''\r\n]+)'
        'JWT_TOKEN' = '\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b'
    }
    $lineNumber = 0
    foreach ($line in ($Text -split '\r?\n')) {
        $lineNumber++
        foreach ($rule in $rules.Keys) {
            foreach ($match in [regex]::Matches($line, $rules[$rule])) {
                if ($match.Groups['value'].Success -and (Test-Placeholder $match.Groups['value'].Value)) { continue }
                # Windows-integrated SQL strings carry no embedded login; password/key rules still run independently.
                if ($rule -eq 'CONNECTION_STRING' -and $match.Value -match '(?i)(?:Trusted_Connection\s*=\s*True|Integrated Security\s*=\s*(?:True|SSPI))(?:;|$)' -and
                    $match.Value -notmatch '(?i)(?:Password|Pwd|User ID|UID|AccountKey|SharedAccessKey)\s*=') { continue }
                # Exact, reviewed dummy URI already used to test SAS rejection; no whole-file exemption.
                if ($Path -eq 'tests/CraterArtwork.AzureChecks/Program.cs' -and $rule -eq 'SAS_SIGNATURE' -and
                    $match.Value -eq ('?sig=' + 'secret') -and $line.Contains(('"https://account.blob.core.windows.net/?sig=' + 'secret"'))) { continue }
                [pscustomobject]@{ Source = $Source; Path = $Path; Line = $lineNumber; Rule = $rule;
                    Action = 'Potential credential: replace with server-side configuration; review privately. Value suppressed.' }
            }
        }
    }
}

function Test-ForbiddenPath {
    param([string]$Path)
    return $Path -match '(?i)(^|/)(?:bin|obj|\.vs|\.azure|\.aws|CraterArt|ProductImages)(/|$)|^DATA/|(^|/)(?:\.env(?:\..*)?|secrets\.json|appsettings\.Local\.json|launchSettings\.local\.json)$|\.(?:db(?:-shm|-wal)?|sqlite3?(?:-shm|-wal)?|mdf|ldf|pfx|p12|pem|key|tmp|temp|bak|log|dll|exe|pdb|nupkg|msix|cache|pubxml\.user)$'
}

function Get-RepositoryFindings {
    param([string]$Root)
    $files = Invoke-GateGit $Root @('ls-files', '--cached', '--others', '--exclude-standard', '-z')
    $index = Invoke-GateGit $Root @('ls-files', '--cached', '-z')
    if ($files.ExitCode -ne 0 -or $index.ExitCode -ne 0) { throw 'Cannot enumerate Git worktree/index.' }
    $indexed = @($index.Output.Split([char]0) | Where-Object { $_ })
    $worktree = @($files.Output.Split([char]0) | Where-Object { $_ } | Sort-Object -Unique)
    foreach ($source in @('worktree', 'index')) {
        $paths = if ($source -eq 'index') { $indexed } else { $worktree }
        foreach ($path in $paths) {
            $fullPath = Join-Path $Root $path
            if ($source -eq 'worktree' -and !(Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
            if (Test-ForbiddenPath $path) {
                [pscustomobject]@{ Source = $source; Path = $path; Line = 0; Rule = 'FORBIDDEN_FILE';
                    Action = 'Local secret, generated output, database, key or temporary file must not be committed.' }
                continue
            }
            if ($source -eq 'index') {
                $blob = Invoke-GateGit $Root @('show', ":$path")
                if ($blob.ExitCode -ne 0) { throw 'Cannot read an indexed file (resolve conflicts/submodules before running the gate).' }
                $text = $blob.Output
            }
            else { $text = [System.IO.File]::ReadAllText($fullPath) }
            # Binary image/font/etc content is not treated as text. Forbidden paths are still checked above.
            if ($text.Contains([string][char]0)) { continue }
            if ($path -match '(^|/)appsettings\.Development\.json$') {
                $safeTemplate = $false
                try {
                    $config = $text | ConvertFrom-Json
                    $keys = @($config.PSObject.Properties.Name)
                    $safeTemplate = $keys.Count -eq 1 -and $keys[0] -eq 'Logging' -and
                        @($config.Logging.PSObject.Properties.Name).Count -eq 1 -and
                        $config.Logging.PSObject.Properties.Name -eq 'LogLevel'
                    foreach ($level in $config.Logging.LogLevel.PSObject.Properties) {
                        if ($level.Value -notin @('Trace','Debug','Information','Warning','Error','Critical','None')) { $safeTemplate = $false }
                    }
                } catch { $safeTemplate = $false }
                if (!$safeTemplate) {
                    [pscustomobject]@{ Source = $source; Path = $path; Line = 0; Rule = 'TRACKED_DEVELOPMENT_CONFIG';
                        Action = 'Only logging-level templates may be tracked; keep machine-specific settings outside Git.' }
                }
            }
            Get-CredentialFindings -Text $text -Path $path -Source $source
        }
    }
}

function Get-IgnoreFindings {
    param([string]$Root)
    $probes = @('.env', '.env.local', 'FlyerMonkey.Api/secrets.json', 'FlyerMonkey.Api/appsettings.Development.json',
        'FlyerMonkey.Api/appsettings.Local.json', 'FlyerMonkey.Api/Properties/launchSettings.local.json',
        '.azure/accessTokens.json', '.aws/credentials', 'probe.pfx', 'probe.pem',
        'FlyerMonkey.Api/Properties/PublishProfiles/probe.pubxml.user',
        'DATA/CraterArt/probe/artwork.jpg', 'DATA/CraterArt/.staging-probe/metadata.json',
        'probe.db', 'probe.db-wal', 'probe.sqlite', 'probe.mdf', 'probe.tmp', 'probe.log',
        'FlyerMonkey.Api/bin/probe.dll', 'FlyerMonkey.Api/obj/probe.json')
    foreach ($probe in $probes) {
        # --no-index tests the rule even when a logging-only Development template is already tracked.
        $result = Invoke-GateGit $Root @('check-ignore', '--no-index', '-q', '--', $probe)
        if ($result.ExitCode -gt 1) { throw 'Git ignore validation failed.' }
        if ($result.ExitCode -ne 0) {
            [pscustomobject]@{ Source = 'ignore'; Path = $probe; Line = 0; Rule = 'MISSING_IGNORE';
                Action = 'Add a repository ignore rule for this local-only path.' }
        }
    }
}

Export-ModuleMember -Function Invoke-GateProcess, Invoke-GateGit, Get-CredentialFindings, Get-RepositoryFindings, Get-IgnoreFindings
