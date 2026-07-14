-- Default per esecuzione standalone (Run-AllTests.ps1 -v ha precedenza su :setvar).
:setvar BackupMaxAgeDays "7"
-- 18_backups_recent.sql
-- Resilienza (anti-ransom/distruzione): ogni DB utente deve avere un full backup recente.
-- WARN se un DB non ha mai un full backup o l'ultimo e' piu' vecchio di $(BackupMaxAgeDays) giorni.
SET NOCOUNT ON;

DECLARE @maxAge int = TRY_CAST('$(BackupMaxAgeDays)' AS int);
IF @maxAge IS NULL SET @maxAge = 7;

IF OBJECT_ID('tempdb..#bk') IS NOT NULL DROP TABLE #bk;
CREATE TABLE #bk (db sysname, last_full datetime NULL, age_days int NULL);

INSERT INTO #bk (db, last_full)
SELECT (d.name COLLATE DATABASE_DEFAULT),
       (SELECT MAX(b.backup_finish_date) FROM msdb.dbo.backupset b
        WHERE b.database_name = (d.name COLLATE DATABASE_DEFAULT) AND b.type = 'D')
FROM sys.databases d
WHERE d.database_id > 4 AND d.state = 0;   -- solo DB utente ONLINE

UPDATE #bk SET age_days = DATEDIFF(DAY, last_full, GETDATE()) WHERE last_full IS NOT NULL;

PRINT 'Ultimo full backup per DB utente (soglia ' + CAST(@maxAge AS varchar) + ' giorni):';
DECLARE @msg nvarchar(max) = '';
SELECT @msg = @msg + CHAR(13) + CHAR(10) + '  ' + db + ' : '
    + CASE WHEN last_full IS NULL THEN 'MAI'
           ELSE CONVERT(varchar(19), last_full, 120) + ' (' + CAST(age_days AS varchar) + 'g)' END
FROM #bk;
PRINT @msg;

DECLARE @bad int = (SELECT COUNT(*) FROM #bk WHERE last_full IS NULL OR age_days > @maxAge);
DROP TABLE #bk;

PRINT 'RESULT: ' + CASE WHEN @bad > 0 THEN 'WARN' ELSE 'PASS' END
    + ' - ' + CAST(@bad AS varchar) + ' DB senza backup full recente';
