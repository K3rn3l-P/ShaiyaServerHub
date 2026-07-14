-- 07_clr_assemblies.sql
-- Elenca gli assembly CLR utente. WARN se EXTERNAL_ACCESS/UNSAFE (rischiosi, specie se
-- abilitati via TRUSTWORTHY invece che firma con certificato).
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#asm') IS NOT NULL DROP TABLE #asm;
CREATE TABLE #asm (db sysname, asm sysname, perm sysname, tw bit);

EXEC sp_MSforeachdb '
  IF DB_ID(''?'') > 4
  INSERT INTO #asm (db, asm, perm, tw)
  SELECT ''?'', a.name, a.permission_set_desc,
         (SELECT is_trustworthy_on FROM sys.databases WHERE name = ''?'')
  FROM [?].sys.assemblies a
  WHERE a.is_user_defined = 1';

PRINT 'Assembly CLR utente:';
DECLARE @msg nvarchar(max) = '';
SELECT @msg = @msg + CHAR(13) + CHAR(10) + '  ' + db + ' : ' + asm
    + ' | ' + perm + ' | trustworthy_db=' + CAST(tw AS varchar) FROM #asm;
IF @msg = '' PRINT '  (nessuno)'; ELSE PRINT @msg;

DECLARE @risky int = (SELECT COUNT(*) FROM #asm WHERE perm IN ('EXTERNAL_ACCESS','UNSAFE'));
DROP TABLE #asm;

PRINT 'RESULT: ' + CASE WHEN @risky > 0 THEN 'WARN' ELSE 'PASS' END
    + ' - ' + CAST(@risky AS varchar) + ' assembly EXTERNAL_ACCESS/UNSAFE';
