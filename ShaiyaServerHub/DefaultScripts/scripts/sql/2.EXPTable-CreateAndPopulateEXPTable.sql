-- Seleziona il database su cui operare
USE [PS_GameDefs];
GO

/****** Object:  Table [dbo].[EXPTable]    Script Date: 10/22/2024 9:23:44 PM ******/

-- Impostazioni per ANSI_NULLS e QUOTED_IDENTIFIER
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Crea la tabella [EXPTable] solo se non esiste già
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EXPTable' AND TABLE_SCHEMA = 'dbo')
BEGIN
    CREATE TABLE [dbo].[EXPTable](
        [ID] [int] IDENTITY(1,1) NOT NULL,       -- Colonna ID con incremento automatico
        [EXP] [varchar](max) NOT NULL,           -- Colonna EXP che contiene valori di esperienza
        [NameDay] [varchar](max) NOT NULL,       -- Colonna NameDay che contiene il nome del giorno
        [ActionTime] [datetime] NOT NULL         -- Colonna ActionTime che contiene la data e l'ora dell'azione
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];
END;
GO

-- Cancella i dati esistenti dalla tabella per garantire che i nuovi valori vengano inseriti
DELETE FROM [dbo].[EXPTable];

-- Inserisce i nuovi dati nella tabella [EXPTable]
INSERT INTO [dbo].[EXPTable] (NameDay, EXP, ActionTime)
VALUES 
    ('Monday', '75', GETDATE()),             -- Inserisce i dati per il lunedì
    ('Tuesday', '75', GETDATE()),            -- Inserisce i dati per il martedì
    ('Wednesday', '75', GETDATE()),          -- Inserisce i dati per il mercoledì
    ('Thursday', '75', GETDATE()),           -- Inserisce i dati per il giovedì
    ('Friday', '75', GETDATE()),             -- Inserisce i dati per il venerdì
    ('Saturday', '100', GETDATE()),          -- Inserisce i dati per il sabato
    ('Sunday', '120', GETDATE());            -- Inserisce i dati per la domenica
GO
