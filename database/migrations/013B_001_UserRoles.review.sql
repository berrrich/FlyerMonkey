-- PROPOSAL ONLY. Mission 013B does not authorize execution.
-- Change @Apply only after approval and explicit selection of the target database.
-- Contains role seeds only. User creation, assignments and Customer links need separate review.
DECLARE @Apply bit = 0;
IF @Apply <> 1
    THROW 50000, 'Review-only migration. Execution requires approval.', 1;

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.UserRoles', N'U') IS NOT NULL
        THROW 50001, 'Proposed tables already exist. Review before applying.', 1;
    IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
        THROW 50002, 'Existing Customers table is required.', 1;
    IF COL_LENGTH(N'dbo.Customers', N'UserId') IS NOT NULL
        THROW 50003, 'Customers.UserId already exists. Review before applying.', 1;

    CREATE TABLE dbo.Users (
        UserId uniqueidentifier NOT NULL CONSTRAINT DF_Users_Id DEFAULT NEWID()
            CONSTRAINT PK_Users PRIMARY KEY,
        ExternalTenantId uniqueidentifier NOT NULL,
        ExternalUserId uniqueidentifier NOT NULL,
        Email nvarchar(320) NULL,
        DisplayName nvarchar(200) NULL,
        Status nvarchar(16) NOT NULL CONSTRAINT DF_Users_Status DEFAULT N'Active',
        CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_Users_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc datetime2(7) NOT NULL CONSTRAINT DF_Users_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Users_ExternalIdentity UNIQUE (ExternalTenantId, ExternalUserId),
        CONSTRAINT CK_Users_Status CHECK (Status IN (N'Active', N'Disabled'))
    );

    CREATE TABLE dbo.Roles (
        RoleId smallint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
        Name nvarchar(64) NOT NULL CONSTRAINT UQ_Roles_Name UNIQUE
    );

    CREATE TABLE dbo.UserRoles (
        UserId uniqueidentifier NOT NULL,
        RoleId smallint NOT NULL,
        CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles(RoleId)
    );

    -- Existing rows receive NULL. Existing CustomerIds and identity columns are untouched.
    ALTER TABLE dbo.Customers ADD UserId uniqueidentifier NULL;
    ALTER TABLE dbo.Customers ADD CONSTRAINT FK_Customers_Users
        FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId);
    CREATE UNIQUE INDEX UX_Customers_UserId ON dbo.Customers(UserId)
        WHERE UserId IS NOT NULL;

    INSERT dbo.Roles (Name) VALUES
        (N'Reader'), (N'Customer'), (N'Crater Tester'),
        (N'Developer'), (N'Administrator'), (N'Artwork Reviewer');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
