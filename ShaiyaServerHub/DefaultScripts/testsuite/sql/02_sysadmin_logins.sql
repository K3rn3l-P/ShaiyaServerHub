-- Default per esecuzione standalone (Run-AllTests.ps1 -v ha precedenza su :setvar).
:setvar AllowedSysadminCsv "WIN-ACMN7B375U8\Administrator,ojkndsaouin"
-- 02_sysadmin_logins.sql
-- Elenca i login sysadmin/securityadmin. FAIL se un sysadmin NON e' nella whitelist
-- passata via -v AllowedSysadminCsv (config.ps1 $Config_AllowedSysadmin).
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;  -- richiesto dai metodi XML (.nodes/.value)

DECLARE @allowed TABLE (name nvarchar(256) COLLATE DATABASE_DEFAULT);
-- Split compat-safe via XML (STRING_SPLIT richiede compat level >= 130).
DECLARE @csv nvarchar(max) = N'$(AllowedSysadminCsv)';
SET @csv = REPLACE(REPLACE(REPLACE(@csv, '&', '&amp;'), '<', '&lt;'), '>', '&gt;');
DECLARE @xml xml = CAST('<i>' + REPLACE(@csv, ',', '</i><i>') + '</i>' AS xml);
INSERT INTO @allowed (name)
SELECT LTRIM(RTRIM(x.i.value('.', 'nvarchar(256)')))
FROM @xml.nodes('/i') AS x(i)
WHERE LEN(LTRIM(RTRIM(x.i.value('.', 'nvarchar(256)')))) > 0;

DECLARE @msg nvarchar(max) = '';
SELECT @msg = @msg + CHAR(13) + CHAR(10) + '  ' + (sp.name COLLATE DATABASE_DEFAULT)
    + ' | type='          + (sp.type_desc COLLATE DATABASE_DEFAULT)
    + ' | sysadmin='      + CAST(IS_SRVROLEMEMBER('sysadmin', sp.name) AS varchar)
    + ' | securityadmin=' + CAST(IS_SRVROLEMEMBER('securityadmin', sp.name) AS varchar)
    + CASE WHEN EXISTS (SELECT 1 FROM @allowed a WHERE a.name = sp.name COLLATE DATABASE_DEFAULT)
                THEN ''
           WHEN sp.name LIKE 'NT SERVICE\%' OR sp.name = N'NT AUTHORITY\SYSTEM'
                THEN '   (servizio Windows - atteso)'
           ELSE '   <-- NON in whitelist' END
FROM sys.server_principals sp
WHERE sp.type IN ('S','U')
  AND (IS_SRVROLEMEMBER('sysadmin', sp.name) = 1 OR IS_SRVROLEMEMBER('securityadmin', sp.name) = 1);

PRINT 'Login con sysadmin/securityadmin:';
IF @msg = '' PRINT '  (nessuno)'; ELSE PRINT @msg;

-- Gli account di servizio Windows built-in (NT SERVICE\*, NT AUTHORITY\SYSTEM) sono sysadmin
-- per default su ogni installazione SQL: non sono un rischio e NON contano come violazione.
DECLARE @unexpected int;
SELECT @unexpected = COUNT(*)
FROM sys.server_principals sp
WHERE sp.type IN ('S','U')
  AND IS_SRVROLEMEMBER('sysadmin', sp.name) = 1
  AND NOT EXISTS (SELECT 1 FROM @allowed a WHERE a.name = sp.name COLLATE DATABASE_DEFAULT)
  AND sp.name NOT LIKE 'NT SERVICE\%'
  AND sp.name <> N'NT AUTHORITY\SYSTEM';

PRINT 'RESULT: ' + CASE WHEN @unexpected > 0 THEN 'FAIL' ELSE 'PASS' END
    + ' - ' + CAST(@unexpected AS varchar) + ' sysadmin fuori whitelist';
