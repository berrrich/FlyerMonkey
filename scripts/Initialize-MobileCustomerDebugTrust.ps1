[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$Thumbprint)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$resourceRoot = Join-Path $repoRoot '.artifacts/mobile-customer-trust/res'
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$Thumbprint"
if ($certificate.Subject -ne 'CN=localhost' -or $certificate.NotAfter -le (Get-Date) -or
    $certificate.Extensions.Oid.Value -notcontains '1.3.6.1.4.1.311.84.1.1') {
    throw 'Select a current ASP.NET localhost development certificate.'
}
New-Item -ItemType Directory -Force -Path (Join-Path $resourceRoot 'raw'), (Join-Path $resourceRoot 'xml') | Out-Null
# X509ContentType.Cert exports public DER only, never the private key.
[IO.File]::WriteAllBytes((Join-Path $resourceRoot 'raw/mobile_customer_dev_ca.cer'),
    $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
@'
<?xml version="1.0" encoding="utf-8"?>
<network-security-config>
  <base-config cleartextTrafficPermitted="false" />
  <domain-config cleartextTrafficPermitted="false">
    <domain includeSubdomains="false">localhost</domain>
    <trust-anchors>
      <certificates src="system" />
      <certificates src="@raw/mobile_customer_dev_ca" />
    </trust-anchors>
  </domain-config>
</network-security-config>
'@ | Set-Content -LiteralPath (Join-Path $resourceRoot 'xml/crater_debug_network_security_config.xml') -Encoding UTF8
Write-Output 'Generated ignored Android Debug resources: public development certificate; localhost-only trust; cleartext disabled.'
