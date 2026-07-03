-- 13_sa_account.sql
-- CIS/STIG: l'account 'sa' (SID 0x01) andrebbe rinominato e/o disabilitato.
-- INFO/WARN: stampa nome reale del SID 0x01 + is_disabled (WARN se abilitato - qui ojkndsaouin lo e' di
--            proposito per gestione remota).
-- FAIL: esiste un login chiamato letteralmente 'sa' diverso dal SID 0x01, oppure un gruppo
--        BUILTIN\* / 'NT *' (non NT SERVICE) e' sysadmin.
SET NOCOUNT ON;

DECLARE @status varchar(10) = 'PASS', @detail varchar(400) = '';

-- 1) Login del SID 0x01 (il vero sa, anche se rinominato)
DECLARE @saName sysname, @saDisabled bit;
SELECT @saName = (name COLLATE DATABASE_DEFAULT), @saDisabled = is_disabled
FROM sys.server_principals WHERE sid = 0x01;
PRINT 'Login SID 0x01 (sa reale) = ' + ISNULL(@saName,'?')
    + ' | is_disabled=' + CAST(ISNULL(@saDisabled,0) AS varchar);
IF ISNULL(@saDisabled,0) = 0
BEGIN
    SET @status = 'WARN';
    SET @detail = @detail + 'sa(' + ISNULL(@saName,'?') + ') abilitato; ';
END

-- 2) Un login letteralmente 'sa' ma con SID diverso da 0x01 = sospetto (backdoor)
IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'sa' AND sid <> 0x01)
BEGIN
    SET @status = 'FAIL';
    SET @detail = @detail + 'esiste login ''sa'' con SID != 0x01 (sospetto); ';
END

-- 3) Gruppi BUILTIN\* o 'NT <gruppo>' (non NT SERVICE) come sysadmin = da rimuovere
DECLARE @builtins int = 0;
DECLARE @bmsg nvarchar(max) = '';
SELECT @bmsg = @bmsg + CHAR(13)+CHAR(10) + '  ' + (name COLLATE DATABASE_DEFAULT)
FROM sys.server_principals
WHERE IS_SRVROLEMEMBER('sysadmin', name) = 1
  AND (name LIKE 'BUILTIN\%' OR name = N'NT AUTHORITY\NETWORK SERVICE');
SELECT @builtins = COUNT(*) FROM sys.server_principals
WHERE IS_SRVROLEMEMBER('sysadmin', name) = 1
  AND (name LIKE 'BUILTIN\%' OR name = N'NT AUTHORITY\NETWORK SERVICE');
IF @builtins > 0
BEGIN
    PRINT 'Gruppi BUILTIN/NETWORK SERVICE con sysadmin:' + @bmsg;
    SET @status = 'FAIL';
    SET @detail = @detail + CAST(@builtins AS varchar) + ' gruppo/i BUILTIN sysadmin; ';
END

IF @detail = '' SET @detail = 'sa rinominato/gestito, nessun account sospetto';
PRINT 'RESULT: ' + @status + ' - ' + @detail;
