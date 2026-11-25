# Offline SQL Server regression checks. Never reads application connection settings.
$ErrorActionPreference = 'Stop'
$database = 'Mission013SqlFixture_' + [Guid]::NewGuid().ToString('N')
$localConnection = "Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=master;Integrated Security=True;Encrypt=False;Connect Timeout=15"
$connection = New-Object System.Data.SqlClient.SqlConnection($localConnection)
$checks = 0
function Query([string] $sql) {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 30
    $command.CommandText = $sql
    try { $command.ExecuteScalar() } finally { $command.Dispose() }
}
function Check([bool] $ok, [string] $name) {
    if (!$ok) { throw "FAIL: $name" }
    $script:checks++
    Write-Output "PASS: $name"
}
function ExpectError([string] $sql, [int] $number) {
    try { Query $sql | Out-Null } catch {
        $errorObject = $_.Exception
        while ($errorObject -and $errorObject -isnot [System.Data.SqlClient.SqlException]) {
            $errorObject = $errorObject.InnerException
        }
        if ($errorObject -and $errorObject.Number -eq $number) { return }
        throw
    }
    throw "Expected SQL error $number"
}
$root = Split-Path $PSScriptRoot -Parent
$schema = Get-Content (Join-Path $root 'database/migrations/013B_001_UserRoles.review.sql') -Raw
$assignments = Get-Content (Join-Path $root 'database/migrations/013C_001_AccountAssignments.review.sql') -Raw
$customers = Get-Content (Join-Path $root 'database/migrations/011A_001_Customers.sql') -Raw
if ($schema -notmatch 'DECLARE @Apply bit = 0;' -or $assignments -notmatch 'DECLARE @Apply bit = 0;') {
    throw 'Both tracked migrations must keep @Apply = 0.'
}
# Only disposable in-memory copies are enabled, on a hardcoded LocalDB connection.
$enabledSchema = $schema.Replace('DECLARE @Apply bit = 0;', 'DECLARE @Apply bit = 1;')
$enabledAssignments = $assignments.Replace('DECLARE @Apply bit = 0;', 'DECLARE @Apply bit = 1;')
try {
    $connection.Open()
    Query "CREATE DATABASE [$database];" | Out-Null
    $connection.ChangeDatabase($database)
    Query $customers | Out-Null
    Query @'
INSERT dbo.Customers VALUES
('11111111-1111-1111-1111-111111111111','bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab','73bd7664-83c7-4a58-9005-ae3f8a80af3d','Active',1,'2026-01-01','2026-01-01'),
('22222222-2222-2222-2222-222222222222','bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab','5c65f998-22c5-4a23-97cc-ea1bbd7ba48e','Active',1,'2026-01-01','2026-01-01');
CREATE TABLE dbo.ArtworkFixture (ArtworkId int PRIMARY KEY, CustomerId uniqueidentifier REFERENCES dbo.Customers(CustomerId), Payload varbinary(8));
INSERT dbo.ArtworkFixture VALUES (1,'11111111-1111-1111-1111-111111111111',0x01020304);
'@ | Out-Null
    $before = Query 'SELECT * FROM dbo.Customers ORDER BY CustomerId FOR JSON PATH;'
    $artBefore = Query 'SELECT * FROM dbo.ArtworkFixture FOR JSON PATH;'
    ExpectError $schema 50000
    Check ((Query "SELECT COUNT(*) FROM sys.tables WHERE name IN ('Users','Roles','UserRoles');") -eq 0) 'Default guard creates no schema'
    # Force an error inside the nested batch, after column creation.
    $brokenSchema = $enabledSchema.Replace("EXEC sys.sp_executesql N'", "EXEC sys.sp_executesql N'SELECT MissingFixtureColumn FROM dbo.Customers;")
    ExpectError $brokenSchema 207
    Check ((Query "SELECT CASE WHEN COL_LENGTH('dbo.Customers','UserId') IS NULL AND OBJECT_ID('dbo.Users') IS NULL AND @@TRANCOUNT=0 THEN 1 ELSE 0 END;") -eq 1) 'Nested compilation error rolls back all DDL and closes transaction'
    Query $enabledSchema | Out-Null
    Check ((Query "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Customers') AND name='UX_Customers_UserId' AND is_unique=1 AND has_filter=1;") -eq 1) '013B compiles and creates filtered unique index'
    Check ((Query "SELECT COUNT(*) FROM sys.foreign_keys WHERE name='FK_Customers_Users' AND delete_referential_action=0 AND is_not_trusted=0;") -eq 1) '013B creates trusted non-cascading Customer link'
    ExpectError $enabledSchema 50001
    Check ((Query 'SELECT @@TRANCOUNT;') -eq 0) '013B repeat run refuses safely'
    $verification = Get-Content (Join-Path $root 'database/migrations/013C_002_Verification.readonly.sql') -Raw
    Query $verification | Out-Null
    ExpectError $assignments 50100
    Query $enabledAssignments | Out-Null
    Query $enabledAssignments | Out-Null
    Check ((Query 'SELECT COUNT(*) FROM dbo.Users;') -eq 3) '013C first and repeat runs produce three Users'
    Check ((Query 'SELECT COUNT(*) FROM dbo.UserRoles;') -eq 6) '013C first and repeat runs preserve six assignments'
    Check ((Query 'SELECT COUNT(*) FROM dbo.Customers WHERE UserId IS NOT NULL;') -eq 2) '013C links both existing Customers'
    Check ((Query "SELECT COUNT(*) FROM dbo.UserRoles ur JOIN dbo.Users u ON u.UserId=ur.UserId JOIN dbo.Roles r ON r.RoleId=ur.RoleId WHERE u.ExternalUserId='5c65f998-22c5-4a23-97cc-ea1bbd7ba48e' AND r.Name<>'Reader';") -eq 0) 'Reader receives no generation roles'
    Check ((Query "SELECT COUNT(*) FROM dbo.UserRoles ur JOIN dbo.Users u ON u.UserId=ur.UserId JOIN dbo.Roles r ON r.RoleId=ur.RoleId WHERE u.ExternalUserId='587c6244-6c78-4917-a953-b880a41a4abb' AND r.Name='Administrator';") -eq 1) 'Operator account receives Administrator'
    Check ((Query "SELECT COUNT(*) FROM dbo.Roles WHERE Name='Operator';") -eq 0) '013C adds no Operator role'
    Query "INSERT dbo.UserRoles SELECT u.UserId,r.RoleId FROM dbo.Users u CROSS JOIN dbo.Roles r WHERE u.ExternalUserId='5c65f998-22c5-4a23-97cc-ea1bbd7ba48e' AND r.Name='Customer';" | Out-Null
    Query $enabledAssignments | Out-Null
    Check ((Query 'SELECT COUNT(*) FROM dbo.UserRoles;') -eq 6) 'Repeat application removes excess Reader role'
    Query "UPDATE dbo.Users SET Status='Disabled' WHERE ExternalUserId='587c6244-6c78-4917-a953-b880a41a4abb';" | Out-Null
    ExpectError $enabledAssignments 50103
    Check ((Query 'SELECT @@TRANCOUNT;') -eq 0) 'Disabled account refusal closes transaction'
    Query "UPDATE dbo.Users SET Status='Active' WHERE ExternalUserId='587c6244-6c78-4917-a953-b880a41a4abb';" | Out-Null
    $after = Query 'SELECT CustomerId,ExternalTenantId,ExternalUserId,Status,CraterEnabled,CreatedUtc,UpdatedUtc FROM dbo.Customers ORDER BY CustomerId FOR JSON PATH;'
    Check ($before -eq $after) 'Original Customer values are unchanged'
    Check ($artBefore -eq (Query 'SELECT * FROM dbo.ArtworkFixture FOR JSON PATH;')) 'Artwork ownership and bytes are unchanged'
    Query ($verification.Replace('DECLARE @PostMigration bit = 0;', 'DECLARE @PostMigration bit = 1;')) | Out-Null
    Check ($true) 'SSMS baseline and post-migration verification execute successfully'
    Write-Output "$checks offline SQL migration checks passed. LocalDB only."
} finally {
    if ($connection.State -eq 'Open') {
        $connection.ChangeDatabase('master')
        # Database name is generated above and never comes from configuration or input.
        if ($database -notmatch '^Mission013SqlFixture_[0-9a-f]{32}$') { throw 'Unsafe fixture name' }
        Query "IF DB_ID('$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END;" | Out-Null
    }
    $connection.Dispose()
}
