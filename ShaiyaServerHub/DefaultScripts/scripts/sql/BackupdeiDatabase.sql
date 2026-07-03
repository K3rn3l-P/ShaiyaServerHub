USE master;
GO

-- 0. Abilita QUOTED_IDENTIFIER e ANSI_NULLS
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 0.1  Abilita xp_cmdshell
EXEC sp_configure 'show advanced options', 1;
RECONFIGURE WITH OVERRIDE;
EXEC sp_configure 'xp_cmdshell', 1;
RECONFIGURE WITH OVERRIDE;
GO

-- 1. Script SQL per il backup dei database
DECLARE @BackupPath       NVARCHAR(500),
        @DatabaseName     NVARCHAR(500),
        @BackupFileName   NVARCHAR(1000),
        @CurrentDateTime  NVARCHAR(50),
        @Command          NVARCHAR(1000),
        @FolderExists     INT;

-- Percorso di backup
SET @BackupPath = 'F:\BackupAutomatici\AutoBackUp\NEW\';

-- Timestamp
SET @CurrentDateTime = FORMAT(GETDATE(), 'dd-MM-yyyy-HH.mm.ss');

-- Controlla se la cartella esiste
EXEC xp_fileexist @BackupPath, @FolderExists OUTPUT;

-- Se non esiste, la crea
IF @FolderExists = 0
BEGIN
    EXEC xp_cmdshell 'mkdir "F:\BackupAutomatici\AutoBackUp\NEW"';
END

-- 2. Backup di ciascun database online (escluse di sistema)
DECLARE db_cursor CURSOR FOR
SELECT name
FROM sys.databases
WHERE state_desc = 'ONLINE'
  AND name NOT IN ('master','tempdb','model','msdb');

OPEN db_cursor;
FETCH NEXT FROM db_cursor INTO @DatabaseName;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Nome file di backup
    SET @BackupFileName = @BackupPath 
        + @DatabaseName + '_' + @CurrentDateTime + '.bak';

    -- Esegui il BACKUP
    SET @Command = 
      'BACKUP DATABASE [' + @DatabaseName + '] '
     + 'TO DISK = ''' + @BackupFileName + ''' WITH INIT';
    EXEC (@Command);

    FETCH NEXT FROM db_cursor INTO @DatabaseName;
END

CLOSE db_cursor;
DEALLOCATE db_cursor;

GO
-- 3. Disabilita xp_cmdshell
EXEC sp_configure 'xp_cmdshell', 0;  
RECONFIGURE WITH OVERRIDE;
EXEC sp_configure 'show advanced options', 0;  
RECONFIGURE WITH OVERRIDE;
GO