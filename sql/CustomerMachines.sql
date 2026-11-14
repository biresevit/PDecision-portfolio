-- Customers and the machines they may run PDecision on.
-- PDecision reads its casino and platform from here at startup, using the MAC addresses of the PC's network adapters.
-- Target: Azure SQL pokerdbserver / pokerdb. Safe to run more than once.

-- ============================================================
-- Tables
-- ============================================================

IF OBJECT_ID(N'dbo.Customer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Customer
    (
        CustomerId   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customer PRIMARY KEY,
        CustomerName nvarchar(100) NOT NULL CONSTRAINT UQ_Customer_CustomerName UNIQUE,
        IsActive     bit           NOT NULL CONSTRAINT DF_Customer_IsActive DEFAULT (1),
        CreatedUtc   datetime2(0)  NOT NULL CONSTRAINT DF_Customer_CreatedUtc DEFAULT (SYSUTCDATETIME())
    );
END
GO

IF OBJECT_ID(N'dbo.CustomerMachine', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerMachine
    (
        MachineId    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerMachine PRIMARY KEY,
        CustomerId   int           NOT NULL CONSTRAINT FK_CustomerMachine_Customer REFERENCES dbo.Customer (CustomerId),
        MacAddress   char(17)      NOT NULL CONSTRAINT UQ_CustomerMachine_MacAddress UNIQUE,   -- AA-BB-CC-DD-EE-FF, upper case
        Casino       nvarchar(50)  NOT NULL,          -- DBLib.EnumCasino name, e.g. 'BluffPoker'
        Platform     nvarchar(50)  NOT NULL,          -- PokerSuite enPlatform name, e.g. 'RunYourOwnPokerSite'
        Description  nvarchar(200) NULL,              -- e.g. 'office PC 1, Ethernet'
        IsActive     bit           NOT NULL CONSTRAINT DF_CustomerMachine_IsActive DEFAULT (1),
        CreatedUtc   datetime2(0)  NOT NULL CONSTRAINT DF_CustomerMachine_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        LastSeenUtc  datetime2(0)  NULL,              -- set when a service on this machine starts
        CONSTRAINT CK_CustomerMachine_MacAddress CHECK (MacAddress LIKE '[0-9A-F][0-9A-F]-[0-9A-F][0-9A-F]-[0-9A-F][0-9A-F]-[0-9A-F][0-9A-F]-[0-9A-F][0-9A-F]-[0-9A-F][0-9A-F]')
    );
END
GO

-- ============================================================
-- Procedures used by PDecision
-- ============================================================

-- @MacAddresses: comma separated MACs of the PC's physical adapters, e.g. '00-1A-2B-3C-4D-5E,00-1A-2B-3C-4D-5F'.
-- Returns the registered machine (active, of an active customer) and stamps LastSeenUtc. No row: not registered.
CREATE OR ALTER PROCEDURE dbo.Machine_GetConfig
    @MacAddresses nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @machine TABLE (MachineId int, MacAddress char(17), CustomerId int, CustomerName nvarchar(100), Casino nvarchar(50), Platform nvarchar(50));

    INSERT INTO @machine
    SELECT TOP (1) m.MachineId, m.MacAddress, c.CustomerId, c.CustomerName, m.Casino, m.Platform
    FROM dbo.CustomerMachine m
    JOIN dbo.Customer c ON c.CustomerId = m.CustomerId
    WHERE m.IsActive = 1 AND c.IsActive = 1
      AND m.MacAddress IN (SELECT UPPER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(@MacAddresses, ','))
    ORDER BY m.MachineId;

    UPDATE dbo.CustomerMachine SET LastSeenUtc = SYSUTCDATETIME()
    WHERE MachineId IN (SELECT MachineId FROM @machine);

    SELECT MachineId, MacAddress, CustomerId, CustomerName, Casino, Platform FROM @machine;
END
GO

-- ============================================================
-- Maintenance procedures
-- ============================================================

-- Adds a customer, or reactivates / keeps an existing one with that name. Returns the CustomerId.
CREATE OR ALTER PROCEDURE dbo.Customer_Upsert
    @CustomerName nvarchar(100),
    @IsActive     bit = 1
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Customer SET IsActive = @IsActive WHERE CustomerName = @CustomerName;
    IF @@ROWCOUNT = 0
        INSERT INTO dbo.Customer (CustomerName, IsActive) VALUES (@CustomerName, @IsActive);
    SELECT CustomerId FROM dbo.Customer WHERE CustomerName = @CustomerName;
END
GO

-- Registers a machine for a customer, or updates it. Accepts AA-BB-CC-DD-EE-FF, AA:BB:CC:DD:EE:FF or AABBCCDDEEFF.
CREATE OR ALTER PROCEDURE dbo.Machine_Upsert
    @CustomerName nvarchar(100),
    @MacAddress   nvarchar(20),
    @Casino       nvarchar(50),
    @Platform     nvarchar(50),
    @Description  nvarchar(200) = NULL,
    @IsActive     bit = 1
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @hex varchar(12) = UPPER(REPLACE(REPLACE(REPLACE(@MacAddress, '-', ''), ':', ''), ' ', ''));
    IF LEN(@hex) <> 12
        THROW 50001, 'MacAddress must have 12 hex digits', 1;
    DECLARE @mac char(17) = SUBSTRING(@hex,1,2) + '-' + SUBSTRING(@hex,3,2) + '-' + SUBSTRING(@hex,5,2) + '-' +
                            SUBSTRING(@hex,7,2) + '-' + SUBSTRING(@hex,9,2) + '-' + SUBSTRING(@hex,11,2);

    DECLARE @customerId int = (SELECT CustomerId FROM dbo.Customer WHERE CustomerName = @CustomerName);
    IF @customerId IS NULL
        THROW 50002, 'Unknown customer, add it with dbo.Customer_Upsert first', 1;

    UPDATE dbo.CustomerMachine
    SET CustomerId = @customerId, Casino = @Casino, Platform = @Platform, Description = @Description, IsActive = @IsActive
    WHERE MacAddress = @mac;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.CustomerMachine (CustomerId, MacAddress, Casino, Platform, Description, IsActive)
        VALUES (@customerId, @mac, @Casino, @Platform, @Description, @IsActive);
END
GO
