-- REVIEW ONLY. Run manually against an explicitly selected development database.
-- No startup migration. Existing operator/catalogue tables are unchanged.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Customers', N'U') IS NOT NULL
    THROW 50000, 'Customers already exists; review its schema before proceeding.', 1;
CREATE TABLE dbo.Customers (
    CustomerId uniqueidentifier NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    ExternalTenantId uniqueidentifier NOT NULL,
    ExternalUserId uniqueidentifier NOT NULL,
    Status nvarchar(16) NOT NULL CONSTRAINT DF_Customers_Status DEFAULT N'Active',
    CraterEnabled bit NOT NULL CONSTRAINT DF_Customers_CraterEnabled DEFAULT 0,
    CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_Customers_CreatedUtc DEFAULT SYSUTCDATETIME(),
    UpdatedUtc datetime2(7) NOT NULL CONSTRAINT DF_Customers_UpdatedUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Customers_ExternalIdentity UNIQUE (ExternalTenantId,ExternalUserId),
    CONSTRAINT CK_Customers_Status CHECK (Status IN (N'Active',N'Disabled'))
);
COMMIT;
-- Runtime principal needs SELECT and INSERT only; account disable uses a separate administrative identity.
