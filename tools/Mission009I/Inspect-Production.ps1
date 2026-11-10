# SELECT-only. Does not load or execute the cleanup script, even in dry-run mode.
param([string]$ReportPath='.artifacts/mission009i-production-readonly.json')
$ErrorActionPreference='Stop'
$raw=[Environment]::GetEnvironmentVariable('FLYERMONKEY_SQL_CONNECTION')
if(!$raw){$raw=[Environment]::GetEnvironmentVariable('FLYERMONKEY_SQL_CONNECTION','User')}
if(!$raw){throw 'Secure SQL configuration unavailable'}
$b=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($raw)
$b.ApplicationIntent='ReadOnly';$b['Connect Timeout']=30;$b.Enlist=$false
$c=New-Object System.Data.SqlClient.SqlConnection($b.ConnectionString)
try {
 $c.Open();$cmd=$c.CreateCommand();$cmd.CommandTimeout=45
 $cmd.CommandText=@'
SELECT CONVERT(varchar(30),SYSUTCDATETIME(),126) CapturedUtc,CONVERT(int,SERVERPROPERTY('EngineEdition')) EngineEdition,
 CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))+N'|'+DB_NAME()),2) ServerDatabaseIdentitySHA256,
 HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION') CanViewDefinition;
SELECT edition,service_objective,elastic_pool_name FROM sys.database_service_objectives WHERE database_id=DB_ID();
SELECT SUM(size)*8.0/1024 AllocatedMB,SUM(FILEPROPERTY(name,'SpaceUsed'))*8.0/1024 UsedMB FROM sys.database_files;
SELECT backup_type,in_retention,COUNT(*) BackupCount,CONVERT(varchar(30),MIN(backup_finish_date),126) OldestFinishUtc,CONVERT(varchar(30),MAX(backup_finish_date),126) LatestFinishUtc FROM sys.dm_database_backups GROUP BY backup_type,in_retention;
SELECT (SELECT COUNT(*) FROM dbo.Offers) Offers,(SELECT COUNT(*) FROM dbo.Products) Products,
 (SELECT COUNT(*) FROM dbo.Offers WHERE ValidFrom<='20261110' AND ValidTo>='20261110') CurrentAtAuditDate,
 (SELECT COUNT(*) FROM dbo.Offers WHERE ValidFrom<=CONVERT(date,SYSDATETIMEOFFSET() AT TIME ZONE 'W. Australia Standard Time') AND ValidTo>=CONVERT(date,SYSDATETIMEOFFSET() AT TIME ZONE 'W. Australia Standard Time')) CurrentPerthDate,
 (SELECT COUNT(*) FROM dbo.ProductCategories) ProductCategoryLinks,(SELECT COUNT(*) FROM dbo.Categories) Categories,
 (SELECT COUNT(*) FROM dbo.RetailerLocations) Locations,(SELECT COUNT(*) FROM dbo.ProviderListings) ProviderListings,
 (SELECT COUNT(*) FROM dbo.OfferPromotionTypes) PromotionLinks,(SELECT COUNT(DISTINCT ImageBlobPath) FROM dbo.Products) DistinctImagePaths;
SELECT o.*,p.Name,p.Brand,p.Variant,p.PackSizeText,p.Barcode,p.Category,p.ImageBlobPath,p.CreatedUtc ProductCreatedUtc,p.UpdatedUtc ProductUpdatedUtc
 FROM dbo.Offers o JOIN dbo.Products p ON p.ID=o.ProductID WHERE o.ID IN(153,154,155,156,157,159,160,161,162,163,171,172,173,174,175) ORDER BY o.ID;
SELECT p.ID ProductID,(SELECT COUNT(*) FROM dbo.Offers o WHERE o.ProductID=p.ID) OfferReferences,
 (SELECT COUNT(*) FROM dbo.ProviderListings l WHERE l.ProductID=p.ID) ProviderReferences,
 (SELECT COUNT(*) FROM dbo.ProductCategories pc WHERE pc.ProductID=p.ID) CategoryLinks
 FROM dbo.Products p WHERE p.ID IN(161,162,163,164,165,167,168,169,170,171,179,180,181,182,183);
SELECT * FROM dbo.ProductCategories WHERE ProductID IN(161,162,163,164,165,167,168,169,170,171,179,180,181,182,183) ORDER BY ProductID,CategoryID;
SELECT fk.name,OBJECT_SCHEMA_NAME(f.parent_object_id) ChildSchema,OBJECT_NAME(f.parent_object_id) ChildTable,COL_NAME(f.parent_object_id,f.parent_column_id) ChildColumn,
 OBJECT_NAME(f.referenced_object_id) ParentTable,COL_NAME(f.referenced_object_id,f.referenced_column_id) ParentColumn,fk.is_disabled,fk.is_not_trusted,fk.delete_referential_action_desc
 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns f ON f.constraint_object_id=fk.object_id ORDER BY fk.name;
SELECT t.name TableName,c.name ColumnName,ty.name TypeName,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity
 FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
 WHERE t.name IN('Products','Offers','ProductCategories') ORDER BY t.name,c.column_id;
SELECT name,type_desc,OBJECT_NAME(parent_object_id) ParentObject FROM sys.objects WHERE is_ms_shipped=0 AND type IN('V','TR','SN');
'@
 $ds=New-Object System.Data.DataSet;$a=New-Object System.Data.SqlClient.SqlDataAdapter($cmd);[void]$a.Fill($ds)
 $names=@('Capture','ServiceTier','Storage','BackupMetadata','Counts','CandidateOffers','ProductDependencies','CategoryMappings','ForeignKeys','TargetColumns','ViewsTriggersSynonyms')
 $report=[ordered]@{}
 for($i=0;$i -lt $ds.Tables.Count;$i++){
  $rows=@();foreach($r in $ds.Tables[$i].Rows){$item=[ordered]@{};foreach($col in $ds.Tables[$i].Columns){$v=$r[$col.ColumnName];if($v -is [DBNull]){$v=$null};if($v -is [DateTime]){$v=$v.ToString('o')};$item[$col.ColumnName]=$v};$rows += [pscustomobject]$item}
  $report[$names[$i]]=$rows
 }
 $repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent;$path=Join-Path $repo $ReportPath
 [void][IO.Directory]::CreateDirectory((Split-Path $path -Parent))
 $report|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $path -Encoding UTF8
 'SELECT-only inspection saved: '+$ReportPath
 $report['Counts']|ConvertTo-Json -Compress
} catch {
 $ex=$_.Exception;while($ex -and $ex -isnot [System.Data.SqlClient.SqlException]){$ex=$ex.InnerException}
 if($ex){throw ('SQL read-only inspection failed, SQL number '+$ex.Number+'; connection details suppressed.')}
 throw 'Read-only inspection failed; details suppressed to protect secure configuration.'
} finally {$c.Dispose()}
