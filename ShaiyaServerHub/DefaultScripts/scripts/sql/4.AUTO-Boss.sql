/* ============================================================================
   4.AUTO-Boss.sql  —  NUOVO canale PSM_Cmd
   /mmake (CUSTOM sdev.dll) -> usp_RunCommand ; /nt -> usp_SendNotice.
   Prerequisiti:
     - allowlist: /mmake deve essere Enabled=1 (vedi 03_wrappers_allowlist.sql).
     - login ShaiyaTaskAgent (tier ECONOMY) con EXECUTE su usp_RunCommand/usp_SendNotice
       e SELECT su PS_GameDefs.dbo.MapNames / Mobs.
   ============================================================================ */
USE [PS_GameDefs];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-------------------------------------------------------
-- 1. Variabili e tabella di mapping coord. per ogni mappa
-------------------------------------------------------
DECLARE
    @MapId        INT,
    @MapName      NVARCHAR(255),
    @MobIds       NVARCHAR(MAX),
    @MobNames     NVARCHAR(MAX),
    @Count        INT = 1,       -- sempre 1 per spawn
    @X            FLOAT,
    @Y            FLOAT,
    @Z            FLOAT,
    @NumTypes     INT;           -- quanti tipi diversi spawnare

-- Tabella di coordinate testate
DECLARE @Coords TABLE (
    MapId INT PRIMARY KEY,
    X     FLOAT, Y FLOAT, Z FLOAT
);
INSERT INTO @Coords(MapId,X,Y,Z) VALUES
(0,   845,  1,   948),
(35,  480, 27,  403),
(36,  508, 43,  532),
(45,  269, 16,  669),
(46,  253, 48,  783),
(47,  811, 76, 1603),
(68, 1793, 67, 1804),
(69,  535, 20,  804),
(70,  791, 25, 1200);

-------------------------------------------------------
-- 2. Scelta casuale di mappa
-------------------------------------------------------
SELECT TOP 1
    @MapId   = MapID,
    @MapName = MapName
FROM PS_GameDefs.dbo.MapNames
WHERE MapID IN (0,35,36,45,46,47,68,69,70)
ORDER BY NEWID();

-------------------------------------------------------
-- 3. Numero di tipi di boss da spawnare (1..3)
-------------------------------------------------------
SET @NumTypes = FLOOR(RAND(CHECKSUM(NEWID())) * 3) + 1;

-------------------------------------------------------
-- 4. Lista fissa di MobID e scelta casuale TOP(@NumTypes)
-------------------------------------------------------
DECLARE @AllowedMobIds TABLE (MobID INT PRIMARY KEY);
INSERT INTO @AllowedMobIds(MobID) VALUES
(2469),(835),(1259),(1975),
(1974),(1716),(1816),(3573),
(3572),(2785),(2839),(2821),
(2803),(3019),(3015),(3023),
(3028),(3010),(3005),(3042),
(3539),(871),(872),(1700),
(1702),(1703),(1704),(1705),
(1706),(1707),(901),(902),
(1800),(1802),(1803),(3043),
(3576),(1804),(1805),(1806),
(1807),(3562),(3537),(3081),
(3083),(2950),(2983);

;WITH RandomMobs AS (
    SELECT TOP (@NumTypes) MobID
    FROM @AllowedMobIds
    ORDER BY NEWID()
)
SELECT @MobIds = STRING_AGG(CAST(MobID AS NVARCHAR(10)), ',')
FROM RandomMobs;

-------------------------------------------------------
-- 5. Recupero coordinate base per la mappa scelta
-------------------------------------------------------
SELECT
    @X = X,
    @Y = Y,
    @Z = Z
FROM @Coords
WHERE MapId = @MapId;

-------------------------------------------------------
-- 6. Split di @MobIds via XML e loop di spawn (count=1)
-------------------------------------------------------
DECLARE
    @i            INT = 1,
    @MobId        NVARCHAR(10),
    @CurrX        FLOAT,
    @CurrZ        FLOAT,
    @SpawnCommand NVARCHAR(MAX);

DECLARE @MobTable TABLE (rn INT IDENTITY(1,1), MobID NVARCHAR(10));
;WITH XmlData AS (
    SELECT CAST('<i>' + REPLACE(@MobIds, ',', '</i><i>') + '</i>' AS XML) AS xm
)
INSERT INTO @MobTable(MobID)
SELECT x.i.value('.', 'NVARCHAR(10)')
FROM XmlData
CROSS APPLY xm.nodes('/i') AS x(i);

WHILE @i <= (SELECT COUNT(*) FROM @MobTable)
BEGIN
    SELECT @MobId = MobID FROM @MobTable WHERE rn = @i;

    -- Sposta ciascun mob di +5*(i-1) su X e Z
    SET @CurrX = @X + 5 * (@i - 1);
    SET @CurrZ = @Z + 5 * (@i - 1);

    SET @SpawnCommand = '/mmake '
        + CAST(@MapId   AS NVARCHAR(10)) + ' '
        + @MobId + ' '
        + CAST(@Count   AS NVARCHAR(10)) + ' '
        + CAST(@CurrX   AS NVARCHAR(10)) + ' '
        + CAST(@Y       AS NVARCHAR(10)) + ' '
        + CAST(@CurrZ   AS NVARCHAR(10));

    -- /mmake -> wrapper con allowlist (/mmake Enabled=1) + tier + audit
    EXEC [PSM_Cmd].[dbo].[usp_RunCommand]
        @service = N'ps_game',
        @command = @SpawnCommand;

    SET @i = @i + 1;
END;

-------------------------------------------------------
-- 7. Notifica /nt [AUTO EVENT]  (via usp_SendNotice, cap 150 char)
-------------------------------------------------------
SELECT
    @MobNames = STUFF((
        SELECT ', ' + M.MobName
        FROM PS_GameDefs.dbo.Mobs M
        WHERE CHARINDEX(',' + CAST(M.MobID AS NVARCHAR(10)) + ',', ',' + @MobIds + ',') > 0
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

-- NON anteporre "/nt": lo fa il wrapper. ATTENZIONE: usp_SendNotice taglia a 150 char,
-- con molti boss il messaggio potrebbe risultare troncato.
DECLARE @NoticeText NVARCHAR(200) =
    '[AUTO EVENT] Bosses "' + @MobNames
    + '" spawned on ' + @MapName + ' - available for 1 hour!';

EXEC [PSM_Cmd].[dbo].[usp_SendNotice]
    @text    = @NoticeText,
    @service = N'ps_game';
GO
