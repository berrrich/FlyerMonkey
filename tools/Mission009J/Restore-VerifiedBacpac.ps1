<#
Restore the verified pre-cleanup BACPAC into a NEW local-only disposable database.
Never reads production credentials, connects to Azure, overwrites a database or deletes one.
Optional -RehearseCleanup runs 009I rollback, local COMMIT and already-clean checks.
Windows PowerShell 5.1; requires the installed SqlPackage DacFx and existing LocalDB.
#>
[CmdletBinding()]
param(
 [string]$Archive='D:\FM_DATA\DatabaseBackups\flyermonkey-azdb_20261110T151441471Z.bacpac',
 [string]$DatabaseName=('Mission009J_Disposable_'+[guid]::NewGuid().ToString('N').Substring(0,12)),
 [string]$SqlPackageDirectory='C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\SQLDB\DAC',
 [switch]$RehearseCleanup
)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$connection=$null;$package=$null;$stage='validation'
if($DatabaseName -cnotmatch '^Mission009J_Disposable_[a-f0-9]{12}$'){throw 'Disposable database name must be Mission009J_Disposable_<12 lowercase hex characters>'}
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Run with Windows PowerShell 5.1'}
# Import ONLY local SQL/hash helper definitions, never execute the export script body.
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'tools/Mission009I/Export-ProductionBacpac.ps1'),[ref]$tokens,[ref]$errors)
if($errors){throw 'Archive helper syntax invalid'}
foreach($function in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){
 if($function.Name -in @('Query','Open-Sql','Quote-SqlName','Get-Inventory','Assert-RequiredCounts')){Invoke-Expression $function.Extent.Text}
}
function Protected-Fingerprint($c,[bool]$ExpectedAfter){
 $sql=@'
SELECT (SELECT * FROM dbo.Offers ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Offers,
 (SELECT * FROM dbo.Products ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Products,
 (SELECT * FROM dbo.ProductCategories ORDER BY ProductID,CategoryID FOR JSON PATH,INCLUDE_NULL_VALUES) Links,
 (SELECT * FROM dbo.Categories ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Categories,
 (SELECT * FROM dbo.Retailers ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Retailers,
 (SELECT * FROM dbo.RetailerLocations ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Locations,
 (SELECT * FROM dbo.ProviderListings ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) Listings,
 (SELECT * FROM dbo.OfferPromotionTypes ORDER BY OfferID,PromotionTypeID FOR JSON PATH,INCLUDE_NULL_VALUES) Promotions
FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES
'@
 if($ExpectedAfter){
  $sql=$sql.Replace('FROM dbo.Offers ORDER BY ID','FROM dbo.Offers WHERE ID NOT IN(159,160,161,162,163,171,172,173,174,175) ORDER BY ID')
  $sql=$sql.Replace('FROM dbo.Products ORDER BY ID','FROM dbo.Products WHERE ID NOT IN(167,168,169,170,171,179,180,181,182,183) ORDER BY ID')
  $sql=$sql.Replace('FROM dbo.ProductCategories ORDER BY','FROM dbo.ProductCategories WHERE ProductID NOT IN(167,168,169,170,171,179,180,181,182,183) ORDER BY')
 }
 $ds=Query $c $sql;$value=($ds.Tables[0].Rows|ForEach-Object{[string]$_[0]}) -join ''
 $sha=[Security.Cryptography.SHA256]::Create()
 try{return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($value))).Replace('-','')}finally{$sha.Dispose()}
}
function Result-Rows($ds){
 $sets=@();foreach($table in $ds.Tables){$rows=@();foreach($r in $table.Rows){$row=[ordered]@{};foreach($col in $table.Columns){$value=$r[$col.ColumnName];if($value -is [DBNull]){$value=$null};if($value -is [DateTime]){$value=$value.ToString('o')};$row[$col.ColumnName]=$value};$rows += [pscustomobject]$row};$sets+=,[pscustomobject]@{Rows=$rows}}
 return ,$sets
}
try{
 $manifest=Get-Content -LiteralPath ($Archive+'.verification.json') -Raw|ConvertFrom-Json
 $archiveHash=(Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash
 if($manifest.Status -cne 'VERIFIED' -or $archiveHash -cne $manifest.SHA256 -or $archiveHash -cne '69308D14312E27069F864CEBF073FBFC5D7D29D3CCDBAC03C2E2BB58358F18FC'){throw 'Verified BACPAC identity/checksum mismatch'}
 if(@($manifest.ImportedArchive).Count -ne 11){throw 'Expected eleven application tables'}
 $builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder
 $builder['Data Source']='(localdb)\MSSQLLocalDB';$builder['Initial Catalog']='master'
 $builder['Integrated Security']=$true;$builder['Encrypt']=$false;$builder['Enlist']=$false;$builder['Connect Timeout']=30
 $connection=Open-Sql $builder.ConnectionString
 if([int](Query $connection "SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB'))").Tables[0].Rows[0][0] -ne 1){throw 'Target is not LocalDB'}
 $exists=Query $connection ("SELECT COUNT(*) FROM sys.databases WHERE name=N'"+$DatabaseName+"'")
 if([int]$exists.Tables[0].Rows[0][0] -ne 0){Write-Output 'REFUSED_EXISTING_DATABASE';throw 'Refusing to overwrite an existing database'}
 if(!('FlyerMonkeyLocalRestore.DacResolver' -as [type])){
  Add-Type -TypeDefinition @'
using System;using System.IO;using System.Reflection;
namespace FlyerMonkeyLocalRestore {
 public static class DacResolver {
  static string directory;
  public static void Install(string path){directory=Path.GetFullPath(path);AppDomain.CurrentDomain.AssemblyResolve+=Resolve;}
  static Assembly Resolve(object sender,ResolveEventArgs args){var p=Path.Combine(directory,new AssemblyName(args.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;}
 }
}
'@
 }
 [FlyerMonkeyLocalRestore.DacResolver]::Install($SqlPackageDirectory)
 Add-Type -Path (Join-Path $SqlPackageDirectory 'Microsoft.SqlServer.Dac.dll')
 $started=[DateTime]::UtcNow.ToString('o')
 $stage='local restore'
 Write-Output ('Restoring NEW local database: '+$DatabaseName)
 $package=[Microsoft.SqlServer.Dac.BacPackage]::Load($Archive)
 # Atomically reserve ownership. If another process created the name after our
 # existence check, CREATE DATABASE fails before DacFx can touch that database.
 # ImportBacpac accepts an existing empty database; only this invocation's newly
 # created empty database may be used. No existing database is dropped/replaced.
 $stage='atomic new local database reservation'
 [void](Query $connection ("CREATE DATABASE ["+$DatabaseName+"]"))
 $stage='local restore'
 $service=New-Object Microsoft.SqlServer.Dac.DacServices($builder.ConnectionString)
 $options=New-Object Microsoft.SqlServer.Dac.DacImportOptions
 $options.CommandTimeout=120;$options.LongRunningCommandTimeout=600
 $service.ImportBacpac($package,$DatabaseName,$options,$null)
 $package.Dispose();$package=$null;$connection.Dispose()
 $builder['Initial Catalog']=$DatabaseName;$connection=Open-Sql $builder.ConnectionString
 $identity=(Query $connection "SELECT DB_NAME() DatabaseName,CONVERT(int,SERVERPROPERTY('IsLocalDB')) IsLocalDB,CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) ServerName").Tables[0].Rows[0]
 if($identity.DatabaseName -cne $DatabaseName -or $identity.IsLocalDB -ne 1){throw 'Local database identity mismatch'}
 $stage='eleven-table restore validation'
 $baseline=Get-Inventory $connection;Assert-RequiredCounts $baseline
 if(($baseline|ConvertTo-Json -Depth 6 -Compress) -cne ($manifest.ImportedArchive|ConvertTo-Json -Depth 6 -Compress)){throw 'Restored table counts/data hashes differ from verified archive'}
 Write-Output 'RESTORE_VERIFIED: 11 tables; Offers=109 Products=112 ProductCategories=57'
 $cleanup=$null;$final=$baseline
 if($RehearseCleanup){
  $stage='local cleanup rehearsal'
  $cleanupPath=Join-Path $repo 'database/maintenance/009I-reconcile-duplicates.sql'
  $cleanupHash=(Get-FileHash $cleanupPath -Algorithm SHA256).Hash
  if($cleanupHash -cne '19D679F34FA3BDC8CE80ECE8276E88B76C7D70B7BC1454C445DCCE6907E0AA05'){throw 'Reviewed cleanup source changed'}
  $sql=[IO.File]::ReadAllText($cleanupPath).Replace('__CONFIRM_SERVER__',([string]$identity.ServerName).Replace("'","''")).Replace('__CONFIRM_DATABASE__',$DatabaseName)
  $expected=Protected-Fingerprint $connection $true
  $dry=Query $connection $sql
  if(($dry.Tables|Where-Object{$_.Columns.Contains('Outcome')}).Rows[0].Outcome -cne 'DRY_RUN_ROLLED_BACK'){throw 'Dry-run rollback not confirmed'}
  $afterDry=Get-Inventory $connection
  if(($baseline|ConvertTo-Json -Depth 6 -Compress) -cne ($afterDry|ConvertTo-Json -Depth 6 -Compress)){throw 'Dry run changed local rows'}
  $commitSql=$sql.Replace('DECLARE @Commit bit = 0;','DECLARE @Commit bit = 1;').Replace("DECLARE @Authorization nvarchar(100) = N'';","DECLARE @Authorization nvarchar(100) = N'MISSION 009I EXECUTION AUTHORIZED';")
  $committed=Query $connection $commitSql
  if(($committed.Tables|Where-Object{$_.Columns.Contains('Outcome')}).Rows[0].Outcome -cne 'COMMITTED'){throw 'Local commit outcome missing'}
  $final=Get-Inventory $connection
  foreach($pair in @(@('Offers',99),@('Products',102),@('ProductCategories',53))){
   if(($final|Where-Object Table -eq $pair[0]).Count -ne $pair[1]){throw 'Local post-cleanup count mismatch'}
  }
  if((Protected-Fingerprint $connection $false) -cne $expected){throw 'Retained/unrelated rows changed'}
  foreach($row in $baseline|Where-Object Table -NotIn @('Offers','Products','ProductCategories')){
   $actual=$final|Where-Object Table -eq $row.Table
   if($row.DataSHA256 -cne $actual.DataSHA256 -or $row.Count -ne $actual.Count){throw 'Unrelated table changed'}
  }
  $repeat=Query $connection $commitSql
  if(($repeat.Tables|Where-Object{$_.Columns.Contains('Outcome')}).Rows[0].Outcome -cne 'ALREADY_CLEAN_VALIDATED_NO_CHANGES'){throw 'Repeat execution failed'}
  if(($final|ConvertTo-Json -Depth 6 -Compress) -cne ((Get-Inventory $connection)|ConvertTo-Json -Depth 6 -Compress)){throw 'Repeat changed data'}
  $cleanup=[ordered]@{ScriptSHA256=$cleanupHash;DryRun='DRY_RUN_ROLLED_BACK';Commit='COMMITTED';Repeat='ALREADY_CLEAN_VALIDATED_NO_CHANGES';RetainedAndUnrelatedRowsUnchanged=$true;CommitResults=(Result-Rows $committed)}
  Write-Output 'LOCAL_CLEANUP_VERIFIED: Offers=99 Products=102 ProductCategories=53; retained/unrelated rows unchanged'
 }
 $report=[ordered]@{Status='PASS';StartedUtc=$started;FinishedUtc=[DateTime]::UtcNow.ToString('o');Archive=$Archive;SHA256=$archiveHash;Server='(localdb)\MSSQLLocalDB';Database=$DatabaseName;DatabaseRetained=$true;AtomicNewDatabaseReservation=$true;ApplicationTableCount=11;Baseline=$baseline;Final=$final;Cleanup=$cleanup;ProductionConnections=0;AzureOperations=0;ExcludedArchiveTooling='dbo.sysdiagrams remains in the original tooling companion; not imported'}
 $directory=Join-Path $repo '.artifacts/mission009j';[void][IO.Directory]::CreateDirectory($directory)
 $reportPath=Join-Path $directory ($DatabaseName+'.restore.json')
 $report|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $reportPath -Encoding UTF8
 Write-Output ('RESULT '+$reportPath)
}catch{
 $types=@();$ex=$_.Exception;while($ex){$types+=$ex.GetType().Name;$ex=$ex.InnerException}
 throw ('Local restore/rehearsal failed at '+$stage+'; '+($types -join ' -> ')+'. Database retained; sensitive exception text suppressed.')
}finally{if($package){$package.Dispose()};if($connection){$connection.Dispose()}}
