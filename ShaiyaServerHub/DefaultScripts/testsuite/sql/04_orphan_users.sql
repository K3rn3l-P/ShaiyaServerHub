-- 04_orphan_users.sql
-- Trova utenti di database "orfani" (mappati a un login per SID, ma il login non esiste piu').
-- Es. il vecchio utente 'Shaiya'. FAIL se ne trova.
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..##orphans') IS NOT NULL DROP TABLE ##orphans;
CREATE TABLE ##orphans (db sysname, usr sysname);

EXEC sp_MSforeachdb '
  IF DB_ID(''?'') > 4
  INSERT INTO ##orphans (db, usr)
  SELECT ''?'', name
  FROM [?].sys.database_principals
  WHERE authentication_type_desc = ''INSTANCE''
    AND SUSER_SNAME(sid) IS NULL
    AND name NOT IN (''dbo'',''guest'',''sys'',''INFORMATION_SCHEMA'')';

PRINT 'Utenti di database orfani (login mancante):';
DECLARE @msg nvarchar(max) = '';
SELECT @msg = @msg + CHAR(13) + CHAR(10) + '  ' + db + ' : ' + usr FROM ##orphans;
IF @msg = '' PRINT '  (nessuno)'; ELSE PRINT @msg;

DECLARE @n int = (SELECT COUNT(*) FROM ##orphans);
DROP TABLE ##orphans;

PRINT 'RESULT: ' + CASE WHEN @n > 0 THEN 'FAIL' ELSE 'PASS' END
    + ' - ' + CAST(@n AS varchar) + ' utenti orfani';
