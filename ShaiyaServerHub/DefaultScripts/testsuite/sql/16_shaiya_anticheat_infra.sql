-- 16_shaiya_anticheat_infra.sql
-- Verifica che l'infrastruttura anti-tamper/HWID sia installata in PS_UserData:
-- tabelle BlockedHWID, SecurityEvents, Members + colonna Members.LastHWID. FAIL se manca qualcosa.
SET NOCOUNT ON;

DECLARE @missing varchar(400) = '';

IF OBJECT_ID('PS_UserData.dbo.BlockedHWID')   IS NULL SET @missing = @missing + 'BlockedHWID; ';
IF OBJECT_ID('PS_UserData.dbo.SecurityEvents')IS NULL SET @missing = @missing + 'SecurityEvents; ';
IF OBJECT_ID('PS_UserData.dbo.Members')       IS NULL SET @missing = @missing + 'Members; ';
ELSE IF COL_LENGTH('PS_UserData.dbo.Members','LastHWID') IS NULL SET @missing = @missing + 'Members.LastHWID; ';

PRINT 'Infra anti-tamper/HWID in PS_UserData:';
PRINT '  BlockedHWID    = ' + CASE WHEN OBJECT_ID('PS_UserData.dbo.BlockedHWID')    IS NULL THEN 'ASSENTE' ELSE 'OK' END;
PRINT '  SecurityEvents = ' + CASE WHEN OBJECT_ID('PS_UserData.dbo.SecurityEvents') IS NULL THEN 'ASSENTE' ELSE 'OK' END;
PRINT '  Members        = ' + CASE WHEN OBJECT_ID('PS_UserData.dbo.Members')        IS NULL THEN 'ASSENTE' ELSE 'OK' END;
PRINT '  Members.LastHWID = ' + CASE WHEN OBJECT_ID('PS_UserData.dbo.Members') IS NOT NULL
        AND COL_LENGTH('PS_UserData.dbo.Members','LastHWID') IS NOT NULL THEN 'OK' ELSE 'ASSENTE' END;

PRINT 'RESULT: ' + CASE WHEN @missing = '' THEN 'PASS - infra anti-cheat presente'
                        ELSE 'FAIL - mancante: ' + @missing END;
