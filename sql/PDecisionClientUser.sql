-- Restricted database user for PDecision on customer PCs.
-- It can only run the procedures PDecision needs and read / add rows in the two tables it uses; no updates, no deletes,
-- no other tables. The admin login (pokeruser) is never shipped.
-- Target: Azure SQL pokerdbserver / pokerdb. Run with: sqlcmd CONFIGURE_LOCALLY -v ClientPassword="<password>" -i PDecisionClientUser.sql
-- Running it again resets the password and re-applies the permissions.

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'pdecision_client')
    CREATE USER pdecision_client WITH PASSWORD = '$(ClientPassword)';
ELSE
    ALTER USER pdecision_client WITH PASSWORD = '$(ClientPassword)';
GO

-- machine registration (also stamps LastSeenUtc inside the procedure)
GRANT EXECUTE ON dbo.Machine_GetConfig TO pdecision_client;

-- bot accounts, profiles and winners (read only)
GRANT EXECUTE ON dbo.BotAccount_ListForCasino TO pdecision_client;
GRANT EXECUTE ON dbo.BotAccount_GetStrategy TO pdecision_client;
GRANT EXECUTE ON dbo.BotWinner_List TO pdecision_client;

-- strategy switches per casino (sql/CasinoSettings.sql)
GRANT EXECUTE ON dbo.CasinoSettings_List TO pdecision_client;

-- card sharing between our bots (DecisionMaking DBOtherAccounts)
GRANT SELECT, INSERT ON dbo.GameState TO pdecision_client;

-- hand histories of the caller's customer only (sql/CustomerHandHistory.sql); the old shared dbo.HandHistory is not reachable
GRANT EXECUTE ON dbo.HH_Exists TO pdecision_client;
GRANT EXECUTE ON dbo.HH_Add TO pdecision_client;
GRANT EXECUTE ON dbo.HH_ListAfter TO pdecision_client;
REVOKE SELECT, INSERT ON dbo.HandHistory FROM pdecision_client;
GO
