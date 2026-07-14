-- 14_guest_access.sql
-- CIS: l'utente 'guest' deve avere CONNECT REVOCATO nei DB utente (altrimenti chiunque abbia un
-- login al server puo' accedere al DB anche senza esservi mappato). FAIL se guest puo' connettersi.
-- (master/tempdb/msdb/model: guest e' di sistema, ignorati.)
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#guest') IS NOT NULL DROP TABLE #guest;
CREATE TABLE #guest (db sysname);

EXEC sp_MSforeachdb '
  IF DB_ID(''?'') > 4
  INSERT INTO #guest (db)
  SELECT ''?''
  FROM [?].sys.database_permissions p
  JOIN [?].sys.database_principals dp ON p.grantee_principal_id = dp.principal_id
  WHERE dp.name = ''guest''
    AND p.permission_name = ''CONNECT''
    AND p.state_desc = ''GRANT''';

PRINT 'DB utente con guest CONNECT abilitato:';
DECLARE @msg nvarchar(max) = '';
SELECT @msg = @msg + CHAR(13) + CHAR(10) + '  ' + db FROM #guest;
IF @msg = '' PRINT '  (nessuno - guest disabilitato ovunque)'; ELSE PRINT @msg;

DECLARE @n int = (SELECT COUNT(*) FROM #guest);
DROP TABLE #guest;

PRINT 'RESULT: ' + CASE WHEN @n > 0 THEN 'FAIL' ELSE 'PASS' END
    + ' - ' + CAST(@n AS varchar) + ' DB con guest CONNECT';
