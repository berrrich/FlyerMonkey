# Management-plane READS only; no login, subscription selection, restore or setting changes.
# Example: .\Inspect-AzureRecovery.ps1 -DatabaseResourceId '/subscriptions/.../resourceGroups/.../providers/Microsoft.Sql/servers/.../databases/...'
param([Parameter(Mandatory=$true)][string]$DatabaseResourceId,[string]$ReportPath='.artifacts/mission009i-azure-recovery.json')
$ErrorActionPreference='Stop'
Import-Module Az.Accounts
if(!(Get-AzContext)){throw 'No authenticated Azure management context. An operator must sign in separately.'}
if($DatabaseResourceId -notmatch '^/subscriptions/[a-fA-F0-9-]+/resourceGroups/[^/]+/providers/Microsoft.Sql/servers/[^/]+/databases/[^/]+$'){throw 'Explicit SQL database resource ID required'}
function Read-Arm([string]$path){$r=Invoke-AzRestMethod -Method GET -Path $path;if($r.StatusCode -ne 200){throw 'Azure management read failed'};return ($r.Content|ConvertFrom-Json)}
function Read-Inventory([string]$path){
 $items=@()
 do{
  $page=Read-Arm $path;$items+=@($page.value);$path=$page.nextLink
  if($path){
   if(!$path.StartsWith('https://management.azure.com/')){throw 'Unexpected inventory continuation endpoint'}
   $path=$path.Substring('https://management.azure.com'.Length)
  }
 }while($path)
 return ,$items
}
$db=Read-Arm "${DatabaseResourceId}?api-version=2023-08-01"
$short=Read-Arm "${DatabaseResourceId}/backupShortTermRetentionPolicies/default?api-version=2023-08-01"
$long=Read-Arm "${DatabaseResourceId}/backupLongTermRetentionPolicies/default?api-version=2023-08-01"
$serverId=$DatabaseResourceId.Substring(0,$DatabaseResourceId.LastIndexOf('/databases/'))
$deleted=Read-Inventory "${serverId}/restorableDroppedDatabases?api-version=2023-08-01"
$sub=($DatabaseResourceId -split '/')[2];$serverName=($serverId -split '/')[-1];$dbName=($DatabaseResourceId -split '/')[-1]
$ltr=Read-Inventory "/subscriptions/$sub/providers/Microsoft.Sql/locations/$($db.location)/longTermRetentionServers/$serverName/longTermRetentionDatabases/$dbName/longTermRetentionBackups?api-version=2023-08-01"
$report=[ordered]@{CapturedUtc=[DateTime]::UtcNow.ToString('o');DatabaseResourceId=$db.id;Location=$db.location;Sku=$db.sku;Status=$db.properties.status;EarliestRestoreDate=$db.properties.earliestRestoreDate;CurrentBackupStorageRedundancy=$db.properties.currentBackupStorageRedundancy;RequestedBackupStorageRedundancy=$db.properties.requestedBackupStorageRedundancy;ShortTermRetention=$short.properties;LongTermRetention=$long.properties;LongTermBackupInventory=$ltr;DroppedDatabaseInventory=$deleted;ManagementWritesPerformed=$false;RestoreDrillPerformed=$false}
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent;$path=Join-Path $repo $ReportPath
[void][IO.Directory]::CreateDirectory((Split-Path $path -Parent))
$report|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $path -Encoding UTF8
'Azure recovery inspection saved: '+$ReportPath
