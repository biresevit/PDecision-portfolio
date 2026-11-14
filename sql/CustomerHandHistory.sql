-- Hand histories per customer. Each hand is uploaded once per customer and downloaded by the customer's other machines,
-- so all machines of a customer have the same player statistics. Customers never see each other's hands.
-- The procedures identify the caller by its registered MAC address and resolve the customer on the server.
-- Target: Azure SQL pokerdbserver / pokerdb. Requires sql/CustomerMachines.sql. Safe to run more than once.

IF OBJECT_ID(N'dbo.CustomerHandHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerHandHistory
    (
        Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerHandHistory PRIMARY KEY,
        CustomerId  int           NOT NULL CONSTRAINT FK_CustomerHandHistory_Customer REFERENCES dbo.Customer (CustomerId),
        MachineId   int           NOT NULL CONSTRAINT FK_CustomerHandHistory_Machine REFERENCES dbo.CustomerMachine (MachineId),
        PokerSite   tinyint       NOT NULL,          -- DBLib.EnumCasino value
        HandId      varchar(100)  NOT NULL,
        GameType    tinyint       NOT NULL,          -- DBLib.EnumGameType value
        BigBlind    decimal(18,4) NULL,
        HeroPlayer  nvarchar(40)  NULL,
        BlobPath    nvarchar(400) NOT NULL,          -- in container hand-histories: <CustomerId>/<Casino>/<HandId>.txt
        UploadedUtc datetime2(3)  NOT NULL CONSTRAINT DF_CustomerHandHistory_UploadedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_CustomerHandHistory_Hand UNIQUE (CustomerId, PokerSite, HandId)
    );
    CREATE INDEX IX_CustomerHandHistory_List ON dbo.CustomerHandHistory (CustomerId, PokerSite, Id)
        INCLUDE (MachineId, HandId, GameType, BlobPath);
END
GO

-- True (1) if the caller's customer already has this hand. No row: the MAC is not registered or not active.
CREATE OR ALTER PROCEDURE dbo.HH_Exists
    @MacAddress char(17),
    @PokerSite  tinyint,
    @HandId     varchar(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.CustomerHandHistory h
                                  WHERE h.CustomerId = m.CustomerId AND h.PokerSite = @PokerSite AND h.HandId = @HandId)
                     THEN 1 ELSE 0 END AS bit) AS HandExists
    FROM dbo.CustomerMachine m
    JOIN dbo.Customer c ON c.CustomerId = m.CustomerId
    WHERE m.MacAddress = @MacAddress AND m.IsActive = 1 AND c.IsActive = 1;
END
GO

-- Stores a hand for the caller's customer, once. Returns Inserted = 1 if it was new, 0 if the customer already had it.
CREATE OR ALTER PROCEDURE dbo.HH_Add
    @MacAddress char(17),
    @PokerSite  tinyint,
    @HandId     varchar(100),
    @GameType   tinyint,
    @BigBlind   decimal(18,4),
    @HeroPlayer nvarchar(40),
    @BlobPath   nvarchar(400)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @machineId int, @customerId int;
    SELECT @machineId = m.MachineId, @customerId = m.CustomerId
    FROM dbo.CustomerMachine m
    JOIN dbo.Customer c ON c.CustomerId = m.CustomerId
    WHERE m.MacAddress = @MacAddress AND m.IsActive = 1 AND c.IsActive = 1;

    IF @machineId IS NULL
        THROW 50010, 'machine is not registered or not active', 1;

    BEGIN TRY
        INSERT INTO dbo.CustomerHandHistory (CustomerId, MachineId, PokerSite, HandId, GameType, BigBlind, HeroPlayer, BlobPath)
        VALUES (@customerId, @machineId, @PokerSite, @HandId, @GameType, @BigBlind, @HeroPlayer, @BlobPath);
        SELECT CAST(1 AS bit) AS Inserted;
    END TRY
    BEGIN CATCH
        -- another bot of the same customer stored this hand first
        IF ERROR_NUMBER() IN (2627, 2601)
            SELECT CAST(0 AS bit) AS Inserted;
        ELSE
            THROW;
    END CATCH
END
GO

-- The caller's customer's hands with Id > @AfterId uploaded by its OTHER machines, oldest first, at most @Max rows.
CREATE OR ALTER PROCEDURE dbo.HH_ListAfter
    @MacAddress char(17),
    @PokerSite  tinyint,
    @AfterId    bigint,
    @Max        int = 500
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @machineId int, @customerId int;
    SELECT @machineId = m.MachineId, @customerId = m.CustomerId
    FROM dbo.CustomerMachine m
    JOIN dbo.Customer c ON c.CustomerId = m.CustomerId
    WHERE m.MacAddress = @MacAddress AND m.IsActive = 1 AND c.IsActive = 1;

    IF @machineId IS NULL
        THROW 50010, 'machine is not registered or not active', 1;

    SELECT TOP (@Max) h.Id, h.HandId, h.GameType, h.BlobPath
    FROM dbo.CustomerHandHistory h
    WHERE h.CustomerId = @customerId AND h.PokerSite = @PokerSite AND h.Id > @AfterId AND h.MachineId <> @machineId
    ORDER BY h.Id;
END
GO

-- All of the caller's customer's hands for one casino (from every one of its machines, including the caller's
-- own) uploaded between @FromUtc and @ToUtc, oldest first, at most @Max rows. For the "download hand
-- histories" report/review tool in PDecision's UI: unlike HH_ListAfter (the background sync between a
-- customer's machines, which excludes the caller's own uploads on purpose), a review of "all of this
-- customer's hands" must include them. Paged with @AfterId, same as HH_ListAfter. @GameType is optional
-- (DBLib.EnumGameType value); NULL returns every game type, same as before this parameter existed.
CREATE OR ALTER PROCEDURE dbo.HH_ListAllForCustomer
    @MacAddress char(17),
    @PokerSite  tinyint,
    @FromUtc    datetime2(3),
    @ToUtc      datetime2(3),
    @GameType   tinyint = NULL,
    @AfterId    bigint = 0,
    @Max        int = 500
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customerId int;
    SELECT @customerId = m.CustomerId
    FROM dbo.CustomerMachine m
    JOIN dbo.Customer c ON c.CustomerId = m.CustomerId
    WHERE m.MacAddress = @MacAddress AND m.IsActive = 1 AND c.IsActive = 1;

    IF @customerId IS NULL
        THROW 50010, 'machine is not registered or not active', 1;

    SELECT TOP (@Max) h.Id, h.HandId, h.GameType, h.BlobPath
    FROM dbo.CustomerHandHistory h
    WHERE h.CustomerId = @customerId AND h.PokerSite = @PokerSite AND h.Id > @AfterId
          AND h.UploadedUtc >= @FromUtc AND h.UploadedUtc <= @ToUtc
          AND (@GameType IS NULL OR h.GameType = @GameType)
    ORDER BY h.Id;
END
GO

-- The restricted PDecision login only reaches hand histories through these procedures
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'pdecision_client')
BEGIN
    GRANT EXECUTE ON dbo.HH_Exists TO pdecision_client;
    GRANT EXECUTE ON dbo.HH_Add TO pdecision_client;
    GRANT EXECUTE ON dbo.HH_ListAfter TO pdecision_client;
    GRANT EXECUTE ON dbo.HH_ListAllForCustomer TO pdecision_client;
    REVOKE SELECT, INSERT ON dbo.HandHistory FROM pdecision_client;
END
GO
