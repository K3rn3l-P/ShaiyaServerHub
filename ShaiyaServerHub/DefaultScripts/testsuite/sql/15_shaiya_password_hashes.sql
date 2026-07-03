-- 15_shaiya_password_hashes.sql
-- Shaiya data-at-rest: le password in PS_UserData.dbo.Users_Master devono essere PBKDF2
-- (prefisso 'pbkdf2sha256:'). WARN se esistono hash legacy (Argon2id) o, peggio, SHA1/plaintext.
-- Lega a verify_pbkdf2() in sdev-login/gm_security.cpp.
SET NOCOUNT ON;

IF OBJECT_ID('PS_UserData.dbo.Users_Master') IS NULL
BEGIN
    PRINT 'RESULT: WARN - PS_UserData.dbo.Users_Master non trovata (DB assente o nome diverso)';
    RETURN;
END

DECLARE @sql nvarchar(max) = N'
  SELECT
    @tot = COUNT(*),
    @pbkdf2 = SUM(CASE WHEN Enpassword LIKE ''pbkdf2sha256:%'' THEN 1 ELSE 0 END),
    @argon = SUM(CASE WHEN Enpassword LIKE ''$argon2%'' THEN 1 ELSE 0 END),
    @other = SUM(CASE WHEN Enpassword NOT LIKE ''pbkdf2sha256:%'' AND Enpassword NOT LIKE ''$argon2%''
                      AND Enpassword IS NOT NULL AND LEN(Enpassword) > 0 THEN 1 ELSE 0 END)
  FROM PS_UserData.dbo.Users_Master';

DECLARE @tot int, @pbkdf2 int, @argon int, @other int;
EXEC sp_executesql @sql,
    N'@tot int OUTPUT, @pbkdf2 int OUTPUT, @argon int OUTPUT, @other int OUTPUT',
    @tot OUTPUT, @pbkdf2 OUTPUT, @argon OUTPUT, @other OUTPUT;

PRINT 'Users_Master: totale=' + CAST(ISNULL(@tot,0) AS varchar)
    + ' | pbkdf2=' + CAST(ISNULL(@pbkdf2,0) AS varchar)
    + ' | argon2(legacy)=' + CAST(ISNULL(@argon,0) AS varchar)
    + ' | altro/plaintext=' + CAST(ISNULL(@other,0) AS varchar);

DECLARE @nonpbkdf2 int = ISNULL(@argon,0) + ISNULL(@other,0);
PRINT 'RESULT: ' + CASE WHEN @nonpbkdf2 > 0 THEN 'WARN' ELSE 'PASS' END
    + ' - ' + CAST(@nonpbkdf2 AS varchar) + ' password non-PBKDF2 (legacy/plaintext)';
