/* =====================================================================
   DocAi page top-up (renewal) for one customer.

   How to use (SSMS, connected to the DocAi service database, e.g. ISTDocAiLocal):
     1. Set the values in the "Settings" block below.
     2. Run with @Apply = 0  -> PREVIEW: shows Before -> After, then rolls back (nothing saved).
     3. Review, set @Apply = 1 and run again -> COMMIT.
     4. NEXT renewal: put a NEW @RequestId (run: SELECT NEWID();).
        Re-running with the same @RequestId never adds pages twice (Replay = 1).

   Same rules as deploy\onprem\32-page-allowance.ps1 -Action TopUp:
   consumer lock, append-only grants, same retry identity (PageQuotaOperatorRequests).
   Production at IST: prefer 32-page-allowance.ps1 (interactive preview + yes).
   ===================================================================== */

-- USE ISTDocAiLocal;   -- local VS profile database

------------------------------ Settings ------------------------------
DECLARE @ConsumerCode varchar(30)       = 'FWD-UNF';
DECLARE @Pages        bigint            = 40;
DECLARE @Note         nvarchar(400)     = N'Approved renewal 40 pages';
DECLARE @Reference    nvarchar(100)     = N'';          -- optional, e.g. invoice/order number
DECLARE @RequestId    uniqueidentifier  = '8E6C2F4A-3B1D-4C7E-9A52-6D0F1B7E3C91';  -- NEW value per renewal
DECLARE @Apply        bit               = 0;            -- 0 = preview (rollback), 1 = commit
-----------------------------------------------------------------------

SET NOCOUNT ON; SET XACT_ABORT ON;
IF OBJECT_ID('dbo.PageAllowanceGrants','U') IS NULL OR OBJECT_ID('dbo.PageQuotaOperatorRequests','U') IS NULL
    THROW 50018, 'Wrong database: DocAi page quota tables not found.', 1;
IF @Pages < 1 OR @Pages > 1000000 THROW 50018, 'TopUp Pages must be 1..1000000.', 1;
IF @RequestId IS NULL OR @RequestId = '00000000-0000-0000-0000-000000000000' THROW 50018, 'Set @RequestId (SELECT NEWID()).', 1;
IF @Reference IS NULL SET @Reference = N'';
IF LEN(@Reference) > 0 SET @Note = CONCAT(@Note, N' [Reference: ', @Reference, N']');
IF @Note IS NULL OR LEN(LTRIM(RTRIM(@Note))) = 0 OR LEN(@Note) > 400 THROW 50018, 'Note of 1..400 characters (including Reference) required.', 1;

-- Same audit identity format as the PowerShell tool: DOMAIN\user@MACHINE.
DECLARE @Operator nvarchar(200) = LEFT(CONCAT(SUSER_SNAME(), N'@', HOST_NAME()), 200);
-- Same payload format as the tool, so a retry with the same RequestId is recognised as a replay.
DECLARE @Payload nvarchar(1200) = CONCAT(N'TopUp|', @Pages, N'|0||10|0|0|0|', @Operator, N'|', @Note);

BEGIN TRY
BEGIN TRANSACTION;
DECLARE @id smallint = (SELECT ConsumerId FROM dbo.Consumers WHERE Code = @ConsumerCode);
IF @id IS NULL THROW 50018, 'Consumer not found.', 1;
EXEC dbo.AcquirePageQuotaLock @id;
IF NOT EXISTS (SELECT 1 FROM dbo.Consumers WHERE ConsumerId = @id AND IsActive = 1) THROW 50018, 'Consumer inactive.', 1;

DECLARE @replay bit = 0;
IF EXISTS (SELECT 1 FROM dbo.PageQuotaOperatorRequests WHERE RequestId = @RequestId)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.PageQuotaOperatorRequests
                   WHERE RequestId = @RequestId AND ConsumerId = @id AND Payload = @Payload)
        THROW 50018, 'RequestId already used for a different request; no changes. Use a NEW @RequestId.', 1;
    SET @replay = 1;
END;
IF @replay = 0 AND EXISTS (SELECT 1 FROM dbo.PageAllowanceGrants WHERE RequestId = @RequestId)
    THROW 50018, 'RequestId already belongs to an allowance grant. Use a NEW @RequestId.', 1;

SELECT 'Before' AS Phase, c.Code, c.Name, c.QuotaMode, c.QuotaWarningPercent,
    COALESCE((SELECT SUM(Pages) FROM dbo.PageAllowanceGrants WHERE ConsumerId = @id), 0) AS Granted,
    COALESCE((SELECT SUM(Pages) FROM dbo.CustomerPageCharges WHERE ConsumerId = @id), 0) AS Used,
    COALESCE((SELECT SUM(Pages) FROM dbo.PageReservations WHERE ConsumerId = @id AND State = 'Reserved'), 0) AS Reserved,
    b.AvailablePages AS Remaining
FROM dbo.Consumers c JOIN dbo.ConsumerPageBalance b ON b.ConsumerId = c.ConsumerId
WHERE c.ConsumerId = @id;

IF @replay = 0
BEGIN
    -- The INSTEAD OF trigger takes the consumer lock again and records BalanceAfter.
    INSERT dbo.PageAllowanceGrants (ConsumerId, Pages, Kind, OriginalGrantId, OperatorIdentity, Note, RequestId)
    VALUES (@id, @Pages, 'TopUp', NULL, @Operator, @Note, @RequestId);
    INSERT dbo.PageQuotaOperatorRequests (RequestId, ConsumerId, Payload) VALUES (@RequestId, @id, @Payload);
END;

SELECT 'After' AS Phase,
    CASE WHEN @replay = 1 THEN 'ALREADY APPLIED earlier (no pages added). For a new renewal use a NEW @RequestId.'
         WHEN @Apply = 1 THEN 'COMMITTED'
         ELSE 'PREVIEW ONLY - rolled back. Set @Apply = 1 to commit.' END AS Result,
    c.Code, c.QuotaMode,
    COALESCE((SELECT SUM(Pages) FROM dbo.PageAllowanceGrants WHERE ConsumerId = @id), 0) AS Granted,
    COALESCE((SELECT SUM(Pages) FROM dbo.CustomerPageCharges WHERE ConsumerId = @id), 0) AS Used,
    COALESCE((SELECT SUM(Pages) FROM dbo.PageReservations WHERE ConsumerId = @id AND State = 'Reserved'), 0) AS Reserved,
    b.AvailablePages AS Remaining,
    CASE WHEN c.QuotaMode = 'Unlimited' THEN 'Unlimited' WHEN b.AvailablePages <= 0 THEN 'Exhausted' ELSE 'Ok' END AS Status
FROM dbo.Consumers c JOIN dbo.ConsumerPageBalance b ON b.ConsumerId = c.ConsumerId
WHERE c.ConsumerId = @id;

SELECT GrantId, Pages, Kind, BalanceAfter, RequestId, RecordedAtUtc
FROM dbo.PageAllowanceGrants WHERE RequestId = @RequestId;

IF @Apply = 1 COMMIT; ELSE ROLLBACK;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK;
    THROW;
END CATCH;
