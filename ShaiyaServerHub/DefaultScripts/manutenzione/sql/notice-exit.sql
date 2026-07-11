/* ============================================================================
   notice-exit.sql  —  Notice IN GIOCO: manutenzione terminata (canale PSM_Cmd)
   NON anteporre "/nt": lo fa il wrapper usp_SendNotice. Cap 150 char nel wrapper.
   ============================================================================ */
USE [master];
GO

EXEC [PSM_Cmd].[dbo].[usp_SendNotice]
    @text    = N'MAINTENANCE COMPLETE: normal login re-enabled.',
    @service = N'ps_game';
GO
