/* ============================================================================
   notice-enter.sql  —  Notice IN GIOCO: manutenzione avviata (canale PSM_Cmd)
   NON anteporre "/nt": lo fa il wrapper usp_SendNotice. Cap 150 char nel wrapper.
   ============================================================================ */
USE [master];
GO

EXEC [PSM_Cmd].[dbo].[usp_SendNotice]
    @text    = N'MAINTENANCE MODE: normal players will be kicked and login is disabled for non-admins.',
    @service = N'ps_game';
GO
