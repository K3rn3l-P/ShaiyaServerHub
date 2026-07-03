-- PS_GameLog.dbo.ActionLog
DELETE FROM [PS_GameLog].[dbo].[ActionLog]
WHERE [ActionTime] < DATEADD(DAY, -7, GETDATE());

-- PS_UserData.dbo.LoginAttempts
DELETE FROM [PS_UserData].[dbo].[LoginAttempts]
WHERE [AttemptTime] < DATEADD(DAY, -7, GETDATE());

-- PS_UserData.dbo.UserLoginLog
DELETE FROM [PS_UserData].[dbo].[UserLoginLog]
WHERE [LoginTime] < DATEADD(DAY, -7, GETDATE());

-- PS_GameLog.dbo.UserLog
DELETE FROM [PS_GameLog].[dbo].[UserLog]
WHERE [LogTime] < DATEADD(DAY, -7, GETDATE());

-- PS_GameLog.dbo.Notified_Respawns
DELETE FROM [PS_GameLog].[dbo].[Notified_Respawns]
WHERE [RespawnTime] < DATEADD(DAY, -7, GETDATE());

-- PS_GameLog.dbo.Boss_Death_Log
DELETE FROM [PS_GameLog].[dbo].[Boss_Death_Log]
WHERE [ActionTime] < DATEADD(DAY, -7, GETDATE());
