/* ============================================================================
   Notice-Maintenance-AUTO.sql  —  NUOVO canale PSM_Cmd (usp_SendNotice)
   Sostituisce il vecchio [OMG_GameWEB].[dbo].[Command] con il wrapper stretto
   [PSM_Cmd].[dbo].[usp_SendNotice] (solo /nt, sanificato + audit).
   NB: il wrapper antepone da solo "/nt " e taglia il testo a 150 char.
       Eseguire come login abilitato a usp_SendNotice (Ernoweb@ o ShaiyaTaskAgent).
   ============================================================================ */
USE [master];
GO

DECLARE @rc INT;

-- Primo messaggio
EXEC @rc = [PSM_Cmd].[dbo].[usp_SendNotice]
    @text = N'Welcome to Shaiya Duff!', @service = N'ps_game';

-- Secondo messaggio
EXEC @rc = [PSM_Cmd].[dbo].[usp_SendNotice]
    @text = N'[AUTO Server Maintenance] WITHIN 12 Min - 04:00 SERVER TIME!!', @service = N'ps_game';

-- Terzo messaggio
EXEC @rc = [PSM_Cmd].[dbo].[usp_SendNotice]
    @text = N'[ATTENTION!]Please exit the game before you lose your data!!', @service = N'ps_game';
GO
