# Creates only uniquely named local disposable databases. Never reads production configuration.
# Fixture databases are retained for inspection; no DROP DATABASE or backup deletion.
param([string]$ReportPath = '.artifacts/mission009i-dev-results.json')
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$script = [IO.File]::ReadAllText((Join-Path $repo 'database/maintenance/009I-reconcile-duplicates.sql'))
$expectedStart = $script.IndexOf('DECLARE @Expected TABLE')
$expectedEnd = $script.IndexOf('DECLARE @DeletedOffers TABLE')
$seedExpected = $script.Substring($expectedStart, $expectedEnd-$expectedStart)
$server = '(localdb)\MSSQLLocalDB'
function Open-Local([string]$database) {
    if ($database -ne 'master' -and $database -notmatch '^Mission009I_Disposable_[a-f0-9]{12}$') { throw 'Invalid DEV database name' }
    $c = New-Object System.Data.SqlClient.SqlConnection("Server=$server;Database=$database;Integrated Security=True;Encrypt=False;Enlist=False;Connect Timeout=30")
    $c.Open()
    $cmd=$c.CreateCommand(); $cmd.CommandText="SELECT CONVERT(int,SERVERPROPERTY('EngineEdition'))";
    if ([int]$cmd.ExecuteScalar() -eq 5) { $c.Dispose(); throw 'Azure endpoint forbidden in DEV harness' }
    return $c
}
function Query($c,[string]$sql) {
    $cmd=$c.CreateCommand(); $cmd.CommandTimeout=60; $cmd.CommandText=$sql
    $ds=New-Object System.Data.DataSet; $adapter=New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
    [void]$adapter.Fill($ds)
    return ,$ds
}
$schema = @'
CREATE TABLE dbo.Retailers(ID int PRIMARY KEY,Name nvarchar(150));
CREATE TABLE dbo.RetailerLocations(ID int PRIMARY KEY,RetailerID int REFERENCES dbo.Retailers(ID),StoreName nvarchar(150),Suburb nvarchar(100));
CREATE TABLE dbo.Products(ID int PRIMARY KEY,Brand nvarchar(100),Name nvarchar(250) NOT NULL,Variant nvarchar(150),PackSizeText nvarchar(100),Barcode nvarchar(50),Category nvarchar(100),ImageBlobPath nvarchar(500),CreatedUtc datetime2 NOT NULL,UpdatedUtc datetime2);
CREATE TABLE dbo.Categories(ID int PRIMARY KEY,Name nvarchar(100),IsActive bit,SortOrder int);
CREATE TABLE dbo.ProductCategories(ProductID int REFERENCES dbo.Products(ID),CategoryID int REFERENCES dbo.Categories(ID),IsPrimary bit NOT NULL,PRIMARY KEY(ProductID,CategoryID));
CREATE TABLE dbo.Offers(ID bigint PRIMARY KEY,ProductID int NOT NULL REFERENCES dbo.Products(ID),RetailerID int REFERENCES dbo.Retailers(ID),RetailerLocationID int REFERENCES dbo.RetailerLocations(ID),AdvertisedPrice decimal(10,2) NOT NULL CHECK(AdvertisedPrice>0),RegularPrice decimal(10,2),AdvertisedSaving decimal(10,2),ValidFrom date NOT NULL,ValidTo date NOT NULL,SourceType nvarchar(50),SourceDescription nvarchar(250),FlyerPageNumber int,FlyerImageBlobPath nvarchar(500),CreatedUtc datetime2 NOT NULL,PromoText nvarchar(500),UnitPriceText nvarchar(100),MultiBuyQuantity int,MultiBuyPrice decimal(10,2),MemberPrice decimal(10,2),MemberOnlyFlag bit,SourceProductText nvarchar(1000),PromotionTypes nvarchar(max),CHECK(ValidTo>=ValidFrom));
CREATE TABLE dbo.DeliveryProviders(ID int PRIMARY KEY,Name nvarchar(100));
CREATE TABLE dbo.ProviderListings(ID int PRIMARY KEY,ProductID int REFERENCES dbo.Products(ID),RetailerID int,RetailerLocationID int,DeliveryProviderID int REFERENCES dbo.DeliveryProviders(ID));
CREATE TABLE dbo.PromotionTypes(ID int PRIMARY KEY,Name nvarchar(100));
CREATE TABLE dbo.OfferPromotionTypes(OfferID bigint REFERENCES dbo.Offers(ID),PromotionTypeID int REFERENCES dbo.PromotionTypes(ID),PRIMARY KEY(OfferID,PromotionTypeID));
INSERT dbo.Retailers VALUES(1,N'Woolworths'),(2,N'Coles');
INSERT dbo.RetailerLocations VALUES(3,1,N'Woolworths Butler',N'Butler'),(2,2,N'Fixture Coles',N'Butler');
INSERT dbo.DeliveryProviders VALUES(1,N'Fixture');
INSERT dbo.PromotionTypes VALUES(1,N'Half price');
;WITH n AS(SELECT TOP(19) ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) n FROM sys.all_objects)
INSERT dbo.Categories SELECT n,CASE n WHEN 2 THEN N'Meat & Poultry' WHEN 4 THEN N'Deli' ELSE CONCAT(N'Fixture ',n) END,1,n FROM n;
'@
$seed = @'
INSERT dbo.Products SELECT ProductID,Brand,Name,Variant,Pack,N'',Category,ImagePath,DATEADD(second,CONVERT(int,OfferID),'20261104'),NULL FROM @Expected;
INSERT dbo.Offers SELECT OfferID,ProductID,1,3,Price,Regular,Saving,'20261104','20261111',N'Flyer',N'page-002.pdf',2,NULL,DATEADD(second,CONVERT(int,OfferID),'20261104'),Promo,UnitText,NULL,NULL,NULL,NULL,NULL,PromotionTypes FROM @Expected;
INSERT dbo.ProductCategories SELECT ProductID,c.ID,c.IsPrimary FROM @Expected CROSS JOIN(VALUES(2,CONVERT(bit,0)),(4,CONVERT(bit,1))) c(ID,IsPrimary) WHERE KeepProductID=162;
;WITH n AS(SELECT TOP(97) ROW_NUMBER() OVER(ORDER BY (SELECT NULL))-1 n FROM sys.all_objects)
INSERT dbo.Products SELECT 1000+n,N'Fixture',CONCAT(N'Unrelated ',n),N'',N'ea',N'',N'Fixture',CONCAT(N'fixture-images/',n%75,N'.jpg'),'20261101',NULL FROM n;
;WITH n AS(SELECT TOP(94) ROW_NUMBER() OVER(ORDER BY (SELECT NULL))-1 n FROM sys.all_objects)
INSERT dbo.Offers SELECT 1000+n,1000+n,2,2,9.99,NULL,NULL,CASE WHEN n<33 THEN '20261104' ELSE '20261001' END,CASE WHEN n<33 THEN '20261111' ELSE '20261008' END,N'Flyer',N'fixture.pdf',1,NULL,'20261101',N'',N'',NULL,NULL,NULL,NULL,NULL,N'[]' FROM n;
;WITH n AS(SELECT TOP(51) ROW_NUMBER() OVER(ORDER BY (SELECT NULL))-1 n FROM sys.all_objects)
INSERT dbo.ProductCategories SELECT 1000+n,1,1 FROM n;
'@
function Fingerprint($c) {
    # Full deterministic row serialization of every fixture table, hashed in memory.
    $sql="SELECT (SELECT * FROM dbo.Products ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) P,(SELECT * FROM dbo.Offers ORDER BY ID FOR JSON PATH,INCLUDE_NULL_VALUES) O,(SELECT * FROM dbo.ProductCategories ORDER BY ProductID,CategoryID FOR JSON PATH) PC,(SELECT * FROM dbo.Retailers ORDER BY ID FOR JSON PATH) R,(SELECT * FROM dbo.RetailerLocations ORDER BY ID FOR JSON PATH) RL,(SELECT * FROM dbo.Categories ORDER BY ID FOR JSON PATH) C,(SELECT * FROM dbo.ProviderListings ORDER BY ID FOR JSON PATH) L,(SELECT * FROM dbo.OfferPromotionTypes ORDER BY OfferID,PromotionTypeID FOR JSON PATH) OP,(SELECT * FROM dbo.PromotionTypes ORDER BY ID FOR JSON PATH) PT,(SELECT * FROM dbo.DeliveryProviders ORDER BY ID FOR JSON PATH) DP FOR JSON PATH,WITHOUT_ARRAY_WRAPPER"
    $ds=Query $c $sql; $value=($ds.Tables[0].Rows | ForEach-Object {[string]$_[0]}) -join ''
    $hash=[Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($value))) } finally {$hash.Dispose()}
}
function Run-Cleanup($c,[bool]$commit,[bool]$fail,[string]$serverOverride='') {
    $id=Query $c "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME()"
    $actualServer=[string]$id.Tables[0].Rows[0][0]; $actualDatabase=[string]$id.Tables[0].Rows[0][1]
    if($serverOverride){$actualServer=$serverOverride}
    $run=$script.Replace('__CONFIRM_SERVER__',$actualServer.Replace("'","''")).Replace('__CONFIRM_DATABASE__',$actualDatabase)
    if($commit){$run=$run.Replace('DECLARE @Commit bit = 0;','DECLARE @Commit bit = 1;').Replace("DECLARE @Authorization nvarchar(100) = N'';","DECLARE @Authorization nvarchar(100) = N'MISSION 009I EXECUTION AUTHORIZED';")}
    if($fail){$run=$run.Replace('DECLARE @DevForceFailure bit = 0;','DECLARE @DevForceFailure bit = 1;')}
    $ds=Query $c $run
    $outcomes=@();foreach($table in $ds.Tables){if($table.Columns.Contains('Outcome')){$outcomes += [string]$table.Rows[0]['Outcome']}}
    return ($outcomes -join ',')
}
$cases=@(
 @{Name='dry-run';Mutation='';Expected=0;Mode='dry'},
 @{Name='commit-and-repeat';Mutation='';Expected=0;Mode='commit'},
 @{Name='failure-after-all-deletes';Mutation='';Expected=50921;Mode='failure'},
 @{Name='wrong-server';Mutation='';Expected=50901;Mode='identity'},
 @{Name='changed-price';Mutation='UPDATE dbo.Offers SET AdvertisedPrice=8.88 WHERE ID=160';Expected=50914;Mode='dry'},
 @{Name='changed-product-name';Mutation="UPDATE dbo.Products SET Name=N'Changed identity' WHERE ID=168";Expected=50914;Mode='dry'},
 @{Name='case-only-identity-drift';Mutation="UPDATE dbo.Products SET Name=N'chicken Garlic Kyly' WHERE ID=168";Expected=50914;Mode='dry'},
 @{Name='changed-validity';Mutation="UPDATE dbo.Offers SET ValidTo='20261112' WHERE ID=160";Expected=50914;Mode='dry'},
 @{Name='changed-pair';Mutation='UPDATE dbo.Offers SET ProductID=1000 WHERE ID=160';Expected=50913;Mode='dry'},
 @{Name='changed-image';Mutation="UPDATE dbo.Products SET ImageBlobPath=N'changed.jpg' WHERE ID=168";Expected=50912;Mode='dry'},
 @{Name='changed-category-primary';Mutation='UPDATE dbo.ProductCategories SET IsPrimary=0 WHERE ProductID=168 AND CategoryID=4';Expected=50917;Mode='dry'},
 @{Name='unexpected-listing';Mutation='INSERT dbo.ProviderListings VALUES(1,168,1,3,1)';Expected=50912;Mode='dry'},
 @{Name='unexpected-promotion-link';Mutation='INSERT dbo.OfferPromotionTypes VALUES(160,1)';Expected=50912;Mode='dry'},
 @{Name='unexpected-fk';Mutation='CREATE TABLE dbo.ExtraDependency(ID int PRIMARY KEY,ProductID int REFERENCES dbo.Products(ID)); INSERT dbo.ExtraDependency VALUES(1,168)';Expected=50907;Mode='dry'},
 @{Name='unexpected-trigger';Mutation="EXEC(N'CREATE TRIGGER dbo.ExtraTrigger ON dbo.Offers AFTER DELETE AS RETURN')";Expected=50906;Mode='dry'},
 @{Name='schema-drift';Mutation='ALTER TABLE dbo.Products ADD UnexpectedColumn int NULL';Expected=50905;Mode='dry'},
 @{Name='additional-offer-reference';Mutation='UPDATE dbo.Offers SET ProductID=168 WHERE ID=1000';Expected=50915;Mode='dry'},
 @{Name='missing-excess-offer';Mutation='DELETE dbo.Offers WHERE ID=160';Expected=50911;Mode='dry'},
 @{Name='later-retained-record';Mutation="UPDATE dbo.Products SET CreatedUtc='20261105' WHERE ID=162";Expected=50916;Mode='dry'},
 @{Name='unrelated-count-drift';Mutation='DELETE dbo.Offers WHERE ID=1000';Expected=50912;Mode='dry'}
)
$results=@()
foreach($case in $cases){
    $database='Mission009I_Disposable_'+[guid]::NewGuid().ToString('N').Substring(0,12)
    $master=Open-Local 'master';try{[void](Query $master "CREATE DATABASE [$database]")}finally{$master.Dispose()}
    $c=Open-Local $database
    try{
        [void](Query $c ($schema+$seedExpected+$seed))
        if($case.Mutation){[void](Query $c $case.Mutation)}
        $before=Fingerprint $c; $errorNumber=0; $outcome='';$repeat=''
        try{$outcome=Run-Cleanup $c ($case.Mode -eq 'commit') ($case.Mode -eq 'failure') $(if($case.Mode -eq 'identity'){'wrong-server'}else{''})}
        catch{ $ex=$_.Exception;while($ex -and $ex -isnot [System.Data.SqlClient.SqlException]){$ex=$ex.InnerException};if(!$ex){throw};$errorNumber=$ex.Number }
        if($errorNumber -ne $case.Expected){throw "$($case.Name): expected SQL $($case.Expected), got $errorNumber"}
        $after=Fingerprint $c
        if($case.Mode -ne 'commit' -and $before -ne $after){throw "$($case.Name): unexpected persistent row changes"}
        if($case.Mode -eq 'dry' -and $case.Expected -eq 0 -and $outcome -ne 'DRY_RUN_ROLLED_BACK'){throw 'Dry-run outcome missing'}
        if($case.Mode -eq 'commit'){
            if($outcome -ne 'COMMITTED' -or $before -eq $after){throw 'DEV commit did not persist'}
            $counts=Query $c 'SELECT (SELECT COUNT(*) FROM dbo.Offers) O,(SELECT COUNT(*) FROM dbo.Products) P,(SELECT COUNT(*) FROM dbo.ProductCategories) PC,(SELECT COUNT(*) FROM dbo.Offers WHERE ValidFrom<=''20261110'' AND ValidTo>=''20261110'') CurrentOffers'
            $row=$counts.Tables[0].Rows[0];if($row.O -ne 99 -or $row.P -ne 102 -or $row.PC -ne 53 -or $row.CurrentOffers -ne 38){throw 'Independent DEV counts failed'}
            $repeat=Run-Cleanup $c $true $false
            if($repeat -ne 'ALREADY_CLEAN_VALIDATED_NO_CHANGES' -or (Fingerprint $c) -ne $after){throw 'Repeat execution changed data'}
        }
        $tr=Query $c 'SELECT @@TRANCOUNT'; if([int]$tr.Tables[0].Rows[0][0] -ne 0){throw 'Transaction leaked'}
        $results += [pscustomobject]@{Case=$case.Name;Passed=$true;ExpectedSqlError=$case.Expected;ObservedSqlError=$errorNumber;Outcome=$outcome;Repeat=$repeat;Database=$database;RowsUnchanged=($before -eq $after)}
        Write-Output "PASS $($case.Name)"
    }finally{$c.Dispose()}
}
$path=Join-Path $repo $ReportPath
[void][IO.Directory]::CreateDirectory((Split-Path $path -Parent))
$report=[pscustomobject]@{CapturedUtc=[DateTime]::UtcNow.ToString('o');ScriptSHA256=(Get-FileHash (Join-Path $repo 'database/maintenance/009I-reconcile-duplicates.sql') -Algorithm SHA256).Hash;Server=$server;AzureResourcesCreated=$false;ProductionCleanupExecuted=$false;Cases=$results}
$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $path -Encoding UTF8
Write-Output "All $($results.Count) DEV cases passed. Fixture databases retained. Report: $ReportPath"
