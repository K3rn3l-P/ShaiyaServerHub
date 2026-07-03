-- 01_surface_config.sql
-- Superficie d'attacco a livello istanza (CIS SQL Server Benchmark - surface area).
-- FAIL: xp_cmdshell / OLE Automation / Ad Hoc Distributed Queries / Database Mail XPs /
--       cross db ownership chaining = 1.
-- WARN: clr strict security <> 1.
-- INFO: clr enabled, remote admin connections, LoginMode (riportati, non valutati).
SET NOCOUNT ON;

DECLARE @status varchar(10) = 'PASS', @detail varchar(600) = '';
DECLARE @lm int;

-- Helper: legge un valore di sys.configurations
DECLARE @xp int, @ole int, @adhoc int, @dbmail int, @crossdb int, @clrstrict int, @clren int, @rac int;
SELECT @xp       = CAST(value AS int) FROM sys.configurations WHERE name = 'xp_cmdshell';
SELECT @ole      = CAST(value AS int) FROM sys.configurations WHERE name = 'Ole Automation Procedures';
SELECT @adhoc    = CAST(value AS int) FROM sys.configurations WHERE name = 'Ad Hoc Distributed Queries';
SELECT @dbmail   = CAST(value AS int) FROM sys.configurations WHERE name = 'Database Mail XPs';
SELECT @crossdb  = CAST(value AS int) FROM sys.configurations WHERE name = 'cross db ownership chaining';
SELECT @clrstrict= CAST(value AS int) FROM sys.configurations WHERE name = 'clr strict security';
SELECT @clren    = CAST(value AS int) FROM sys.configurations WHERE name = 'clr enabled';
SELECT @rac      = CAST(value AS int) FROM sys.configurations WHERE name = 'remote admin connections';

BEGIN TRY
    EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE',
        N'Software\Microsoft\MSSQLServer\MSSQLServer', N'LoginMode', @lm OUTPUT;
END TRY
BEGIN CATCH SET @lm = -1; END CATCH

PRINT 'xp_cmdshell                 = ' + CAST(ISNULL(@xp,-1)       AS varchar);
PRINT 'Ole Automation Procedures   = ' + CAST(ISNULL(@ole,-1)      AS varchar);
PRINT 'Ad Hoc Distributed Queries  = ' + CAST(ISNULL(@adhoc,-1)    AS varchar);
PRINT 'Database Mail XPs           = ' + CAST(ISNULL(@dbmail,-1)    AS varchar);
PRINT 'cross db ownership chaining = ' + CAST(ISNULL(@crossdb,-1)  AS varchar);
PRINT 'clr strict security         = ' + CAST(ISNULL(@clrstrict,-1)AS varchar);
PRINT 'clr enabled                 = ' + CAST(ISNULL(@clren,-1)    AS varchar) + ' (info: 1 atteso per PSMagent)';
PRINT 'remote admin connections    = ' + CAST(ISNULL(@rac,-1)      AS varchar) + ' (info)';
PRINT 'LoginMode                   = ' + CAST(ISNULL(@lm,-1)       AS varchar) + ' (1=Windows-only, 2=Mixed)';

IF ISNULL(@xp,0)      <> 0 BEGIN SET @status='FAIL'; SET @detail=@detail+'xp_cmdshell ATTIVO; '; END
IF ISNULL(@ole,0)     <> 0 BEGIN SET @status='FAIL'; SET @detail=@detail+'OLE Automation ATTIVO; '; END
IF ISNULL(@adhoc,0)   <> 0 BEGIN SET @status='FAIL'; SET @detail=@detail+'Ad Hoc Distributed Queries ATTIVO; '; END
IF ISNULL(@dbmail,0)  <> 0 BEGIN SET @status='FAIL'; SET @detail=@detail+'Database Mail XPs ATTIVO; '; END
IF ISNULL(@crossdb,0) <> 0 BEGIN SET @status='FAIL'; SET @detail=@detail+'cross db ownership chaining ATTIVO; '; END
IF ISNULL(@clrstrict,1) <> 1 BEGIN IF @status <> 'FAIL' SET @status='WARN'; SET @detail=@detail+'clr strict security disattivo; '; END

IF @detail = '' SET @detail = 'superficie d''attacco minimizzata';
PRINT 'RESULT: ' + @status + ' - ' + @detail;
