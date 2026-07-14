-- 23_sql_audit_install.sql
-- Installa/ripara il SQL Server Audit nativo usato da SqlAuditSensor (config-drift database + login
-- SQL falliti a livello server) - TO APPLICATION_LOG (Get-WinEvent, stesso pattern degli altri sensori
-- Event Log, nessun file .sqlaudit da gestire). Idempotente: sicuro da rilanciare, ripara se un audit
-- esiste ma e' stato disabilitato (STATE=OFF) invece di ricrearlo da zero.
-- INFO: creato/riattivato ora.  PASS: gia' installato e attivo.  WARN/FAIL: non dovrebbero accadere
-- (richiede sysadmin - la Test Suite gira sempre in Windows auth, ha gia' i permessi).
SET NOCOUNT ON;
USE master;

DECLARE @status varchar(10) = 'PASS', @detail varchar(400) = '';

-- LoginAudit (FAILED_LOGIN_GROUP)
IF NOT EXISTS (SELECT * FROM sys.server_audits WHERE name = 'ShaiyaHub_LoginAudit')
BEGIN
    CREATE SERVER AUDIT ShaiyaHub_LoginAudit TO APPLICATION_LOG WITH (QUEUE_DELAY = 1000);
    ALTER SERVER AUDIT ShaiyaHub_LoginAudit WITH (STATE = ON);
    SET @status = 'INFO'; SET @detail = @detail + 'LoginAudit creato; ';
END
ELSE IF EXISTS (SELECT * FROM sys.server_audits WHERE name = 'ShaiyaHub_LoginAudit' AND is_state_enabled = 0)
BEGIN
    ALTER SERVER AUDIT ShaiyaHub_LoginAudit WITH (STATE = ON);
    SET @status = 'INFO'; SET @detail = @detail + 'LoginAudit era disattivato, riattivato; ';
END

IF NOT EXISTS (SELECT * FROM sys.server_audit_specifications WHERE name = 'ShaiyaHub_LoginAuditSpec')
BEGIN
    CREATE SERVER AUDIT SPECIFICATION ShaiyaHub_LoginAuditSpec
    FOR SERVER AUDIT ShaiyaHub_LoginAudit
    ADD (FAILED_LOGIN_GROUP)
    WITH (STATE = ON);
    SET @status = 'INFO'; SET @detail = @detail + 'LoginAuditSpec creata; ';
END
ELSE IF EXISTS (SELECT * FROM sys.server_audit_specifications WHERE name = 'ShaiyaHub_LoginAuditSpec' AND is_state_enabled = 0)
BEGIN
    ALTER SERVER AUDIT SPECIFICATION ShaiyaHub_LoginAuditSpec WITH (STATE = ON);
    SET @status = 'INFO'; SET @detail = @detail + 'LoginAuditSpec riattivata; ';
END

-- SchemaChangesAudit (SCHEMA_OBJECT_CHANGE_GROUP)
IF NOT EXISTS (SELECT * FROM sys.server_audits WHERE name = 'ShaiyaHub_SchemaChangesAudit')
BEGIN
    CREATE SERVER AUDIT ShaiyaHub_SchemaChangesAudit TO APPLICATION_LOG WITH (QUEUE_DELAY = 1000);
    ALTER SERVER AUDIT ShaiyaHub_SchemaChangesAudit WITH (STATE = ON);
    SET @status = 'INFO'; SET @detail = @detail + 'SchemaChangesAudit creato; ';
END
ELSE IF EXISTS (SELECT * FROM sys.server_audits WHERE name = 'ShaiyaHub_SchemaChangesAudit' AND is_state_enabled = 0)
BEGIN
    ALTER SERVER AUDIT ShaiyaHub_SchemaChangesAudit WITH (STATE = ON);
    SET @status = 'INFO'; SET @detail = @detail + 'SchemaChangesAudit era disattivato, riattivato; ';
END

IF NOT EXISTS (SELECT * FROM sys.server_audit_specifications WHERE name = 'ShaiyaHub_SchemaChangesSpec')
BEGIN
    CREATE SERVER AUDIT SPECIFICATION ShaiyaHub_SchemaChangesSpec
    FOR SERVER AUDIT ShaiyaHub_SchemaChangesAudit
    ADD (SCHEMA_OBJECT_CHANGE_GROUP)
    WITH (STATE = ON);
    SET @status = 'INFO'; SET @detail = @detail + 'SchemaChangesSpec creata; ';
END
ELSE IF EXISTS (SELECT * FROM sys.server_audit_specifications WHERE name = 'ShaiyaHub_SchemaChangesSpec' AND is_state_enabled = 0)
BEGIN
    ALTER SERVER AUDIT SPECIFICATION ShaiyaHub_SchemaChangesSpec WITH (STATE = ON);
    SET @status = 'INFO'; SET @detail = @detail + 'SchemaChangesSpec riattivata; ';
END

IF @detail = '' SET @detail = 'entrambi gli audit (login/schema) gia'' installati e attivi';
PRINT 'RESULT: ' + @status + ' - ' + @detail;
