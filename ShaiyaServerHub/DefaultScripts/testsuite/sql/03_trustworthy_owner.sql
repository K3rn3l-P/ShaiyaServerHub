-- Default per esecuzione standalone (Run-AllTests.ps1 -v ha precedenza su :setvar).
:setvar TrustworthyAllowedCsv "PS_GameDefs"
-- 03_trustworthy_owner.sql
-- TRUSTWORTHY + owner per ogni DB utente.
-- FAIL  : TRUSTWORTHY=ON con owner sysadmin e DB NON in whitelist (catena privesc).
-- WARN  : TRUSTWORTHY=ON con owner sysadmin ma DB in whitelist (-v TrustworthyAllowedCsv).
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;  -- richiesto dai metodi XML (.nodes/.value)

DECLARE @allowed TABLE (name sysname);
-- Split compat-safe via XML (STRING_SPLIT richiede compat level >= 130).
DECLARE @csv nvarchar(max) = N'$(TrustworthyAllowedCsv)';
SET @csv = REPLACE(REPLACE(REPLACE(@csv, '&', '&amp;'), '<', '&lt;'), '>', '&gt;');
DECLARE @xml xml = CAST('<i>' + REPLACE(@csv, ',', '</i><i>') + '</i>' AS xml);
INSERT INTO @allowed (name)
SELECT LTRIM(RTRIM(x.i.value('.', 'nvarchar(128)')))
FROM @xml.nodes('/i') AS x(i)
WHERE LEN(LTRIM(RTRIM(x.i.value('.', 'nvarchar(128)')))) > 0;

DECLARE @msg nvarchar(max) = '';
SELECT @msg = @msg + CHAR(13) + CHAR(10) + '  ' + (d.name COLLATE DATABASE_DEFAULT)
    + ' | TRUSTWORTHY='   + CAST(d.is_trustworthy_on AS varchar)
    + ' | owner='         + ISNULL(SUSER_SNAME(d.owner_sid) COLLATE DATABASE_DEFAULT, '?')
    + ' | owner_sysadmin='+ CAST(ISNULL(IS_SRVROLEMEMBER('sysadmin', SUSER_SNAME(d.owner_sid)), 0) AS varchar)
FROM sys.databases d
WHERE d.database_id > 4;

PRINT 'Stato TRUSTWORTHY / owner per DB utente:';
PRINT @msg;

DECLARE @fail int, @warn int;
SELECT @fail = COUNT(*) FROM sys.databases d
WHERE d.database_id > 4 AND d.is_trustworthy_on = 1
  AND ISNULL(IS_SRVROLEMEMBER('sysadmin', SUSER_SNAME(d.owner_sid)), 0) = 1
  AND NOT EXISTS (SELECT 1 FROM @allowed a WHERE a.name = d.name COLLATE DATABASE_DEFAULT);

SELECT @warn = COUNT(*) FROM sys.databases d
WHERE d.database_id > 4 AND d.is_trustworthy_on = 1
  AND ISNULL(IS_SRVROLEMEMBER('sysadmin', SUSER_SNAME(d.owner_sid)), 0) = 1
  AND EXISTS (SELECT 1 FROM @allowed a WHERE a.name = d.name COLLATE DATABASE_DEFAULT);

PRINT 'RESULT: ' + CASE WHEN @fail > 0 THEN 'FAIL' WHEN @warn > 0 THEN 'WARN' ELSE 'PASS' END
    + ' - TRUSTWORTHY+owner-sysadmin: ' + CAST(@fail AS varchar) + ' fuori whitelist, '
    + CAST(@warn AS varchar) + ' ammessi';
