/* ============================================================================
   3.Auto-EXP-Notice.sql  —  NUOVO canale PSM_Cmd
   /exp2xenable (ECONOMY) -> usp_RunCommand ; /nt -> usp_SendNotice.
   Eseguire come login ShaiyaTaskAgent (tier ECONOMY): serve per /exp2xenable.
   Richiede anche SELECT su PS_GameDefs.dbo.EXPTable per ShaiyaTaskAgent.
   ============================================================================ */
USE [PS_GameDefs];
GO

DECLARE @DayOfWeek     NVARCHAR(50);
DECLARE @EXPPercentage INT;
DECLARE @cmd           NVARCHAR(4000);
DECLARE @NoticeText    NVARCHAR(200);

-- Giorno corrente (inglese) + EXP corrispondente
SET @DayOfWeek = DATENAME(dw, GETDATE());

SELECT @EXPPercentage = EXP
FROM PS_GameDefs.dbo.EXPTable
WHERE NameDay = @DayOfWeek;

-- /exp2xenable <rate> -> wrapper con allowlist + tier + audit
SET @cmd = N'/exp2xenable ' + CAST(@EXPPercentage AS NVARCHAR(10));
EXEC [PSM_Cmd].[dbo].[usp_RunCommand]
    @service = N'ps_game',
    @command = @cmd;

-- Notice: NON anteporre "/nt" (lo fa il wrapper). Cap 150 char nel wrapper.
SET @NoticeText = N'Player, today is ' + @DayOfWeek
    + N'. EXP: x' + CAST(@EXPPercentage AS NVARCHAR(10)) + N'. Enjoy the game!';
EXEC [PSM_Cmd].[dbo].[usp_SendNotice]
    @text    = @NoticeText,
    @service = N'ps_game';
GO
