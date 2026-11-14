/* =====================================================================
   DocAi: reverse (cancel) one page top-up given by mistake.

   How to use (SSMS, connected to the DocAi service database, e.g. ISTDocAiLocal):
     0. Find the GrantId of the wrong top-up (read only):
          SELECT g.GrantId, g.Pages, g.Kind, g.OriginalGrantId, g.BalanceAfter, g.Note, g.RecordedAtUtc
          FROM dbo.PageAllowanceGrants g JOIN dbo.Consumers c ON c.ConsumerId = g.ConsumerId
          WHERE c.Code = 'FWD-UNF' ORDER BY g.GrantId DESC;
     1. Set the values in the "Settings" block below.
     2. Run with @Apply = 0  -> PREVIEW: shows the target top-up and Before -> After, then rolls back.
     3. Review, set @Apply = 1 and run again -> COMMIT.
     4. Each reversal needs its own NEW @RequestId (run: SELECT NEWID();).
        Re-running with the same @RequestId never reverses twice (Replay = 1).

   Nothing is deleted: a 'Reversal' row with the negative pages is appended and linked to the
   original top-up. A top-up can be reversed only once, only for the same customer.
   If the customer already used/reserved those pages the reversal is refused (shows the shortfall);
   @AllowDebt = 1 lets the balance go negative (customer blocked until the next top-up covers it).

   Same rules as deploy\onprem\32-page-allowance.ps1 -Action Reverse.
   Production at IST: prefer 32-page-allowance.ps1 (interactive preview + yes).
   ===================================================================== */

-- USE ISTDocAiLocal;   -- local VS profile database

------------------------------ Settings ------------------------------
DECLARE @ConsumerCode varchar(30)       = 'FWD-UNF';
DECLARE @GrantId      bigint            = 0;            -- GrantId of the TopUp to reverse
DECLARE @Note         nvarchar(400)     = N'Duplicate top-up by mistake';
DECLARE @Reference    nvarchar(100)     = N'';          -- optional
DECLARE @RequestId    uniqueidentifier  = '3F9A1C77-5E24-4B8D-A6C3-2B71D0E9F458';  -- NEW value per reversal
DECLARE @AllowDebt    bit               = 0;            -- 1 = allow negative balance (approved only)
DECLARE @Apply        bit               = 0;            -- 0 = preview (rollback), 1 = commit
-----------------------------------------------------------------------

SET NOCOUNT ON; SET XACT_ABORT ON;
IF OBJECT_ID('dbo.PageAllowanceGrants','U') IS NULL OR OBJECT_ID('dbo.PageQuotaOperatorRequests','U') IS NULL
    THROW 50018, 'Wrong database: DocAi page quota tables not found.', 1;
IF @GrantId IS NULL OR @GrantId <= 0 THROW 50018, 'Set @GrantId to the TopUp to reverse (positive).', 1;
IF @RequestId IS NULL OR @RequestId = '00000000-0000-0000-0000-000000000000' THROW 50018, 'Set @RequestId (SELECT NEWID()).', 1;
IF @AllowDebt IS NULL SET @AllowDebt = 0;
IF @Reference IS NULL SET @Reference = N'';
IF LEN(@Reference) > 0 SET @Note = CONCAT(@Note, N' [Reference: ', @Reference, N']');
IF @Note IS NULL OR LEN(LTRIM(RTRIM(@Note))) = 0 OR LEN(@Note) > 400 THROW 50018, 'Note of 1..400 characters (including Reference) required.', 1;

-- Same audit identity and payload format as the PowerShell tool (retry = replay, not a second reversal).
DECLARE @Operator nvarchar(200) = LEFT(CONCAT(SUSER_SNAME(), N'@', HOST_NAME()), 200);
DECLARE @Payload nvarchar(1200) = CONCAT(N'Reverse|0|', @GrantId, N'||10|0|', CONVERT(int, @AllowDebt), N'|0|', @Operator, N'|', @Note);

BEGIN TRY
BEGIN TRANSACTION;
DECLARE @id smallint = (SELECT ConsumerId FROM dbo.Consumers WHERE Code = @ConsumerCode);
IF @id IS NULL THROW 50018, 'Consumer not found.', 1;
EXEC dbo.AcquirePageQuotaLock @id;
IF NOT EXISTS (SELECT 1 FROM dbo.Consumers WHERE ConsumerId = @id AND IsActive = 1) THROW 50018, 'Consumer inactive.', 1;

DECLARE @before bigint = (SELECT AvailablePages FROM dbo.ConsumerPageBalance WHERE ConsumerId = @id),
        @delta bigint = NULL, @original bigint = NULL, @replay bit = 0, @refused nvarchar(300) = NULL;

IF EXISTS (SELECT 1 FROM dbo.PageQuotaOperatorRequests WHERE RequestId = @RequestId)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.PageQuotaOperatorRequests
                   WHERE RequestId = @RequestId AND ConsumerId = @id AND Payload = @Payload)
        THROW 50018, 'RequestId already used for a different request; no changes. Use a NEW @RequestId.', 1;
    SET @replay = 1;
END;

-- The top-up that will be reversed, so you can check it is the right one.
SELECT 'Target top-up' AS Phase, g.GrantId, g.Pages, g.Kind, g.BalanceAfter, g.Note, g.RecordedAtUtc,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.PageAllowanceGrants r WHERE r.Kind = 'Reversal' AND r.OriginalGrantId = g.GrantId)
         THEN 'Already reversed' ELSE 'Not reversed' END AS ReversalState
FROM dbo.PageAllowanceGrants g WHERE g.GrantId = @GrantId AND g.ConsumerId = @id;

SELECT 'Before' AS Phase, c.Code, c.QuotaMode,
    COALESCE((SELECT SUM(Pages) FROM dbo.PageAllowanceGrants WHERE ConsumerId = @id), 0) AS Granted,
    COALESCE((SELECT SUM(Pages) FROM dbo.CustomerPageCharges WHERE ConsumerId = @id), 0) AS Used,
    COALESCE((SELECT SUM(Pages) FROM dbo.PageReservations WHERE ConsumerId = @id AND State = 'Reserved'), 0) AS Reserved,
    @before AS Remaining
FROM dbo.Consumers c WHERE c.ConsumerId = @id;

IF @replay = 0
BEGIN
    SELECT @delta = -Pages, @original = GrantId FROM dbo.PageAllowanceGrants
    WHERE GrantId = @GrantId AND ConsumerId = @id AND Kind = 'TopUp';
    IF @original IS NULL THROW 50018, 'TopUp not found for this consumer (only a TopUp of the same customer can be reversed).', 1;
    IF EXISTS (SELECT 1 FROM dbo.PageAllowanceGrants WHERE OriginalGrantId = @original AND Kind = 'Reversal')
        THROW 50018, 'TopUp already reversed.', 1;
    IF @before + @delta < 0 AND @AllowDebt = 0
        SET @refused = CONCAT(N'REFUSED: customer already used/reserved ', -@delta - @before,
                              N' of these pages. Approved debt only: set @AllowDebt = 1.');
    IF @refused IS NULL AND EXISTS (SELECT 1 FROM dbo.PageAllowanceGrants WHERE RequestId = @RequestId)
        THROW 50018, 'RequestId already belongs to an allowance grant. Use a NEW @RequestId.', 1;
    IF @refused IS NULL
    BEGIN
        -- The INSTEAD OF trigger re-checks: same customer, exact negative pages, TopUp only, once only.
        INSERT dbo.PageAllowanceGrants (ConsumerId, Pages, Kind, OriginalGrantId, OperatorIdentity, Note, RequestId)
        VALUES (@id, @delta, 'Reversal', @original, @Operator, @Note, @RequestId);
        INSERT dbo.PageQuotaOperatorRequests (RequestId, ConsumerId, Payload) VALUES (@RequestId, @id, @Payload);
    END;
END;

IF @refused IS NOT NULL
BEGIN
    SELECT 'Refused' AS Phase, @refused AS Result, @before AS Remaining, -@delta - @before AS Shortfall;
    ROLLBACK;
    IF @Apply = 1 THROW 50018, @refused, 1;
    RETURN;
END;

SELECT 'After' AS Phase,
    CASE WHEN @replay = 1 THEN 'ALREADY APPLIED earlier (nothing reversed again).'
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

SELECT GrantId, Pages, Kind, OriginalGrantId, BalanceAfter, RequestId, RecordedAtUtc
FROM dbo.PageAllowanceGrants WHERE RequestId = @RequestId;

IF @Apply = 1 COMMIT; ELSE ROLLBACK;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK;
    THROW;
END CATCH;
