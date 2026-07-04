-- PS_GameLog.dbo.Boss_Death_Log
DELETE FROM PS_GameLog.dbo.Boss_Death_Log;
 
-- PS_GameLog.dbo.Notified_Respawns
--DELETE FROM PS_GameLog.dbo.Notified_Respawns;

-- PS_UserData: elimina i log più vecchi di 30 giorni in LoginAttempts
DELETE FROM PS_UserData.dbo.LoginAttempts WHERE AttemptTime < DATEADD(DAY, -30, GETDATE())
