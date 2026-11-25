-- SSMS verification only: no persistent writes. Run in the explicitly selected database.
-- 013B is already deployed: DO NOT rerun 013B.
-- First run with @PostMigration=0 and keep this SSMS connection open.
-- After a separately authorized 013C application, rerun with @PostMigration=1.
-- No Azure execution or migration application is authorized by this file.
DECLARE @PostMigration bit = 0;
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName, SYSUTCDATETIME() AS ObservedUtc;
IF @PostMigration=0
BEGIN
    IF OBJECT_ID('tempdb..#Mission013C_Customers') IS NOT NULL
        THROW 50110, 'Baseline already exists; preserve it and use post-migration mode.', 1;
    SELECT CustomerId,ExternalTenantId,ExternalUserId,Status,CraterEnabled,CreatedUtc,UpdatedUtc
    INTO #Mission013C_Customers FROM dbo.Customers;
    SELECT * FROM #Mission013C_Customers ORDER BY CustomerId;
    SELECT N'Baseline captured. Save results and retain this connection.' AS Result;
END
ELSE
BEGIN
    IF OBJECT_ID('tempdb..#Mission013C_Customers') IS NULL
        THROW 50111, 'Missing pre-migration baseline in this connection.', 1;
    IF EXISTS (SELECT CustomerId,ExternalTenantId,ExternalUserId,Status,CraterEnabled,CreatedUtc,UpdatedUtc FROM dbo.Customers
        EXCEPT SELECT * FROM #Mission013C_Customers)
        OR EXISTS (SELECT * FROM #Mission013C_Customers EXCEPT
        SELECT CustomerId,ExternalTenantId,ExternalUserId,Status,CraterEnabled,CreatedUtc,UpdatedUtc FROM dbo.Customers)
        THROW 50112, 'Customer values changed; investigate before continuing.', 1;
    DECLARE @Expected TABLE (TenantId uniqueidentifier,ObjectId uniqueidentifier,RoleName nvarchar(64),NeedsCustomer bit);
    INSERT @Expected VALUES
    ('bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab','73bd7664-83c7-4a58-9005-ae3f8a80af3d',N'Customer',1),
    ('bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab','73bd7664-83c7-4a58-9005-ae3f8a80af3d',N'Crater Tester',1),
    ('bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab','73bd7664-83c7-4a58-9005-ae3f8a80af3d',N'Developer',1),
    ('bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab','73bd7664-83c7-4a58-9005-ae3f8a80af3d',N'Administrator',1),
    ('bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab','5c65f998-22c5-4a23-97cc-ea1bbd7ba48e',N'Reader',1),
    ('4112a2a6-70d8-47f0-a0fe-352edd57cc04','587c6244-6c78-4917-a953-b880a41a4abb',N'Administrator',0);
    IF EXISTS (SELECT TenantId,ObjectId,RoleName FROM @Expected EXCEPT
        SELECT u.ExternalTenantId,u.ExternalUserId,r.Name FROM dbo.Users u
        JOIN dbo.UserRoles ur ON ur.UserId=u.UserId JOIN dbo.Roles r ON r.RoleId=ur.RoleId)
        OR EXISTS (SELECT u.ExternalTenantId,u.ExternalUserId,r.Name FROM dbo.Users u
        JOIN dbo.UserRoles ur ON ur.UserId=u.UserId JOIN dbo.Roles r ON r.RoleId=ur.RoleId
        WHERE EXISTS (SELECT 1 FROM @Expected e WHERE e.TenantId=u.ExternalTenantId AND e.ObjectId=u.ExternalUserId)
        EXCEPT SELECT TenantId,ObjectId,RoleName FROM @Expected)
        THROW 50113, 'Role sets differ from the six intended assignments.', 1;
    IF EXISTS (SELECT 1 FROM @Expected e WHERE NOT EXISTS
        (SELECT 1 FROM dbo.Users u WHERE u.ExternalTenantId=e.TenantId AND u.ExternalUserId=e.ObjectId AND u.Status=N'Active'
        AND (e.NeedsCustomer=0 OR EXISTS (SELECT 1 FROM dbo.Customers c WHERE c.UserId=u.UserId
            AND c.ExternalTenantId=e.TenantId AND c.ExternalUserId=e.ObjectId))))
        THROW 50114, 'Active User or exact Customer link is missing.', 1;
    SELECT u.Email,u.UserId,u.ExternalTenantId,u.ExternalUserId,u.Status,r.Name AS RoleName,c.CustomerId
    FROM dbo.Users u JOIN dbo.UserRoles ur ON ur.UserId=u.UserId JOIN dbo.Roles r ON r.RoleId=ur.RoleId
    LEFT JOIN dbo.Customers c ON c.UserId=u.UserId
    WHERE EXISTS (SELECT 1 FROM @Expected e WHERE e.TenantId=u.ExternalTenantId AND e.ObjectId=u.ExternalUserId)
    ORDER BY u.Email,r.Name;
    SELECT N'PASS: exact role sets, Active Users, Customer links and baseline Customer values.' AS Result;
END;
-- SQL cannot prove file/blob preview readability. CustomerCraterArt remains keyed by
-- unchanged CustomerId/product. Offline endpoint tests verify Reader POST=403 with
-- no reservation/provider call and existing owner image GET=200 under existing gates.
-- Live API behavior remains unverified until a separately authorized deployment/test.
