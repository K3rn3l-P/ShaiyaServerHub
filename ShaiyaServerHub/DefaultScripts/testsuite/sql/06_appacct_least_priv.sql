-- Default per esecuzione standalone (Run-AllTests.ps1 -v ha precedenza su :setvar).
:setvar ExpectedAppLoginsCsv "Disadu@,Ernoweb@,S@o0#$h1908,Scr%1@be1422,Dr4s&t1c9686,Is0@te#d0217,Pus#h%c@rt7517,S4Ncu7@m0B3n3#,SecEventWriter"
-- 06_appacct_least_priv.sql
-- Gli account applicativi (-v ExpectedAppLoginsCsv) NON devono avere ruoli elevati
-- (db_owner / db_ddladmin / db_securityadmin) in nessun DB: solo ruoli custom MioRuolo*.
-- FAIL se uno di essi ha un ruolo elevato.
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#ap') IS NOT NULL DROP TABLE #ap;
CREATE TABLE #ap (db sysname, login sysname, role sysname);

EXEC sp_MSforeachdb '
  IF DB_ID(''?'') > 4
  INSERT INTO #ap (db, login, role)
  SELECT ''?'', m.name, r.name
  FROM [?].sys.database_role_members rm
  JOIN [?].sys.database_principals r ON rm.role_principal_id = r.principal_id
  JOIN [?].sys.database_principals m ON rm.member_principal_id = m.principal_id
  WHERE r.name IN (''db_owner'',''db_ddladmin'',''db_securityadmin'')
    AND m.name IN (SELECT LTRIM(RTRIM(value)) FROM STRING_SPLIT(N''$(ExpectedAppLoginsCsv)'', '','')
                   WHERE LEN(LTRIM(RTRIM(value))) > 0)';

PRINT 'Account app con ruoli elevati (dovrebbe essere VUOTO):';
DECLARE @msg nvarchar(max) = '';
SELECT @msg = @msg + CHAR(13) + CHAR(10) + '  ' + db + ' : ' + login + ' -> ' + role FROM #ap;
IF @msg = '' PRINT '  (nessuno - solo ruoli custom MioRuolo*)'; ELSE PRINT @msg;

DECLARE @n int = (SELECT COUNT(*) FROM #ap);
DROP TABLE #ap;

PRINT 'RESULT: ' + CASE WHEN @n > 0 THEN 'FAIL' ELSE 'PASS' END
    + ' - ' + CAST(@n AS varchar) + ' grant elevati su account app';
