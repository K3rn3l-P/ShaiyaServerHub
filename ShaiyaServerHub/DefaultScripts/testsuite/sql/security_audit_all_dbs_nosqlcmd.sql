-- security_audit_all_dbs_nosqlcmd.sql
-- Audit diagnostico esteso (utenti/ruoli/permessi/moduli EXECUTE AS+dynamic SQL per ogni DB PS_*, flag
-- TRUSTWORTHY/db_chaining, superficie xp_cmdshell/OLE/CLR) - dump informativo PRINT/SELECT PIU' un
-- verdetto RESULT: calcolato in fondo.
-- FIX: le righe ":OUT C:\SecurityAudit_AllDBs.txt" / ":OUT STDOUT" originali (scritto per uso manuale in
-- SSMS con SQLCMD Mode) redirigevano TUTTO l'output verso un file locale fisso invece che su stdout -
-- per questo nella Test Suite risultava sempre "INFO" senza contenuto (lo stream catturato dal Hub era
-- vuoto). Rimosse: ora l'output arriva nel log della Test Suite come tutti gli altri script.
SET NOCOUNT ON;

DECLARE @DbPattern sysname = N'PS[_]%';  -- metti N'%' per tutti i DB utente

PRINT '=== CONTEXT ===';
SELECT
    @@SERVERNAME AS server_name,
    DB_NAME() AS current_db,
    SUSER_SNAME() AS executed_as_login,
    USER_NAME() AS executed_as_user;

PRINT '=== SERVER: SYSADMIN MEMBERS ===';
SELECT
    r.name AS server_role,
    m.name AS member_name,
    m.type_desc AS member_type,
    m.is_disabled
FROM sys.server_role_members srm
JOIN sys.server_principals r ON r.principal_id = srm.role_principal_id
JOIN sys.server_principals m ON m.principal_id = srm.member_principal_id
WHERE r.name = N'sysadmin'
ORDER BY m.name;

IF OBJECT_ID('tempdb..#DbUsers') IS NOT NULL DROP TABLE #DbUsers;
IF OBJECT_ID('tempdb..#DbRoles') IS NOT NULL DROP TABLE #DbRoles;
IF OBJECT_ID('tempdb..#DbRoleMembers') IS NOT NULL DROP TABLE #DbRoleMembers;
IF OBJECT_ID('tempdb..#DbPerms') IS NOT NULL DROP TABLE #DbPerms;
IF OBJECT_ID('tempdb..#DbModules') IS NOT NULL DROP TABLE #DbModules;

CREATE TABLE #DbUsers
(
    DatabaseName sysname,
    UserName sysname,
    TypeDesc nvarchar(60),
    AuthType nvarchar(60),
    DefaultSchema sysname NULL
);

CREATE TABLE #DbRoles
(
    DatabaseName sysname,
    RoleName sysname,
    IsFixedRole bit
);

CREATE TABLE #DbRoleMembers
(
    DatabaseName sysname,
    RoleName sysname,
    MemberName sysname
);

CREATE TABLE #DbPerms
(
    DatabaseName sysname,
    PrincipalName sysname,
    PrincipalType nvarchar(60),
    StateDesc nvarchar(60),
    PermissionName nvarchar(128),
    ClassDesc nvarchar(60),
    SecurableName nvarchar(512)
);

CREATE TABLE #DbModules
(
    DatabaseName sysname,
    SchemaName sysname,
    ModuleName sysname,
    TypeDesc nvarchar(60),
    HasExecuteAs bit,
    HasDynamicSql bit
);

DECLARE @db sysname, @sql nvarchar(max);

DECLARE c CURSOR LOCAL FAST_FORWARD FOR
SELECT name
FROM sys.databases
WHERE database_id > 4
  AND state_desc = 'ONLINE'
  AND name LIKE @DbPattern
ORDER BY name;

OPEN c;
FETCH NEXT FROM c INTO @db;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'
    INSERT INTO #DbUsers(DatabaseName, UserName, TypeDesc, AuthType, DefaultSchema)
    SELECT N''' + REPLACE(@db,'''','''''') + ''', dp.name, dp.type_desc, dp.authentication_type_desc, dp.default_schema_name
    FROM ' + QUOTENAME(@db) + N'.sys.database_principals dp
    WHERE dp.principal_id > 4
      AND dp.type IN (''S'',''U'',''G'',''E'',''X'')
      AND dp.name NOT IN (''guest'',''INFORMATION_SCHEMA'',''sys'');

    INSERT INTO #DbRoles(DatabaseName, RoleName, IsFixedRole)
    SELECT N''' + REPLACE(@db,'''','''''') + ''', dp.name, dp.is_fixed_role
    FROM ' + QUOTENAME(@db) + N'.sys.database_principals dp
    WHERE dp.type = ''R'';

    INSERT INTO #DbRoleMembers(DatabaseName, RoleName, MemberName)
    SELECT N''' + REPLACE(@db,'''','''''') + ''', r.name, m.name
    FROM ' + QUOTENAME(@db) + N'.sys.database_role_members drm
    JOIN ' + QUOTENAME(@db) + N'.sys.database_principals r ON r.principal_id = drm.role_principal_id
    JOIN ' + QUOTENAME(@db) + N'.sys.database_principals m ON m.principal_id = drm.member_principal_id;

    INSERT INTO #DbPerms(DatabaseName, PrincipalName, PrincipalType, StateDesc, PermissionName, ClassDesc, SecurableName)
    SELECT
        N''' + REPLACE(@db,'''','''''') + ''',
        p.name,
        p.type_desc,
        pe.state_desc,
        pe.permission_name,
        pe.class_desc,
        CASE pe.class_desc
            WHEN ''OBJECT_OR_COLUMN'' THEN
                ISNULL(OBJECT_SCHEMA_NAME(pe.major_id, DB_ID(N''' + REPLACE(@db,'''','''''') + ''')), ''?'') + ''.'' +
                ISNULL(OBJECT_NAME(pe.major_id, DB_ID(N''' + REPLACE(@db,'''','''''') + ''')), ''?'')
            WHEN ''SCHEMA'' THEN
                ISNULL(SCHEMA_NAME(pe.major_id), ''?'')
            WHEN ''DATABASE'' THEN N''DATABASE''
            ELSE CAST(pe.major_id AS nvarchar(100))
        END
    FROM ' + QUOTENAME(@db) + N'.sys.database_permissions pe
    JOIN ' + QUOTENAME(@db) + N'.sys.database_principals p ON p.principal_id = pe.grantee_principal_id
    LEFT JOIN ' + QUOTENAME(@db) + N'.sys.objects o ON o.object_id = pe.major_id
    LEFT JOIN ' + QUOTENAME(@db) + N'.sys.schemas s ON s.schema_id = o.schema_id
    WHERE p.principal_id > 4
      AND (o.object_id IS NULL OR o.is_ms_shipped = 0)
      AND (s.name IS NULL OR s.name NOT IN (''sys'',''INFORMATION_SCHEMA''));

    INSERT INTO #DbModules(DatabaseName, SchemaName, ModuleName, TypeDesc, HasExecuteAs, HasDynamicSql)
    SELECT
        N''' + REPLACE(@db,'''','''''') + ''',
        SCHEMA_NAME(o.schema_id),
        o.name,
        o.type_desc,
        CASE WHEN m.execute_as_principal_id IS NULL THEN 0 ELSE 1 END,
        CASE WHEN m.definition LIKE ''%sp_executesql%'' OR m.definition LIKE ''%EXEC(%'' OR m.definition LIKE ''%EXECUTE(%'' THEN 1 ELSE 0 END
    FROM ' + QUOTENAME(@db) + N'.sys.sql_modules m
    JOIN ' + QUOTENAME(@db) + N'.sys.objects o ON o.object_id = m.object_id
    WHERE o.is_ms_shipped = 0;
    ';
    EXEC sys.sp_executesql @sql;

    FETCH NEXT FROM c INTO @db;
END

CLOSE c;
DEALLOCATE c;

PRINT '=== DB USERS (ALL TARGET DBs) ===';
SELECT * FROM #DbUsers ORDER BY DatabaseName, UserName;

PRINT '=== CUSTOM ROLES ONLY ===';
SELECT * FROM #DbRoles WHERE IsFixedRole = 0 ORDER BY DatabaseName, RoleName;

PRINT '=== ROLE MEMBERS (ALL) ===';
SELECT * FROM #DbRoleMembers ORDER BY DatabaseName, RoleName, MemberName;

PRINT '=== EXPLICIT PERMISSIONS (NON SYSTEM OBJECTS) ===';
SELECT * FROM #DbPerms ORDER BY DatabaseName, PrincipalName, PermissionName;

PRINT '=== MODULES WITH EXECUTE AS OR DYNAMIC SQL ===';
SELECT *
FROM #DbModules
WHERE HasExecuteAs = 1 OR HasDynamicSql = 1
ORDER BY DatabaseName, SchemaName, ModuleName;

PRINT '=== DB FLAGS ===';
SELECT
    name,
    is_trustworthy_on,
    is_db_chaining_on,
    containment_desc
FROM sys.databases
WHERE database_id > 4
  AND name LIKE @DbPattern
ORDER BY name;

PRINT '=== INSTANCE SURFACE AREA ===';
SELECT name, value_in_use
FROM sys.configurations
WHERE name IN (N'xp_cmdshell',N'Ole Automation Procedures',N'clr enabled')
ORDER BY name;

-- ── Verdetto (nuovo — mancava nello script originale) ──
DECLARE @status varchar(10) = 'PASS', @detail varchar(1000) = '';

-- sysadmin oltre l'insieme noto (Administrator locale, ojkndsaouin = sa rinominato, service account SQL)
DECLARE @unexpectedSysadmin int;
SELECT @unexpectedSysadmin = COUNT(*)
FROM sys.server_role_members srm
JOIN sys.server_principals r ON r.principal_id = srm.role_principal_id
JOIN sys.server_principals m ON m.principal_id = srm.member_principal_id
WHERE r.name = N'sysadmin'
  AND m.name NOT LIKE '%\Administrator'
  AND m.name <> 'ojkndsaouin'
  AND m.name NOT LIKE 'NT SERVICE\%'
  AND m.name NOT LIKE 'NT AUTHORITY\%'
  AND m.name <> 'sa';
IF @unexpectedSysadmin > 0
BEGIN
    SET @status = 'WARN';
    SET @detail = @detail + CAST(@unexpectedSysadmin AS varchar) + ' membro/i sysadmin inatteso/i; ';
END

-- TRUSTWORTHY / db_chaining su un DB PS_* = anti-pattern noto
IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id > 4 AND name LIKE @DbPattern AND (is_trustworthy_on = 1 OR is_db_chaining_on = 1))
BEGIN
    SET @status = 'WARN';
    SET @detail = @detail + 'TRUSTWORTHY o db_chaining attivo su un DB PS_*; ';
END

-- xp_cmdshell / OLE Automation abilitati = superficie di attacco inutile per questo server (CLR e'
-- volutamente escluso: serve a PSM_Cmd, atteso ON - vedi 07_clr_assemblies.sql per il controllo dedicato)
IF EXISTS (SELECT 1 FROM sys.configurations WHERE name IN (N'xp_cmdshell', N'Ole Automation Procedures') AND value_in_use = 1)
BEGIN
    SET @status = 'WARN';
    SET @detail = @detail + 'xp_cmdshell o OLE Automation Procedures abilitati; ';
END

IF @detail = '' SET @detail = 'nessun anti-pattern noto (sysadmin/TRUSTWORTHY/xp_cmdshell/OLE) sui DB PS_*';
PRINT 'RESULT: ' + @status + ' - ' + @detail;
