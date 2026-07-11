/* ============================================================================
   Pulizia-Log-database.sql — pulizia log piu' vecchi di 7 giorni
   Ogni tabella e' isolata: IF OBJECT_ID(...) IS NOT NULL salta silenziosamente
   le tabelle mancanti (nessun errore), TRY/CATCH copre qualsiasi altro problema
   runtime (permessi, lock, ecc.) — una tabella con problemi NON blocca le altre.
   Instantaneo cancellazione "[PS_GameLog].[dbo].[Boss_Death_Log]" per il gioco!
   ============================================================================ */

-- PS_GameLog.dbo.ActionLog
BEGIN TRY
    IF OBJECT_ID('PS_GameLog.dbo.ActionLog') IS NOT NULL
    BEGIN
        DELETE FROM [PS_GameLog].[dbo].[ActionLog]
        WHERE [ActionTime] < DATEADD(DAY, -7, GETDATE());
    END
    ELSE PRINT 'SKIP: PS_GameLog.dbo.ActionLog non trovata';
END TRY
BEGIN CATCH
    PRINT 'ERRORE: PS_GameLog.dbo.ActionLog - ' + ERROR_MESSAGE();
END CATCH

-- PS_UserData.dbo.LoginAttempts
BEGIN TRY
    IF OBJECT_ID('PS_UserData.dbo.LoginAttempts') IS NOT NULL
    BEGIN
        DELETE FROM [PS_UserData].[dbo].[LoginAttempts]
        WHERE [AttemptTime] < DATEADD(DAY, -7, GETDATE());
    END
    ELSE PRINT 'SKIP: PS_UserData.dbo.LoginAttempts non trovata';
END TRY
BEGIN CATCH
    PRINT 'ERRORE: PS_UserData.dbo.LoginAttempts - ' + ERROR_MESSAGE();
END CATCH

-- PS_UserData.dbo.UserLoginLog
BEGIN TRY
    IF OBJECT_ID('PS_UserData.dbo.UserLoginLog') IS NOT NULL
    BEGIN
        DELETE FROM [PS_UserData].[dbo].[UserLoginLog]
        WHERE [LoginTime] < DATEADD(DAY, -7, GETDATE());
    END
    ELSE PRINT 'SKIP: PS_UserData.dbo.UserLoginLog non trovata';
END TRY
BEGIN CATCH
    PRINT 'ERRORE: PS_UserData.dbo.UserLoginLog - ' + ERROR_MESSAGE();
END CATCH

-- PS_GameLog.dbo.UserLog
BEGIN TRY
    IF OBJECT_ID('PS_GameLog.dbo.UserLog') IS NOT NULL
    BEGIN
        DELETE FROM [PS_GameLog].[dbo].[UserLog]
        WHERE [LogTime] < DATEADD(DAY, -7, GETDATE());
    END
    ELSE PRINT 'SKIP: PS_GameLog.dbo.UserLog non trovata';
END TRY
BEGIN CATCH
    PRINT 'ERRORE: PS_GameLog.dbo.UserLog - ' + ERROR_MESSAGE();
END CATCH

-- PS_GameLog.dbo.Notified_Respawns
BEGIN TRY
    IF OBJECT_ID('PS_GameLog.dbo.Notified_Respawns') IS NOT NULL
    BEGIN
        DELETE FROM [PS_GameLog].[dbo].[Notified_Respawns]
        WHERE [RespawnTime] < DATEADD(DAY, -7, GETDATE());
    END
    ELSE PRINT 'SKIP: PS_GameLog.dbo.Notified_Respawns non trovata';
END TRY
BEGIN CATCH
    PRINT 'ERRORE: PS_GameLog.dbo.Notified_Respawns - ' + ERROR_MESSAGE();
END CATCH

-- PS_GameLog.dbo.Boss_Death_Log
BEGIN TRY
    IF OBJECT_ID('PS_GameLog.dbo.Boss_Death_Log') IS NOT NULL
    BEGIN
        DELETE FROM [PS_GameLog].[dbo].[Boss_Death_Log] -- ISTANTANEO PER IL GIOCO!
        --WHERE [ActionTime] < DATEADD(DAY, -7, GETDATE());
    END
    ELSE PRINT 'SKIP: PS_GameLog.dbo.Boss_Death_Log non trovata';
END TRY
BEGIN CATCH
    PRINT 'ERRORE: PS_GameLog.dbo.Boss_Death_Log - ' + ERROR_MESSAGE();
END CATCH

-- PS_UserData.dbo.SecurityEvents
BEGIN TRY
    IF OBJECT_ID('PS_UserData.dbo.SecurityEvents') IS NOT NULL
    BEGIN
        DELETE FROM [PS_UserData].[dbo].[SecurityEvents]
        WHERE [EventTime] < DATEADD(DAY, -7, GETDATE());
    END
    ELSE PRINT 'SKIP: PS_UserData.dbo.SecurityEvents non trovata';
END TRY
BEGIN CATCH
    PRINT 'ERRORE: PS_UserData.dbo.SecurityEvents - ' + ERROR_MESSAGE();
END CATCH

-- PSM_Cmd.dbo.GmCommandLog
BEGIN TRY
    IF OBJECT_ID('PSM_Cmd.dbo.GmCommandLog') IS NOT NULL
    BEGIN
        DELETE FROM [PSM_Cmd].[dbo].[GmCommandLog]
        WHERE [Ts] < DATEADD(DAY, -7, GETDATE());
    END
    ELSE PRINT 'SKIP: PSM_Cmd.dbo.GmCommandLog non trovata';
END TRY
BEGIN CATCH
    PRINT 'ERRORE: PSM_Cmd.dbo.GmCommandLog - ' + ERROR_MESSAGE();
END CATCH

-- PS_ChatLog.dbo.ChatLog
BEGIN TRY
    IF OBJECT_ID('PS_ChatLog.dbo.ChatLog') IS NOT NULL
    BEGIN
        DELETE FROM [PS_ChatLog].[dbo].[ChatLog]
        WHERE [ChatTime] < DATEADD(DAY, -7, GETDATE());
    END
    ELSE PRINT 'SKIP: PS_ChatLog.dbo.ChatLog non trovata';
END TRY
BEGIN CATCH
    PRINT 'ERRORE: PS_ChatLog.dbo.ChatLog - ' + ERROR_MESSAGE();
END CATCH
