param(
    [Parameter(Mandatory=$true)][Security.SecureString]$AccessToken,
    [string]$CheckpointRoot = (Join-Path $PSScriptRoot '../../DATA/ApiRecovery')
)
# GET-only capture. Supply a current Entra token authorized for this App Service's
# SCM endpoint, for example via Read-Host -AsSecureString. Never put tokens in argv.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$folder = Join-Path ([IO.Path]::GetFullPath($CheckpointRoot)) ('FlyerMonkeyApi-' + $stamp)
New-Item -ItemType Directory -Path $folder | Out-Null
$zipPath = Join-Path $folder 'wwwroot.zip'
$verified = $false
$tokenPointer = [IntPtr]::Zero
try {
    $tokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($AccessToken)
    $headers = @{ Authorization = 'Bearer ' + [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPointer) }
    $scm = 'https://flyermonkeyapi-g7htasdacxfzgbcd.scm.australiaeast-01.azurewebsites.net'
    Invoke-WebRequest -Uri ($scm + '/api/zip/site/wwwroot/') -Headers $headers -OutFile $zipPath -UseBasicParsing -MaximumRedirection 0 | Out-Null
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
        foreach ($required in @('FlyerMonkey.Api.dll','FlyerMonkey.Api.deps.json','FlyerMonkey.Api.runtimeconfig.json','web.config','approved-artwork.json')) {
            if (@($names | Where-Object { $_ -ceq $required }).Count -ne 1) { throw "Missing or duplicated required entry: $required" }
        }
        $inventory = foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\','/')
            if ($name.StartsWith('/') -or $name.Contains(':') -or $name.Split('/') -contains '..') { throw 'Unsafe ZIP entry' }
            if ($name.EndsWith('/')) { continue }
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
            finally { $sha.Dispose(); $stream.Dispose() }
            [pscustomobject]@{ Path=$name; Length=$entry.Length; Sha256=$hash }
        }
        $approval = $archive.GetEntry('approved-artwork.json')
        $reader = [IO.StreamReader]::new($approval.Open())
        try { $manifestText = $reader.ReadToEnd(); $manifest = $manifestText | ConvertFrom-Json }
        finally { $reader.Dispose() }
        if ($manifest.SchemaVersion -ne 1 -or $null -eq $manifest.Entries) { throw 'Unrecognized approval manifest' }
        [IO.File]::WriteAllText((Join-Path $folder 'approved-artwork.json'), $manifestText)
        $inventory | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $folder 'file-hashes.json')
    } finally { $archive.Dispose() }
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    [pscustomobject]@{
        App='FlyerMonkeyApi'; CapturedUtc=[DateTimeOffset]::UtcNow.ToString('o');
        Source='Kudu GET /api/zip/site/wwwroot/'; ZipSha256=$zipHash;
        Files=@($inventory).Count; ApprovalEntries=@($manifest.Entries).Count;
        Verification='All file streams read and hashed; required files and manifest structure checked. Restore not rehearsed.'
    } | ConvertTo-Json | Set-Content (Join-Path $folder 'checkpoint.json')
    $verified = $true
    Write-Output "Verified checkpoint: $folder"
} finally {
    $headers = $null
    if ($tokenPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPointer) }
    if (!$verified) { Write-Warning "Capture incomplete; do not use as recovery checkpoint: $folder" }
}
