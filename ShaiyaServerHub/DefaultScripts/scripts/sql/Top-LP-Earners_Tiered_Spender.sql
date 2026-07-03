-- Script: Premia automaticamente l'utente con più DP (Loyalty Points)
-- Trova l'evento attivo più recente
DECLARE @SpenderID INT;
SELECT TOP 1 @SpenderID = ID
FROM PS_WebSite.dbo.Tiered_Spender
WHERE StartDate <= GETDATE() AND EndDate >= GETDATE()
ORDER BY StartDate DESC;

-- Se esiste un evento attivo, trova l'utente top e premialo
IF @SpenderID IS NOT NULL
BEGIN
    ;WITH TopUser AS (
        SELECT TOP 1 UserUID, SUM(DP) AS TotalDP
        FROM PS_WebSite.dbo.Tiered_Spender_User_Progress
        GROUP BY UserUID
        ORDER BY TotalDP DESC
    )
    MERGE PS_WebSite.dbo.Tiered_Spender_User_Progress AS target
    USING (SELECT UserUID FROM TopUser) AS src
        ON target.UserUID = src.UserUID AND target.SpenderID = @SpenderID
    WHEN MATCHED THEN
        UPDATE SET DP = DP + 10
    WHEN NOT MATCHED THEN
        INSERT (UserUID, SpenderID, DP) VALUES (src.UserUID, @SpenderID, 1);
END