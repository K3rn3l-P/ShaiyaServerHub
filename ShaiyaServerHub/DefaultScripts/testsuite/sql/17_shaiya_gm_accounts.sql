-- Default per esecuzione standalone (Run-AllTests.ps1 -v ha precedenza su :setvar).
:setvar MaxGmAccounts "10"
-- 17_shaiya_gm_accounts.sql
-- Conta + elenca gli account staff in PS_UserData.dbo.Users_Master (Admin=1 OR AdminLevel>0).
-- Aiuto alla review periodica + indicatore di compromissione: un salto improvviso nel numero di account
-- admin = possibile auto-promozione (vedi catena DB-write). WARN se supera la soglia $(MaxGmAccounts).
SET NOCOUNT ON;

IF OBJECT_ID('PS_UserData.dbo.Users_Master') IS NULL
BEGIN PRINT 'RESULT: WARN - PS_UserData.dbo.Users_Master non trovata'; RETURN; END

DECLARE @maxGm int = TRY_CAST('$(MaxGmAccounts)' AS int);
IF @maxGm IS NULL SET @maxGm = 10;

DECLARE @cond nvarchar(200) = '';
IF COL_LENGTH('PS_UserData.dbo.Users_Master','Admin')      IS NOT NULL SET @cond = '[Admin] = 1';
IF COL_LENGTH('PS_UserData.dbo.Users_Master','AdminLevel') IS NOT NULL
    SET @cond = CASE WHEN @cond = '' THEN '' ELSE @cond + ' OR ' END + '[AdminLevel] > 0';
IF @cond = '' BEGIN PRINT 'RESULT: WARN - colonne Admin/AdminLevel assenti in Users_Master'; RETURN; END

DECLARE @sql nvarchar(max);
DECLARE @cnt int;
SET @sql = N'SELECT @c = COUNT(*) FROM PS_UserData.dbo.Users_Master WHERE ' + @cond;
EXEC sp_executesql @sql, N'@c int OUTPUT', @c = @cnt OUTPUT;

DECLARE @list nvarchar(max);
SET @sql = N'SELECT @l = STUFF((SELECT TOP 50 '', '' + CAST(UserID AS varchar(64))
             FROM PS_UserData.dbo.Users_Master WHERE ' + @cond + N' FOR XML PATH('''')), 1, 2, '''')';
EXEC sp_executesql @sql, N'@l nvarchar(max) OUTPUT', @l = @list OUTPUT;

PRINT 'Account GM/Admin (Admin=1 OR AdminLevel>0): ' + CAST(@cnt AS varchar);
IF @list IS NOT NULL PRINT '  ' + @list;

PRINT 'RESULT: ' + CASE WHEN @cnt > @maxGm THEN 'WARN' ELSE 'PASS' END
    + ' - ' + CAST(@cnt AS varchar) + ' account GM/Admin (soglia ' + CAST(@maxGm AS varchar) + ')';
