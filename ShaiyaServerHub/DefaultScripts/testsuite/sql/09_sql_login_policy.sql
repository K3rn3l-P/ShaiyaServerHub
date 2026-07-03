-- 09_sql_login_policy.sql
-- CIS: i login SQL (autenticazione SQL) devono avere CHECK_POLICY attivo (is_policy_checked=1)
-- per resistere a brute-force. WARN se qualche login SQL abilitato ha la policy disattivata.
SET NOCOUNT ON;

DECLARE @msg nvarchar(max) = '';
SELECT @msg = @msg + CHAR(13) + CHAR(10) + '  ' + (sl.name COLLATE DATABASE_DEFAULT)
    + ' | policy_checked=' + CAST(sl.is_policy_checked AS varchar)
    + ' | expiration_checked=' + CAST(sl.is_expiration_checked AS varchar)
FROM sys.sql_logins sl
WHERE sl.is_disabled = 0
  AND sl.name NOT LIKE '##%'
  AND sl.is_policy_checked = 0;

PRINT 'Login SQL abilitati senza CHECK_POLICY:';
IF @msg = '' PRINT '  (nessuno - tutti con policy attiva)'; ELSE PRINT @msg;

DECLARE @n int;
SELECT @n = COUNT(*) FROM sys.sql_logins
WHERE is_disabled = 0 AND name NOT LIKE '##%' AND is_policy_checked = 0;

PRINT 'RESULT: ' + CASE WHEN @n > 0 THEN 'WARN' ELSE 'PASS' END
    + ' - ' + CAST(@n AS varchar) + ' login SQL senza CHECK_POLICY';
