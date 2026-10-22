[CmdletBinding()]
param([switch]$Operator, [switch]$WebCredential)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (!$Operator -and !$WebCredential) { throw 'Choose -Operator, -WebCredential, or both. Values are requested through masked prompts.' }
$repositoryRoot = Split-Path $PSScriptRoot -Parent

function Set-PrivateUserSecret([string]$ProjectPath, [string]$Name, [string]$Value) {
    [xml]$projectXml = [IO.File]::ReadAllText((Join-Path $repositoryRoot $ProjectPath))
    $secretStoreId = [string]$projectXml.Project.PropertyGroup.UserSecretsId
    $parsedId = [Guid]::Empty
    if (![Guid]::TryParse($secretStoreId, [ref]$parsedId) -or $parsedId -eq [Guid]::Empty) { throw 'Expected project UserSecretsId missing.' }
    $payload = @{ $Name = $Value } | ConvertTo-Json -Compress
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = 'dotnet'
    # Only a validated nonsecret store ID appears in process arguments. Secret JSON goes through stdin.
    $start.Arguments = 'user-secrets set --id ' + $parsedId.ToString('D')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        if (!$process.Start()) { throw 'Could not start local Secret Manager.' }
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.WriteLine($payload)
        $process.StandardInput.Close()
        $process.WaitForExit()
        $null = $outputTask.GetAwaiter().GetResult()
        $null = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'Secret Manager failed. Output withheld to avoid disclosing values.' }
    }
    finally { $payload = $null; $Value = $null; $process.Dispose() }
}

function Read-PrivateValue([string]$Prompt, [scriptblock]$Save) {
    $masked = Read-Host $Prompt -AsSecureString
    $pointer = [IntPtr]::Zero
    $plain = $null
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($masked)
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ([string]::IsNullOrWhiteSpace($plain)) { throw 'A nonempty value is required.' }
        & $Save $plain
    }
    finally {
        $plain = $null
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
        $masked.Dispose()
    }
}

if ($Operator) {
    Read-PrivateValue 'Operator Object ID (masked; not the application ID)' {
        param($operatorValue)
        $operatorId = [Guid]::Empty
        if (![Guid]::TryParse($operatorValue, [ref]$operatorId) -or $operatorId -eq [Guid]::Empty) { throw 'Operator Object ID must be a nonempty GUID.' }
        foreach ($project in @('FlyerMonkey.Api/FlyerMonkey.Api.csproj', 'FlyerMonkey/FlyerMonkey.Web/FlyerMonkey.Web.csproj')) {
            Set-PrivateUserSecret $project 'Authentication:OperatorObjectId' $operatorId.ToString('D')
        }
    }
    Write-Host 'Operator ID saved to both existing local User Secrets stores. Value withheld.'
}
if ($WebCredential) {
    Read-PrivateValue 'FlyerMonkey Web client secret VALUE (masked; not Secret ID)' {
        param($credentialValue)
        Set-PrivateUserSecret 'FlyerMonkey/FlyerMonkey.Web/FlyerMonkey.Web.csproj' 'Authentication:WebClientSecret' $credentialValue
    }
    Write-Host 'Web credential saved to the existing Web User Secrets store. Value withheld.'
}
Write-Host 'Authentication was not enabled. No Azure changes were made. User Secrets are development-only plaintext files protected by your local account permissions.'
