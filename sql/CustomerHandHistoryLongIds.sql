-- Preserve BluffPoker's table UUID plus hand number. A 30-character procedure
-- parameter silently truncated IDs and made every later hand at a table a duplicate.
-- Apply to existing installations before deploying CustomerHandHistory.sql again.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.CustomerHandHistory', 'HandId') < 100
BEGIN
    ALTER TABLE dbo.CustomerHandHistory DROP CONSTRAINT UQ_CustomerHandHistory_Hand;
    DROP INDEX IX_CustomerHandHistory_List ON dbo.CustomerHandHistory;
    ALTER TABLE dbo.CustomerHandHistory ALTER COLUMN HandId varchar(100) NOT NULL;
    ALTER TABLE dbo.CustomerHandHistory ADD CONSTRAINT UQ_CustomerHandHistory_Hand
        UNIQUE (CustomerId, PokerSite, HandId);
    CREATE INDEX IX_CustomerHandHistory_List ON dbo.CustomerHandHistory (CustomerId, PokerSite, Id)
        INCLUDE (MachineId, HandId, GameType, BlobPath);
END;

-- Preserve the deployed procedures' customer isolation and permissions.
DECLARE @name sysname, @definition nvarchar(max);
DECLARE procedures CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.procedures WHERE schema_id = SCHEMA_ID('dbo')
        AND name IN ('HH_Exists', 'HH_Add');
OPEN procedures;
FETCH NEXT FROM procedures INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @definition = OBJECT_DEFINITION(OBJECT_ID('dbo.' + @name));
    SET @definition = REPLACE(@definition, 'varchar(30)', 'varchar(100)');
    SET @definition = REPLACE(@definition, 'CREATE OR ALTER PROCEDURE', 'ALTER PROCEDURE');
    SET @definition = REPLACE(@definition, 'CREATE   PROCEDURE', 'ALTER PROCEDURE');
    SET @definition = REPLACE(@definition, 'CREATE PROCEDURE', 'ALTER PROCEDURE');
    EXEC sys.sp_executesql @definition;
    FETCH NEXT FROM procedures INTO @name;
END;
CLOSE procedures;
DEALLOCATE procedures;

-- The blob filename retained the full ID even when the SQL parameter truncated it.
-- Restore only provably truncated IDs; keep paths, ownership and reporting IDs intact.
UPDATE h SET HandId = f.FullId
FROM dbo.CustomerHandHistory h
CROSS APPLY (SELECT RIGHT(h.BlobPath, CHARINDEX('/', REVERSE(h.BlobPath)) - 1) AS FileName) b
CROSS APPLY (SELECT LEFT(b.FileName, LEN(b.FileName) - 4) AS FullId) f
WHERE LEN(h.HandId) = 30 AND RIGHT(b.FileName, 4) = '.txt'
    AND LEN(f.FullId) BETWEEN 31 AND 100
    AND LEFT(f.FullId, 30) = h.HandId;

COMMIT TRANSACTION;
