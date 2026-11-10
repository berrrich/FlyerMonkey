<#
Reusable full schema/data BACPAC export and local import verification.
Run with Windows PowerShell 5.1 and SqlPackage's .NET Framework DacFx installation.
Uses the DacFx engine bundled with SqlPackage in-process: credentials are never passed
on the command line or written to logs/response files. No production writes or cleanup.
Existing archives are never overwritten/deleted. Local verification databases are retained.
#>
[CmdletBinding()]
param(
 [string]$BackupDirectory='D:\FM_DATA\DatabaseBackups',
 [string]$SqlPackageDirectory='C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\SQLDB\DAC',
 [string]$LocalDbInstance='MSSQLLocalDB'
)
$ErrorActionPreference='Stop'
$stage='initialize'
$archive=$null;$stream=$null;$sourceConnection=$null;$localConnection=$null;$package=$null
function Query($connection,[string]$sql){
 $cmd=$connection.CreateCommand();$cmd.CommandTimeout=120;$cmd.CommandText=$sql
 $ds=New-Object System.Data.DataSet;$adapter=New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
 [void]$adapter.Fill($ds);return ,$ds
}
function Open-Sql([string]$connectionString){
 $connection=New-Object System.Data.SqlClient.SqlConnection($connectionString)
 try{$connection.Open();return $connection}catch{$connection.Dispose();throw}
}
function Quote-SqlName([string]$name){return '['+$name.Replace(']',']]')+']'}
function Get-Inventory($connection){
 $metadata=Query $connection @'
SELECT s.name SchemaName,t.name TableName,c.name ColumnName,ic.key_ordinal
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
LEFT JOIN sys.indexes i ON i.object_id=t.object_id AND i.is_primary_key=1
LEFT JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0
LEFT JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name,ic.key_ordinal
'@
 $inventory=@()
 foreach($group in ($metadata.Tables[0].Rows | Group-Object SchemaName,TableName)){
  $first=$group.Group[0];$schema=[string]$first.SchemaName;$table=[string]$first.TableName
  $keys=@($group.Group|Where-Object{$_.ColumnName -isnot [DBNull]}|ForEach-Object{Quote-SqlName ([string]$_.ColumnName)})
  if(!$keys.Count){throw 'A table lacks a primary key; deterministic verification requires review'}
  $qualified=(Quote-SqlName $schema)+'.'+(Quote-SqlName $table)
  $count=(Query $connection ("SELECT COUNT_BIG(*) FROM "+$qualified)).Tables[0].Rows[0][0]
  $json=Query $connection ("SELECT * FROM "+$qualified+" ORDER BY "+($keys -join ',')+" FOR JSON PATH,INCLUDE_NULL_VALUES")
  $value=($json.Tables[0].Rows|ForEach-Object{[string]$_[0]}) -join ''
  $sha=[Security.Cryptography.SHA256]::Create()
  try{$hash=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($value))).Replace('-','')}finally{$sha.Dispose()}
  $inventory += [pscustomobject]@{Schema=$schema;Table=$table;Count=[long]$count;DataSHA256=$hash}
 }
 return ,$inventory
}
function Assert-RequiredCounts($inventory){
 foreach($item in @(@('Offers',109),@('Products',112),@('ProductCategories',57))){
  $found=@($inventory|Where-Object{$_.Schema -ceq 'dbo' -and $_.Table -ceq $item[0]})
  if($found.Count -ne 1 -or $found[0].Count -ne $item[1]){throw ('Required count mismatch for '+$item[0])}
 }
}
try{
 if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Use Windows PowerShell 5.1 for the installed SqlPackage DacFx'}
 if($LocalDbInstance -notmatch '^[A-Za-z0-9_]+$'){throw 'Invalid local instance name'}
 $dac=Join-Path $SqlPackageDirectory 'Microsoft.SqlServer.Dac.dll'
 $sqlPackage=Join-Path $SqlPackageDirectory 'SqlPackage.exe'
 if(!(Test-Path -LiteralPath $dac) -or !(Test-Path -LiteralPath $sqlPackage)){throw 'SqlPackage and its DacFx library are required'}
 # DacFx resolves additional assemblies on worker threads. Use a CLR resolver rather
 # than a PowerShell callback (worker threads have no PowerShell runspace).
 if(!('FlyerMonkeyArchive.DacAssemblyResolver' -as [type])){
  Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
namespace FlyerMonkeyArchive {
 public static class DacAssemblyResolver {
  private static string directory;
  public static void Install(string path) {
   directory = Path.GetFullPath(path);
   AppDomain.CurrentDomain.AssemblyResolve += Resolve;
  }
  private static Assembly Resolve(object sender, ResolveEventArgs args) {
   string name = new AssemblyName(args.Name).Name;
   string path = Path.Combine(directory, name + ".dll");
   return File.Exists(path) ? Assembly.LoadFrom(path) : null;
  }
 }
}
'@
 }
 [FlyerMonkeyArchive.DacAssemblyResolver]::Install($SqlPackageDirectory)
 Add-Type -Path $dac
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $raw=[Environment]::GetEnvironmentVariable('FLYERMONKEY_SQL_CONNECTION')
 if(!$raw){$raw=[Environment]::GetEnvironmentVariable('FLYERMONKEY_SQL_CONNECTION','User')}
 if(!$raw){throw 'Existing secure configuration unavailable'}
 $b=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($raw)
 if($b['Initial Catalog'] -cne 'flyermonkey-azdb'){throw 'Source database identity mismatch'}
 $b['Connect Timeout']=60;$b.Enlist=$false;$b.ApplicationIntent='ReadOnly'
 $stage='source identity and pre-export inventory'
 $sourceConnection=Open-Sql $b.ConnectionString
 $id=(Query $sourceConnection @'
SELECT DB_NAME() DatabaseName,CONVERT(int,SERVERPROPERTY('EngineEdition')) EngineEdition,
 CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))+N'|'+DB_NAME()),2) IdentitySHA256
'@
 ).Tables[0].Rows[0]
 if($id.DatabaseName -cne 'flyermonkey-azdb' -or $id.EngineEdition -ne 5 -or $id.IdentitySHA256 -cne '9A655FB0EEF12EBD287E4946517AC06CEF04DB1F23DF6E900948609D3C378218'){throw 'Verified production identity mismatch'}
 $before=Get-Inventory $sourceConnection
 Assert-RequiredCounts $before
 # Verify the existing local engine before starting an export.
 $localBuilder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder
 $localBuilder['Data Source']='(localdb)\'+$LocalDbInstance
 $localBuilder['Initial Catalog']='master';$localBuilder['Integrated Security']=$true
 $localBuilder['Encrypt']=$false;$localBuilder['Enlist']=$false;$localBuilder['Connect Timeout']=30
 $localConnection=Open-Sql $localBuilder.ConnectionString
 if([int](Query $localConnection "SELECT CONVERT(int,SERVERPROPERTY('EngineEdition'))").Tables[0].Rows[0][0] -eq 5){throw 'Verification target must be local SQL Server'}
 [void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($BackupDirectory))
 $started=[DateTime]::UtcNow
 $stamp=$started.ToString('yyyyMMddTHHmmssfffZ')
 $archive=Join-Path ([IO.Path]::GetFullPath($BackupDirectory)) ("flyermonkey-azdb_"+$stamp+'.bacpac')
 # SqlPackage deliberately omits SSMS diagram-support objects. Preserve their
 # schema metadata and data as an explicit companion, never silently ignore them.
 $toolingPath=$null
 $diagram=@($before|Where-Object{$_.Schema -ceq 'dbo' -and $_.Table -ceq 'sysdiagrams'})
 if($diagram.Count){
  $diagramData=Query $sourceConnection 'SELECT * FROM dbo.sysdiagrams ORDER BY diagram_id FOR JSON PATH,INCLUDE_NULL_VALUES'
  $diagramJson=($diagramData.Tables[0].Rows|ForEach-Object{[string]$_[0]}) -join ''
  $diagramSchema=Query $sourceConnection @'
SELECT c.name ColumnName,ty.name TypeName,c.max_length,c.is_nullable,c.is_identity
FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE c.object_id=OBJECT_ID('dbo.sysdiagrams') ORDER BY c.column_id
'@
  $columns=@();foreach($r in $diagramSchema.Tables[0].Rows){$columns+=[pscustomobject]@{Name=[string]$r.ColumnName;Type=[string]$r.TypeName;MaxLength=[int]$r.max_length;Nullable=[bool]$r.is_nullable;Identity=[bool]$r.is_identity}}
  $diagramHash=[Security.Cryptography.SHA256]::Create()
  try{$diagramDataHash=[BitConverter]::ToString($diagramHash.ComputeHash([Text.Encoding]::UTF8.GetBytes($diagramJson))).Replace('-','')}finally{$diagramHash.Dispose()}
  if($diagramDataHash -cne $diagram[0].DataSHA256){throw 'Diagram metadata changed during companion capture'}
  $toolingPath=$archive+'.tooling.json'
  [ordered]@{Table='dbo.sysdiagrams';Reason='SSMS diagram-support table omitted by standard SqlPackage export';Columns=$columns;Rows=($diagramJson|ConvertFrom-Json);Count=$diagram[0].Count;DataSHA256=$diagramDataHash}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $toolingPath -Encoding UTF8
 }
 # CreateNew atomically prevents overwriting any existing archive.
 $stream=[IO.File]::Open($archive,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 $stage='SqlPackage DacFx full schema/data export'
 Write-Output ('Exporting production BACPAC: '+$archive)
 $options=New-Object Microsoft.SqlServer.Dac.DacExportOptions
 $options.VerifyExtraction=$true;$options.CommandTimeout=120;$options.LongRunningCommandTimeout=600
 $services=New-Object Microsoft.SqlServer.Dac.DacServices($b.ConnectionString)
 $services.ExportBacpac($stream,'flyermonkey-azdb',$options,$null,$null)
 $stream.Dispose();$stream=$null
 $stage='archive schema/data inspection'
 $zip=[IO.Compression.ZipFile]::OpenRead($archive)
 try{
  $model=$zip.GetEntry('model.xml')
  $data=@($zip.Entries|Where-Object{$_.FullName -match '^Data/' -and $_.Length -gt 0})
  if(!$model -or $model.Length -eq 0 -or !$data.Count){throw 'BACPAC lacks schema or nonempty table-data entries'}
  # Fully read every entry to verify decompression rather than filenames alone.
  foreach($entry in $zip.Entries){$entryStream=$entry.Open();try{$entryStream.CopyTo([IO.Stream]::Null)}finally{$entryStream.Dispose()}}
  $entries=@($zip.Entries|ForEach-Object{[pscustomobject]@{Path=$_.FullName;Bytes=$_.Length}})
 }finally{$zip.Dispose()}
 $stage='local BACPAC import verification'
 $verifyDb='Mission009I_ArchiveVerify_'+[guid]::NewGuid().ToString('N').Substring(0,12)
 $exists=Query $localConnection ("SELECT COUNT(*) FROM sys.databases WHERE name=N'"+$verifyDb+"'")
 if([int]$exists.Tables[0].Rows[0][0] -ne 0){throw 'Verification database already exists'}
 Write-Output ('Verifying archive by import into new local database: '+$verifyDb)
 $package=[Microsoft.SqlServer.Dac.BacPackage]::Load($archive)
 $localServices=New-Object Microsoft.SqlServer.Dac.DacServices($localBuilder.ConnectionString)
 $importOptions=New-Object Microsoft.SqlServer.Dac.DacImportOptions
 $importOptions.CommandTimeout=120;$importOptions.LongRunningCommandTimeout=600
 $localServices.ImportBacpac($package,$verifyDb,$importOptions,$null)
 $package.Dispose();$package=$null
 $localConnection.Dispose()
 $localBuilder['Initial Catalog']=$verifyDb
 $localConnection=Open-Sql $localBuilder.ConnectionString
 $imported=Get-Inventory $localConnection
 Assert-RequiredCounts $imported
 $stage='post-export source comparison and checksum'
 $after=Get-Inventory $sourceConnection
 Assert-RequiredCounts $after
 $beforeJson=$before|ConvertTo-Json -Depth 5 -Compress
 $afterJson=$after|ConvertTo-Json -Depth 5 -Compress
 $excluded=@()
 foreach($row in $before){
  $match=@($imported|Where-Object{$_.Schema -ceq $row.Schema -and $_.Table -ceq $row.Table})
  if(!$match.Count){
   if($row.Schema -cne 'dbo' -or $row.Table -cne 'sysdiagrams' -or !$toolingPath){throw 'Unexpected missing table in imported archive'}
   $excluded+='dbo.sysdiagrams'
  }
 }
 $exportedBefore=@($before|Where-Object{($_.Schema+'.'+$_.Table) -cnotin $excluded})
 $exportedBeforeJson=$exportedBefore|ConvertTo-Json -Depth 5 -Compress
 $importJson=$imported|ConvertTo-Json -Depth 5 -Compress
 if($beforeJson -cne $afterJson -or $exportedBeforeJson -cne $importJson){throw 'Source changed or imported archive differs; do not accept this archive as verified'}
 $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
 $checksumPath=$archive+'.sha256'
 [IO.File]::WriteAllText($checksumPath,($hash+'  '+[IO.Path]::GetFileName($archive)+[Environment]::NewLine),[Text.Encoding]::ASCII)
 $manifest=[ordered]@{Status='VERIFIED';Archive=$archive;Bytes=(Get-Item -LiteralPath $archive).Length;SHA256=$hash;ChecksumFile=$checksumPath;StartedUtc=$started.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o');Database='flyermonkey-azdb';ServerDatabaseIdentitySHA256=[string]$id.IdentitySHA256;SqlPackageVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($sqlPackage).FileVersion;Engine='SqlPackage bundled DacFx API (in-process credentials)';ExportCompleted=$true;SchemaPresent=$true;DataPresent=$true;ArchiveEntries=$entries;SourceBefore=$before;SourceAfter=$after;ImportedArchive=$imported;AllExportedTableDataHashesMatch=$true;SourceBeforeAfterMatches=$true;ExcludedToolingTables=$excluded;ToolingDataCompanion=$toolingPath;ToolingCompanionSHA256=$(if($toolingPath){(Get-FileHash -LiteralPath $toolingPath -Algorithm SHA256).Hash}else{$null});VerificationDatabase=$verifyDb;VerificationServer='(localdb)\'+$LocalDbInstance;VerificationDatabaseRetained=$true;ProductionWritesPerformed=$false;AzureRestoreDatabaseModified=$false;ConsistencyScope='All source tables stable before/after. Exported tables match local import; omitted SSMS diagrams are preserved separately. No claim of a single transactional export snapshot or physical/PITR backup.'}
 $manifest|ConvertTo-Json -Depth 10|Set-Content -LiteralPath ($archive+'.verification.json') -Encoding UTF8
 Write-Output 'EXPORT_AND_IMPORT_VERIFICATION_SUCCESS'
 Write-Output ('ARCHIVE '+$archive)
 Write-Output ('SHA256 '+$hash)
 Write-Output 'Offers=109 Products=112 ProductCategories=57'
}catch{
 # Never emit DacFx exception text: it can contain connection details or data.
 $exception=$_.Exception;$types=@()
 while($exception){
  $types+=$exception.GetType().Name
  if($exception -is [IO.FileNotFoundException] -and $exception.FileName -match '^[A-Za-z0-9_.]+, Version='){
   $types+=('Missing assembly: '+($exception.FileName -split ',')[0])
  }
  $exception=$exception.InnerException
 }
 if($archive){Write-Output ('Archive/partial artifact retained without deletion: '+$archive)}
 throw ('BACPAC operation failed at stage ['+$stage+']; exception types: '+($types -join ' -> ')+'. Sensitive details suppressed; no archive was deleted.')
}finally{
 if($package){$package.Dispose()}
 if($stream){$stream.Dispose()}
 if($localConnection){$localConnection.Dispose()}
 if($sourceConnection){$sourceConnection.Dispose()}
}
