-- 19_sql_build_patch.sql
-- Esposizione CVE/KEV: riporta versione/patch dell'istanza SQL. WARN se nessuna CU applicata
-- (ProductLevel='RTM' o ProductUpdateLevel NULL) -> istanza potenzialmente non patchata.
SET NOCOUNT ON;

DECLARE @ver   varchar(64)  = CAST(SERVERPROPERTY('ProductVersion')     AS varchar(64));
DECLARE @lvl   varchar(64)  = CAST(SERVERPROPERTY('ProductLevel')       AS varchar(64));
DECLARE @upd   varchar(64)  = CAST(ISNULL(SERVERPROPERTY('ProductUpdateLevel'),'') AS varchar(64));
DECLARE @ed    varchar(128) = CAST(SERVERPROPERTY('Edition')            AS varchar(128));

PRINT 'ProductVersion     = ' + ISNULL(@ver,'?');
PRINT 'ProductLevel       = ' + ISNULL(@lvl,'?') + ' (RTM = nessun Service Pack)';
PRINT 'ProductUpdateLevel = ' + CASE WHEN @upd = '' THEN '(nessuna CU)' ELSE @upd END;
PRINT 'Edition            = ' + ISNULL(@ed,'?');

DECLARE @status varchar(10) = 'PASS', @detail varchar(300);
IF @upd = '' OR @lvl = 'RTM'
BEGIN
    SET @status = 'WARN';
    SET @detail = 'istanza senza CU/SP applicata (' + ISNULL(@ver,'?') + ' ' + ISNULL(@lvl,'?')
                + ') - verificare ultime patch (CVE/KEV)';
END
ELSE
    SET @detail = 'build ' + ISNULL(@ver,'?') + ' ' + ISNULL(@lvl,'?') + ' ' + @upd;

PRINT 'RESULT: ' + @status + ' - ' + @detail;
