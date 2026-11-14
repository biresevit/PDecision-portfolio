-- Customer-specific strategy settings. Apply CustomerMachines.sql first.
-- For an existing casino-only table, apply PokerBackoffice/sql/013_customer_casino_settings.sql instead.
IF OBJECT_ID('dbo.CasinoSettings','U') IS NOT NULL AND COL_LENGTH('dbo.CasinoSettings','CustomerId') IS NULL
    THROW 51121,'Migrate existing casino settings with backoffice migration 013 before provisioning.',1;
IF OBJECT_ID('dbo.CasinoSettings','U') IS NULL
BEGIN
    CREATE TABLE dbo.CasinoSettings(
        CustomerId int NOT NULL CONSTRAINT FK_CasinoSettings_Customer REFERENCES dbo.Customer(CustomerId),
        Casino nvarchar(50) NOT NULL,
        UseHelp bit NOT NULL CONSTRAINT DF_CasinoSettings_UseHelp DEFAULT(0),
        ProfileVsHumans varchar(20) NOT NULL CONSTRAINT DF_CasinoSettings_ProfileVsHumans DEFAULT('Everyone')
            CONSTRAINT CK_CasinoSettings_ProfileVsHumans CHECK(ProfileVsHumans IN ('Everyone','WeakHumans','ListedPlayers','Never')),
        ProfileMaxStake varchar(10) NULL CONSTRAINT CK_CasinoSettings_ProfileMaxStake CHECK(ProfileMaxStake IN ('Micro','Low','Mid','High')),
        ProfileInTournaments bit NOT NULL CONSTRAINT DF_CasinoSettings_ProfileInTournaments DEFAULT(1),
        UpdatedUtc datetime2(0) NOT NULL CONSTRAINT DF_CasinoSettings_UpdatedUtc DEFAULT(SYSUTCDATETIME()),
        HelpMinBigBlind decimal(18,4) NULL CONSTRAINT CK_CasinoSettings_HelpMinBigBlind CHECK(HelpMinBigBlind>=0),
        HelpVsHumans varchar(20) NOT NULL CONSTRAINT DF_CasinoSettings_HelpVsHumans DEFAULT('Everyone')
            CONSTRAINT CK_CasinoSettings_HelpVsHumans CHECK(HelpVsHumans IN ('Everyone','ListedPlayers')),
        ProfileMinBigBlind decimal(18,4) NULL CONSTRAINT CK_CasinoSettings_ProfileMinBigBlind CHECK(ProfileMinBigBlind>=0),
        HelpMinBuyIn decimal(18,4) NULL CONSTRAINT CK_CasinoSettings_HelpMinBuyIn CHECK(HelpMinBuyIn>=0),
        SolverVsHumans bit NOT NULL CONSTRAINT DF_CasinoSettings_SolverVsHumans DEFAULT(0),
        SolverMinBigBlind decimal(18,4) NULL CONSTRAINT CK_CasinoSettings_SolverMinBigBlind CHECK(SolverMinBigBlind>=0),
        SolverMinBuyIn decimal(18,4) NULL CONSTRAINT CK_CasinoSettings_SolverMinBuyIn CHECK(SolverMinBuyIn>=0),
        CONSTRAINT PK_CasinoSettings PRIMARY KEY(CustomerId,Casino));
END;
GO
IF COL_LENGTH('dbo.CasinoSettings','HelpVsHumans') IS NULL
    THROW 51122,'Add the card sharing and profile conditions with backoffice migration 019 before provisioning.',1;
IF COL_LENGTH('dbo.CasinoSettings','SolverVsHumans') IS NULL
    THROW 51123,'Add the solver strategy settings with backoffice migration 020 before provisioning.',1;
GO
-- Players against whom a switched-on feature is used at any stake: card sharing ('CardSharing') and the solver strategy ('Solver').
IF OBJECT_ID('dbo.CasinoSettingsPlayer','U') IS NULL
    CREATE TABLE dbo.CasinoSettingsPlayer(
        CustomerId int NOT NULL,
        Casino nvarchar(50) NOT NULL,
        Feature varchar(20) NOT NULL CONSTRAINT CK_CasinoSettingsPlayer_Feature CHECK(Feature IN ('CardSharing','Solver')),
        PlayerName nvarchar(100) NOT NULL,
        AddedUtc datetime2(0) NOT NULL CONSTRAINT DF_CasinoSettingsPlayer_AddedUtc DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_CasinoSettingsPlayer PRIMARY KEY(CustomerId,Casino,Feature,PlayerName),
        CONSTRAINT FK_CasinoSettingsPlayer_Settings FOREIGN KEY(CustomerId,Casino) REFERENCES dbo.CasinoSettings(CustomerId,Casino));
GO
CREATE OR ALTER PROCEDURE dbo.CasinoSettings_Initialize
    @CustomerId int,@Casino nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.CasinoSettings(CustomerId,Casino)
        SELECT @CustomerId,@Casino WHERE NOT EXISTS(
            SELECT 1 FROM dbo.CasinoSettings WITH(UPDLOCK,HOLDLOCK) WHERE CustomerId=@CustomerId AND Casino=@Casino);
END;
GO
CREATE OR ALTER TRIGGER dbo.CustomerMachine_InitializeCasinoSettings ON dbo.CustomerMachine AFTER INSERT,UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.CasinoSettings(CustomerId,Casino)
        SELECT DISTINCT i.CustomerId,i.Casino FROM inserted i WHERE i.IsActive=1 AND NOT EXISTS(
            SELECT 1 FROM dbo.CasinoSettings s WITH(UPDLOCK,HOLDLOCK) WHERE s.CustomerId=i.CustomerId AND s.Casino=i.Casino);
END;
GO
IF OBJECT_ID('scheduler.Site','U') IS NOT NULL EXEC(N'CREATE OR ALTER TRIGGER scheduler.Site_InitializeCasinoSettings ON scheduler.Site AFTER INSERT,UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    -- SchedulingEnabled pauses traffic; the customer''s configured site and its settings remain enabled.
    INSERT dbo.CasinoSettings(CustomerId,Casino)
        SELECT DISTINCT i.CustomerId,i.Casino FROM inserted i WHERE NOT EXISTS(
            SELECT 1 FROM dbo.CasinoSettings s WITH(UPDLOCK,HOLDLOCK) WHERE s.CustomerId=i.CustomerId AND s.Casino=i.Casino);
END;');
GO
CREATE OR ALTER PROCEDURE dbo.CasinoSettings_List
    @CustomerId int=NULL,@Casino nvarchar(50)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- Columns are only ever appended: PDecision before 1.0.0.49 reads the first five, 1.0.0.49 the first eight.
    IF @CustomerId IS NULL
    BEGIN
        -- Legacy clients have no customer context. Return fixed defaults, never any customer's configured values.
        SELECT DISTINCT Casino,CAST(0 AS bit) AS UseHelp,CAST('Everyone' AS varchar(20)) AS ProfileVsHumans,
            CAST(NULL AS varchar(10)) AS ProfileMaxStake,CAST(1 AS bit) AS ProfileInTournaments,
            CAST(NULL AS decimal(18,4)) AS HelpMinBigBlind,CAST('Everyone' AS varchar(20)) AS HelpVsHumans,
            CAST(NULL AS decimal(18,4)) AS ProfileMinBigBlind,CAST(NULL AS decimal(18,4)) AS HelpMinBuyIn,
            CAST(0 AS bit) AS SolverVsHumans,CAST(NULL AS decimal(18,4)) AS SolverMinBigBlind,
            CAST(NULL AS decimal(18,4)) AS SolverMinBuyIn FROM dbo.CasinoSettings;
        RETURN;
    END;
    SELECT Casino,UseHelp,ProfileVsHumans,ProfileMaxStake,ProfileInTournaments,HelpMinBigBlind,HelpVsHumans,ProfileMinBigBlind,
        HelpMinBuyIn,SolverVsHumans,SolverMinBigBlind,SolverMinBuyIn
        FROM dbo.CasinoSettings WHERE CustomerId=@CustomerId AND (@Casino IS NULL OR Casino=@Casino);
END;
GO
CREATE OR ALTER PROCEDURE dbo.CasinoSettings_Upsert
    @Casino nvarchar(50),@UseHelp bit=0,@CustomerId int=NULL,
    @HelpMinBigBlind decimal(18,4)=NULL,@HelpMinBuyIn decimal(18,4)=NULL,
    @SolverVsHumans bit=0,@SolverMinBigBlind decimal(18,4)=NULL,@SolverMinBuyIn decimal(18,4)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @CustomerId IS NULL THROW 51120,'CustomerId is required for casino settings.',1;
    -- legacy columns for PDecision before 1.0.0.50
    DECLARE @Profile varchar(20)=CASE WHEN @SolverVsHumans=1 THEN 'Never' ELSE 'Everyone' END;
    UPDATE dbo.CasinoSettings WITH(UPDLOCK,HOLDLOCK)
        SET UseHelp=@UseHelp,HelpMinBigBlind=@HelpMinBigBlind,HelpVsHumans='Everyone',HelpMinBuyIn=@HelpMinBuyIn,
            SolverVsHumans=@SolverVsHumans,SolverMinBigBlind=@SolverMinBigBlind,SolverMinBuyIn=@SolverMinBuyIn,
            ProfileVsHumans=@Profile,ProfileMaxStake=NULL,ProfileMinBigBlind=NULL,ProfileInTournaments=1-@SolverVsHumans,
            UpdatedUtc=SYSUTCDATETIME()
        WHERE CustomerId=@CustomerId AND Casino=@Casino;
    IF @@ROWCOUNT=0 INSERT dbo.CasinoSettings(CustomerId,Casino,UseHelp,HelpMinBigBlind,HelpVsHumans,HelpMinBuyIn,
            SolverVsHumans,SolverMinBigBlind,SolverMinBuyIn,ProfileVsHumans,ProfileMaxStake,ProfileMinBigBlind,ProfileInTournaments)
        VALUES(@CustomerId,@Casino,@UseHelp,@HelpMinBigBlind,'Everyone',@HelpMinBuyIn,
            @SolverVsHumans,@SolverMinBigBlind,@SolverMinBuyIn,@Profile,NULL,NULL,1-@SolverVsHumans);
END;
GO
CREATE OR ALTER PROCEDURE dbo.CasinoSettingsPlayer_List
    @CustomerId int,@Casino nvarchar(50)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Casino,Feature,PlayerName FROM dbo.CasinoSettingsPlayer
        WHERE CustomerId=@CustomerId AND (@Casino IS NULL OR Casino=@Casino);
END;
GO
-- Existing registrations also receive defaults on a fresh settings-table installation.
INSERT dbo.CasinoSettings(CustomerId,Casino)
    SELECT DISTINCT m.CustomerId,m.Casino FROM dbo.CustomerMachine m WHERE m.IsActive=1
      AND NOT EXISTS(SELECT 1 FROM dbo.CasinoSettings s WHERE s.CustomerId=m.CustomerId AND s.Casino=m.Casino);
IF OBJECT_ID('scheduler.Site','U') IS NOT NULL EXEC(N'
    INSERT dbo.CasinoSettings(CustomerId,Casino)
        SELECT CustomerId,Casino FROM scheduler.Site e WHERE NOT EXISTS(
            SELECT 1 FROM dbo.CasinoSettings s WHERE s.CustomerId=e.CustomerId AND s.Casino=e.Casino);');
GO
IF EXISTS(SELECT 1 FROM sys.database_principals WHERE name='pdecision_client') GRANT EXECUTE ON dbo.CasinoSettings_List TO pdecision_client;
IF EXISTS(SELECT 1 FROM sys.database_principals WHERE name='pdecision_client') GRANT EXECUTE ON dbo.CasinoSettingsPlayer_List TO pdecision_client;
