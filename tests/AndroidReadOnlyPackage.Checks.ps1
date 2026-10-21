param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'FlyerMonkey/FlyerMonkey'
$count = 0
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw $Name }
    $script:count++
    Write-Output "PASS: $Name"
}
$androidNs = 'http://schemas.android.com/apk/res/android'
$manifestPath = Join-Path $app "obj/$Configuration/net10.0-android/android/AndroidManifest.xml"
[xml]$manifest = [IO.File]::ReadAllText($manifestPath)
Check ($manifest.manifest.package -eq 'com.companyname.flyermonkey') 'Expected application identity'
Check ($manifest.manifest.application.GetAttribute('usesCleartextTraffic', $androidNs) -eq 'false') 'Explicit cleartext denial in merged manifest'
Check (@($manifest.manifest.'uses-permission' | Where-Object { $_.GetAttribute('name', $androidNs) -eq 'android.permission.INTERNET' }).Count -eq 1) 'Internet permission packaged'
$debug = $Configuration -eq 'Debug'
Check (($manifest.manifest.application.GetAttribute('debuggable', $androidNs) -eq 'true') -eq $debug) 'Debuggable flag matches configuration'
Check (($manifest.manifest.application.GetAttribute('networkSecurityConfig', $androidNs) -eq '@xml/crater_debug_network_security_config') -eq $debug) 'Localhost cleartext overlay is Debug only'
$properties = & dotnet msbuild (Join-Path $app 'FlyerMonkey.csproj') -nologo -p:TargetFramework=net10.0-android "-p:Configuration=$Configuration" -getProperty:DefineConstants,JsonSerializerIsReflectionEnabledByDefault
if ($LASTEXITCODE -ne 0) { throw 'Cannot evaluate build properties' }
$properties = ($properties -join "`n") | ConvertFrom-Json
Check ([bool]($properties.Properties.DefineConstants -match '(^|;)DEBUG(;|$)') -eq $debug) 'DEBUG compiler symbol matches configuration'
Check ($properties.Properties.JsonSerializerIsReflectionEnabledByDefault -eq 'true') 'Current HTTP JSON clients retain configured reflection support'
$apk = Join-Path $app "bin/$Configuration/net10.0-android/com.companyname.flyermonkey-Signed.apk"
Check (Test-Path -LiteralPath $apk) 'Local signed APK exists (signer requires separate verification)'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($apk)
try {
    $names = @($archive.Entries.FullName)
    foreach ($asset in @('index.html','css/app.css','FlyerMonkey.styles.css',
        '_framework/blazor.webview.js','_content/FlyerMonkey.Shared/app.css',
        '_content/FlyerMonkey.Shared/lib/bootstrap/dist/css/bootstrap.min.css',
        '_content/FlyerMonkey.Shared/images/product-placeholder.png',
        '_content/FlyerMonkey.Shared/craterInvitation.js','_content/FlyerMonkey.Shared/craterTiming.js')) {
        Check ($names -contains "assets/wwwroot/$asset") "Packaged asset: $asset"
    }
    if (!$debug) {
        Check (@($names | Where-Object { $_ -match 'crater_debug_network_security_config' }).Count -eq 0) 'Release APK excludes localhost network-security resource'
    }
    foreach ($asset in @('index.html','_content/FlyerMonkey.Shared/craterInvitation.js','_content/FlyerMonkey.Shared/craterTiming.js')) {
        $reader = [IO.StreamReader]::new($archive.GetEntry("assets/wwwroot/$asset").Open())
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
        Check ($text -notmatch 'http://(?:localhost|127\.0\.0\.1|10\.0\.2\.2)|[A-Z]:\\Users\\') "No development server/file dependency in $asset"
    }
} finally { $archive.Dispose() }
Write-Output "$count Android $Configuration package checks passed. No installation or API calls."
