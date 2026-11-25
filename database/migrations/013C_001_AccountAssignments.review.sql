-- REVIEW ONLY. Apply 013B_001 first, in a separately approved SQL change window.
-- Revalidate these tenant/object identities before enabling this script.
DECLARE @Apply bit = 0;
IF @Apply <> 1
    THROW 50100, 'Review-only assignments. Azure SQL writes are not authorized.', 1;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @Accounts TABLE (TenantId uniqueidentifier, ObjectId uniqueidentifier,
        Email nvarchar(320), IsCustomer bit);
    INSERT @Accounts VALUES
    ('bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab','73bd7664-83c7-4a58-9005-ae3f8a80af3d',N'richard.berriman@gmail.com',1),
    ('bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab','5c65f998-22c5-4a23-97cc-ea1bbd7ba48e',N'berrimar999@gmail.com',1),
    ('4112a2a6-70d8-47f0-a0fe-352edd57cc04','587c6244-6c78-4917-a953-b880a41a4abb',N'fm.operator@richardberriman.onmicrosoft.com',0);

    -- Refuse missing customer evidence or conflicting links; never match by email.
    IF EXISTS (SELECT 1 FROM @Accounts a WHERE a.IsCustomer=1 AND
        (SELECT COUNT(*) FROM dbo.Customers c WITH (UPDLOCK,HOLDLOCK)
         WHERE c.ExternalTenantId=a.TenantId AND c.ExternalUserId=a.ObjectId) <> 1)
        THROW 50101, 'Expected existing customer identity is missing or ambiguous.', 1;
    IF EXISTS (SELECT 1 FROM @Accounts a JOIN dbo.Customers c WITH (UPDLOCK,HOLDLOCK)
        ON c.ExternalTenantId=a.TenantId AND c.ExternalUserId=a.ObjectId
        JOIN dbo.Users u ON u.UserId=c.UserId
        WHERE u.ExternalTenantId<>a.TenantId OR u.ExternalUserId<>a.ObjectId)
        THROW 50102, 'Customer has a conflicting User link.', 1;
    IF EXISTS (SELECT 1 FROM @Accounts a JOIN dbo.Users u WITH (UPDLOCK,HOLDLOCK)
        ON u.ExternalTenantId=a.TenantId AND u.ExternalUserId=a.ObjectId WHERE u.Status<>N'Active')
        THROW 50103, 'Existing User is disabled; review rather than reactivate.', 1;

    INSERT dbo.Users (ExternalTenantId,ExternalUserId,Email)
    SELECT a.TenantId,a.ObjectId,a.Email FROM @Accounts a
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Users u WITH (UPDLOCK,HOLDLOCK)
        WHERE u.ExternalTenantId=a.TenantId AND u.ExternalUserId=a.ObjectId);

    DECLARE @Assignments TABLE (Email nvarchar(320), RoleName nvarchar(64));
    INSERT @Assignments VALUES
    (N'richard.berriman@gmail.com',N'Developer'),
    (N'richard.berriman@gmail.com',N'Administrator'),
    (N'richard.berriman@gmail.com',N'Crater Tester'),
    (N'richard.berriman@gmail.com',N'Customer'),
    (N'berrimar999@gmail.com',N'Reader'),
    (N'fm.operator@richardberriman.onmicrosoft.com',N'Administrator');
    IF EXISTS (SELECT 1 FROM @Assignments s WHERE NOT EXISTS
        (SELECT 1 FROM dbo.Roles r WHERE r.Name=s.RoleName))
        THROW 50104, 'Required 013B role seed is missing.', 1;

    -- Exact role sets for these three identities only; preserve every other account.
    DELETE ur FROM dbo.UserRoles ur JOIN dbo.Users u ON u.UserId=ur.UserId
    JOIN @Accounts a ON a.TenantId=u.ExternalTenantId AND a.ObjectId=u.ExternalUserId
    JOIN dbo.Roles r ON r.RoleId=ur.RoleId
    WHERE NOT EXISTS (SELECT 1 FROM @Assignments s WHERE s.Email=a.Email AND s.RoleName=r.Name);
    INSERT dbo.UserRoles (UserId,RoleId)
    SELECT u.UserId,r.RoleId FROM @Accounts a
    JOIN dbo.Users u ON u.ExternalTenantId=a.TenantId AND u.ExternalUserId=a.ObjectId
    JOIN @Assignments s ON s.Email=a.Email JOIN dbo.Roles r ON r.Name=s.RoleName
    WHERE NOT EXISTS (SELECT 1 FROM dbo.UserRoles ur WITH (UPDLOCK,HOLDLOCK)
        WHERE ur.UserId=u.UserId AND ur.RoleId=r.RoleId);

    UPDATE c SET UserId=u.UserId FROM dbo.Customers c
    JOIN @Accounts a ON a.TenantId=c.ExternalTenantId AND a.ObjectId=c.ExternalUserId AND a.IsCustomer=1
    JOIN dbo.Users u ON u.ExternalTenantId=a.TenantId AND u.ExternalUserId=a.ObjectId
    WHERE c.UserId IS NULL;
    -- CustomerId, identity, status, CraterEnabled, timestamps and artwork are untouched.
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
