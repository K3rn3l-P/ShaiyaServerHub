-- 05_privesc_check.sql
-- Catena di privilege-escalation: un login VIVO non-sysadmin che e' db_owner/db_ddladmin
-- dentro un DB TRUSTWORTHY puo' creare proc WITH EXECUTE AS OWNER ed escalare a sysadmin
-- (se l'owner del DB e' sysadmin). FAIL se trova anche un solo principal sfruttabile.
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..##pe') IS NOT NULL DROP TABLE ##pe;
CREATE TABLE ##pe (db sysname, principal sysname, why varchar(100));

EXEC sp_MSforeachdb '
  IF DB_ID(''?'') > 4 AND (SELECT is_trustworthy_on FROM sys.databases WHERE name = ''?'') = 1
  INSERT INTO ##pe (db, principal, why)
  SELECT ''?'', m.name, ''role:'' + r.name
  FROM [?].sys.database_role_members rm
  JOIN [?].sys.database_principals r ON rm.role_principal_id = r.principal_id
  JOIN [?].sys.database_principals m ON rm.member_principal_id = m.principal_id
  WHERE r.name IN (''db_owner'',''db_ddladmin'')
    AND m.authentication_type_desc = ''INSTANCE''
    AND SUSER_SNAME(m.sid) IS NOT NULL
    AND ISNULL(IS_SRVROLEMEMBER(''sysadmin'', SUSER_SNAME(m.sid)), 0) = 0';

PRINT 'Login non-sysadmin con db_owner/ddladmin su DB TRUSTWORTHY (catena privesc):';
DECLARE @msg nvarchar(max) = '';
SELECT @msg = @msg + CHAR(13) + CHAR(10) + '  ' + db + ' : ' + principal + ' (' + why + ')' FROM ##pe;
IF @msg = '' PRINT '  (nessuno)'; ELSE PRINT @msg;

DECLARE @n int = (SELECT COUNT(*) FROM ##pe);
DROP TABLE ##pe;

PRINT 'RESULT: ' + CASE WHEN @n > 0 THEN 'FAIL' ELSE 'PASS' END
    + ' - ' + CAST(@n AS varchar) + ' principal sfruttabili';
