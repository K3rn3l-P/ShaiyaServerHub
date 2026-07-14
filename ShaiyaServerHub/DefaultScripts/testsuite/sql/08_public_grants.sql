-- 08_public_grants.sql
-- Permessi DML/EXECUTE concessi al ruolo "public" su oggetti UTENTE (non di sistema):
-- chiunque possa connettersi al DB li erediterebbe => potenziale leak. WARN se trovati.
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#pub') IS NOT NULL DROP TABLE #pub;
CREATE TABLE #pub (db sysname, obj nvarchar(300), perm sysname);

EXEC sp_MSforeachdb '
  IF DB_ID(''?'') > 4
  INSERT INTO #pub (db, obj, perm)
  SELECT ''?'',
         OBJECT_SCHEMA_NAME(p.major_id, DB_ID(''?'')) + ''.'' + OBJECT_NAME(p.major_id, DB_ID(''?'')),
         p.permission_name
  FROM [?].sys.database_permissions p
  JOIN [?].sys.database_principals dp ON p.grantee_principal_id = dp.principal_id
  JOIN [?].sys.objects o ON p.major_id = o.object_id
  WHERE dp.name = ''public''
    AND p.state_desc = ''GRANT''
    AND p.class = 1
    AND o.is_ms_shipped = 0
    AND OBJECT_NAME(p.major_id, DB_ID(''?'')) NOT LIKE ''dt[_]%''   -- proc dei Database Diagram (SSMS)
    AND OBJECT_NAME(p.major_id, DB_ID(''?'')) <> ''dtproperties''
    AND p.permission_name IN (''SELECT'',''INSERT'',''UPDATE'',''DELETE'',''EXECUTE'')';

PRINT 'Permessi concessi a PUBLIC su oggetti utente (potenziale leak):';
DECLARE @msg nvarchar(max) = '';
SELECT @msg = @msg + CHAR(13) + CHAR(10) + '  ' + db + ' : ' + obj + ' -> ' + perm FROM #pub;
IF @msg = '' PRINT '  (nessuno)'; ELSE PRINT @msg;

DECLARE @n int = (SELECT COUNT(*) FROM #pub);
DROP TABLE #pub;

PRINT 'RESULT: ' + CASE WHEN @n > 0 THEN 'WARN' ELSE 'PASS' END
    + ' - ' + CAST(@n AS varchar) + ' grant a public su oggetti utente';
