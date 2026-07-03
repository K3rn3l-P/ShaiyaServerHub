-- Verifica se i premi sono già stati distribuiti per la settimana corrente
IF NOT EXISTS (
    SELECT 1 
    FROM PS_Billing.dbo.Users_Product 
    WHERE ProductCode = 'GRBReward' 
    AND BuyDate > DATEADD(week, DATEDIFF(week, 0, GETDATE()), 0)
)
BEGIN
    -- Premi solo se non sono già stati assegnati
    DECLARE @GuildID INT, @MasterUserID VARCHAR(12), @UserUID INT, @Slot TINYINT
    DECLARE @Rank INT = 1
    DECLARE @ItemID INT

    -- Creazione di un cursore per le gilde
    DECLARE guild_cursor CURSOR FOR
    SELECT TOP 3 GuildID, MasterUserID
    FROM PS_GameData.dbo.Guilds
    WHERE Del = 0
    ORDER BY GuildPoint DESC

    OPEN guild_cursor
    FETCH NEXT FROM guild_cursor INTO @GuildID, @MasterUserID

    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Ottieni il UserUID dal MasterUserID
        SELECT @UserUID = UserUID 
        FROM PS_UserData.dbo.Users_Master 
        WHERE UserID = @MasterUserID

        IF @UserUID IS NOT NULL
        BEGIN
            -- Premi per i primi 3 posti
            DECLARE @Items TABLE (ItemID INT)
            IF @Rank = 1
                INSERT INTO @Items (ItemID) VALUES (100202), (100203), (100204) -- Premi per il primo posto
            ELSE IF @Rank = 2
                INSERT INTO @Items (ItemID) VALUES (100202), (100203) -- Premi per il secondo posto
            ELSE IF @Rank = 3
                INSERT INTO @Items (ItemID) VALUES (100202) -- Premio per il terzo posto

            -- Inserisci ogni item nella banca del leader della gilda
            DECLARE item_cursor CURSOR FOR SELECT ItemID FROM @Items
            OPEN item_cursor
            FETCH NEXT FROM item_cursor INTO @ItemID

            WHILE @@FETCH_STATUS = 0
            BEGIN
                -- Trova slot libero
                SET @Slot = 0
                WHILE @Slot < 240
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 
                        FROM PS_Billing.dbo.Users_Product 
                        WHERE UserUID = @UserUID 
                        AND Slot = @Slot
                    )
                    BEGIN
                        -- Inserimento nel caso in cui c'è uno slot libero
                        INSERT INTO PS_Billing.dbo.Users_Product (UserUID, Slot, ItemID, ItemCount, ProductCode, BuyDate) 
                        VALUES (@UserUID, @Slot, @ItemID, 1, 'GRBReward', CURRENT_TIMESTAMP)
                        BREAK -- Esci dal ciclo una volta inserito l'item
                    END
                    SET @Slot = @Slot + 1
                END

                FETCH NEXT FROM item_cursor INTO @ItemID
            END

            CLOSE item_cursor
            DEALLOCATE item_cursor

            SET @Rank = @Rank + 1 -- Passa al prossimo rango
        END

        FETCH NEXT FROM guild_cursor INTO @GuildID, @MasterUserID
    END

    CLOSE guild_cursor
    DEALLOCATE guild_cursor
END
ELSE
BEGIN
    PRINT 'Premi già distribuiti questa settimana!'
END
