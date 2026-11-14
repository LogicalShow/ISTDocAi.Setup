-- =====================================================================================================
-- REFERENCE ONLY - DO NOT RUN THIS FILE AS A SCRIPT. The API and the Worker run these ([S1]-[S25]). Each statement is sent on its own, with
-- its @parameters bound by the code. Generated from the SQL tested on SQL Server 2019 and 2025 with
-- READ_COMMITTED_SNAPSHOT ON (plan revision 2.9). AUTO/MANIFEST additions: plan update pending.
-- =====================================================================================================

-- [S1] Intake: daily page cap over the runs created today (UTC), requeues and reprocesses included.
--      Informational only. The authoritative cap check is in the locked final S2 transaction.
SELECT COALESCE(SUM(PageCount), 0) AS PagesToday
FROM dbo.JobRuns
WHERE ConsumerId = @consumerId
  AND CreatedAtUtc >= CAST(CAST(SYSUTCDATETIME() AS date) AS datetime2(3));

-- [S2] Intake, in one transaction. The API calls it FIRST WITHOUT a blob (@blobPath NULL):
--      * the same submission again (same clientRequestId)  -> its own bound job and run       'Existing' (200)
--      * a queued/running job or an unchanged completed job -> bind this submission to it     'Existing' (200)
--      * anything that needs a new job or a new run         -> nothing changes                'NeedsUpload'
--      On NeedsUpload the API uploads the file and calls [S2] again WITH the blob; S2 atomically checks both caps:
--      a new job (run 1) 'Created', or a new run of a Failed job (or Completed with reprocess/model changes)
--      'Requeued' — both 202 — and older Pending events become Superseded. Every answered submission is bound
--      in JobSubmissions. Deadlock 1205: rerun the whole batch. A blob orphaned by a crash between the upload
--      and this transaction is removed by the 90-day lifecycle rule. Stage: with a line, or without one when the
--      document type has an active generic model (line NULL), the job starts at Extract; only a document type
--      with no generic model starts at Classify. AUTO always starts at Classify, unresolved.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
EXEC dbo.AcquirePageQuotaLock @consumerId;
DECLARE @availablePages bigint, @quotaMode varchar(10), @rereadSkipped varchar(30);
SELECT @availablePages=AvailablePages FROM dbo.ConsumerPageBalance WHERE ConsumerId=@consumerId;
SELECT @quotaMode=QuotaMode FROM dbo.Consumers WHERE ConsumerId=@consumerId;

DECLARE @existingJobId uniqueidentifier, @existingStatus varchar(12), @existingRun smallint,
        @runNo smallint, @outcome varchar(12), @stale bit = 0, @changes nvarchar(max),
        @resolvedType varchar(20), @resolvedLine varchar(20), @oldClassifier varchar(64), @oldExtract varchar(64),
        @oldFingerprint char(64), @classifierChanged bit = 0, @extractChanged bit = 0, @mappingChanged bit = 0;

SELECT @existingJobId = JobId, @runNo = RunNo             -- the same submission again: its binding never changes
FROM dbo.JobSubmissions WITH (UPDLOCK, HOLDLOCK)
WHERE ConsumerId = @consumerId AND ClientRequestId = @clientRequestId;

IF @existingJobId IS NOT NULL
    SET @outcome = 'Existing';
ELSE
BEGIN
    SELECT @existingJobId = JobId, @existingStatus = Status, @existingRun = RunNo,
           @resolvedType = ResolvedDocumentTypeCode, @resolvedLine = ShippingLineCode
    FROM dbo.Jobs WITH (UPDLOCK, HOLDLOCK)                 -- key-range lock: the same file is serialized
    WHERE ConsumerId = @consumerId AND DocumentTypeCode = @documentTypeCode
      AND FileSha256 = @fileSha256 AND IdemLine = @idemLine AND IsDeleted = 0;

    IF @existingStatus = 'Completed' AND @reprocess = 0
    BEGIN
        SELECT @resolvedType = ResolvedDocumentTypeCode FROM dbo.JobRuns
        WHERE JobId = @existingJobId AND RunNo = @existingRun;
        -- Snapshot may have been read while the worker was completing or another run was finishing.
        IF @expectedRunNo IS NULL OR @expectedRunNo <> @existingRun
           OR ISNULL(@expectedDocumentType, '') <> ISNULL(@resolvedType, '')
           OR ISNULL(@expectedLine, '') <> ISNULL(@resolvedLine, '')
            THROW 50015, 'Intake route changed; refresh the active model snapshot.', 1;
        SELECT @oldFingerprint = MappingFingerprint FROM dbo.JobRuns
        WHERE JobId = @existingJobId AND RunNo = @existingRun AND Status = 'Completed';
        SELECT TOP (1) @oldClassifier = ModelId FROM dbo.AzureOperations
        WHERE JobId = @existingJobId AND RunNo = @existingRun AND Kind = 'classify' AND State = 'Succeeded'
        ORDER BY OperationId DESC;
        SELECT TOP (1) @oldExtract = ModelId FROM dbo.AzureOperations
        WHERE JobId = @existingJobId AND RunNo = @existingRun AND Kind = 'extract' AND State = 'Succeeded'
        ORDER BY OperationId DESC;
        IF @documentTypeCode = 'AUTO' AND ISNULL(@oldClassifier, '') COLLATE Latin1_General_100_BIN2
             <> ISNULL(@activeClassifierModelId, '') COLLATE Latin1_General_100_BIN2 SET @classifierChanged = 1;
        IF ISNULL(@oldExtract, '') COLLATE Latin1_General_100_BIN2
             <> ISNULL(@activeExtractModelId, '') COLLATE Latin1_General_100_BIN2 SET @extractChanged = 1;
        IF @oldFingerprint IS NULL OR ISNULL(@oldFingerprint, '') <> ISNULL(@activeMappingFingerprint, '') SET @mappingChanged = 1;
        IF @classifierChanged = 1 OR @extractChanged = 1 OR @mappingChanged = 1
        BEGIN
            SET @stale = 1;
            SELECT @changes = (SELECT
                JSON_QUERY(CASE WHEN @classifierChanged = 1 THEN (SELECT @oldClassifier AS oldModelId, @activeClassifierModelId AS newModelId FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER) END) AS classifier,
                JSON_QUERY(CASE WHEN @extractChanged = 1 THEN (SELECT @oldExtract AS oldModelId, @activeExtractModelId AS newModelId FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER) END) AS extractModel,
                CASE WHEN @mappingChanged = 1 THEN CAST(1 AS bit) END AS mapping
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        END;
    END;

    -- Settle an older terminal reservation before computing headroom for its next run.
    IF @existingStatus IN ('Completed','Failed')
        EXEC dbo.SettlePageReservation @existingJobId,@existingRun;
    SELECT @availablePages=AvailablePages FROM dbo.ConsumerPageBalance WHERE ConsumerId=@consumerId;

    -- Decide under the consumer lock, before creating a run or a submission binding.
    IF @existingJobId IS NULL OR @existingStatus='Failed'
       OR (@existingStatus='Completed' AND (@reprocess=1 OR @stale=1))
    BEGIN
        IF @quotaMode='Prepaid' AND @pageCount>@availablePages
        BEGIN
            IF @stale=1 AND @reprocess=0 AND @existingStatus='Completed'
                SELECT @stale=0,@rereadSkipped='QUOTA_EXHAUSTED';
            ELSE IF @blobPath IS NOT NULL
            BEGIN
                SELECT @pageCount AS RequiredPages,@availablePages AS AvailablePages,
                    'QuotaExceeded' AS Outcome,CAST(NULL AS uniqueidentifier) AS JobId,
                    CAST(NULL AS smallint) AS RunNo,CAST(NULL AS varchar(12)) AS RunStatus;
                COMMIT;
                RETURN;
            END;
        END;
        IF @blobPath IS NOT NULL AND @rereadSkipped IS NULL
           AND (SELECT COALESCE(SUM(CONVERT(bigint,PageCount)),0) FROM dbo.JobRuns
                WHERE ConsumerId=@consumerId AND CreatedAtUtc>=CONVERT(date,SYSUTCDATETIME())) + @pageCount
                > (SELECT DailyPageCap FROM dbo.Consumers WHERE ConsumerId=@consumerId)
        BEGIN
            SELECT 'DailyCapExceeded' AS Outcome,CAST(NULL AS uniqueidentifier) AS JobId,
                CAST(NULL AS smallint) AS RunNo,CAST(NULL AS varchar(12)) AS RunStatus;
            COMMIT;
            RETURN;
        END;
    END;

    IF @existingJobId IS NOT NULL
       AND NOT (@existingStatus = 'Failed' OR ((@reprocess = 1 OR @stale = 1) AND @existingStatus = 'Completed'))
        SELECT @runNo = @existingRun, @outcome = 'Existing';
    ELSE IF @blobPath IS NULL
        SELECT @outcome = 'NeedsUpload', @existingJobId = NULL;
    ELSE IF @existingJobId IS NULL
    BEGIN
        INSERT dbo.Jobs (JobId, ConsumerId, ClientRequestId, DocumentTypeCode, RequestedShippingLineCode, IdemLine,
                         ShippingLineCode, FileSha256, FileName, ContentType, FileSizeBytes, PageCount, BlobPath,
                         Stage, CorrelationId, ResolvedDocumentTypeCode)
        VALUES (@jobId, @consumerId, @clientRequestId, @documentTypeCode, @requestedLine, @idemLine,
                @requestedLine, @fileSha256, @fileName, @contentType, @fileSizeBytes, @pageCount, @blobPath,
                CASE WHEN @documentTypeCode = 'AUTO' THEN 'Classify'
                     WHEN @requestedLine IS NULL AND NOT EXISTS (SELECT 1 FROM dbo.ModelRegistry AS g
                                                 WHERE g.DocumentTypeCode = @documentTypeCode AND g.Kind = 'extract'
                                                   AND g.ShippingLineCode IS NULL AND g.Status = 'Active')
                     THEN 'Classify' ELSE 'Extract' END, @correlationId, NULLIF(@documentTypeCode, 'AUTO'));
        INSERT dbo.JobRuns (JobId, RunNo, Reason, ConsumerId, PageCount, ClientRequestId, CorrelationId, ResolvedDocumentTypeCode)
        VALUES (@jobId, 1, 'New', @consumerId, @pageCount, @clientRequestId, @correlationId, NULLIF(@documentTypeCode, 'AUTO'));
        SELECT @existingJobId = @jobId, @runNo = 1, @outcome = 'Created';
    END
    ELSE
    BEGIN
        SET @runNo = @existingRun + 1;
        INSERT dbo.JobRuns (JobId, RunNo, Reason, ConsumerId, PageCount, ClientRequestId, CorrelationId, ResolvedDocumentTypeCode, ChangesJson)
        VALUES (@existingJobId, @runNo, CASE WHEN @existingStatus = 'Failed' THEN 'Requeue' WHEN @reprocess = 1 THEN 'Reprocess' ELSE 'ModelChanged' END,
                @consumerId, @pageCount, @clientRequestId, @correlationId, NULLIF(@documentTypeCode, 'AUTO'), @changes);
        UPDATE dbo.Jobs
        SET RunNo = @runNo, Status = 'Queued',
            Stage = CASE WHEN @documentTypeCode = 'AUTO' THEN 'Classify'
                         WHEN RequestedShippingLineCode IS NULL AND NOT EXISTS (SELECT 1 FROM dbo.ModelRegistry AS g
                                                      WHERE g.DocumentTypeCode = @documentTypeCode AND g.Kind = 'extract'
                                                        AND g.ShippingLineCode IS NULL AND g.Status = 'Active')
                          THEN 'Classify' ELSE 'Extract' END,
            ResolvedDocumentTypeCode = NULLIF(@documentTypeCode, 'AUTO'),
            ShippingLineCode = RequestedShippingLineCode, ModelId = NULL, Attempts = 0, TakeoverCount = 0,
            NextAttemptAtUtc = SYSUTCDATETIME(), LeaseToken = NULL, LeaseExpiresAtUtc = NULL,
            ErrorCode = NULL, ErrorMessage = NULL, CompletedAtUtc = NULL,
            BlobPath = @blobPath, ClientRequestId = @clientRequestId, CorrelationId = @correlationId
        WHERE JobId = @existingJobId;
        UPDATE dbo.WebhookDeliveries                            -- older runs' undelivered events: the bound
        SET Status = 'Superseded', LeaseToken = NULL, LeaseExpiresAtUtc = NULL   -- consumers read them by GET
        WHERE JobId = @existingJobId AND RunNo < @runNo AND Status = 'Pending';
        SET @outcome = 'Requeued';
    END

    IF @outcome IN ('Created','Requeued')
        INSERT dbo.PageReservations(JobId,RunNo,ConsumerId,Pages)
        VALUES(@existingJobId,@runNo,@consumerId,@pageCount);

    IF @outcome <> 'NeedsUpload' AND @clientRequestId IS NOT NULL
        INSERT dbo.JobSubmissions (ConsumerId, ClientRequestId, JobId, RunNo)
        VALUES (@consumerId, @clientRequestId, @existingJobId, @runNo);
END

SELECT @rereadSkipped AS RereadSkipped, @existingJobId AS JobId, @runNo AS RunNo, @outcome AS Outcome,
       (SELECT CASE WHEN r.Status = 'Active' THEN j.Status ELSE r.Status END
        FROM dbo.JobRuns AS r JOIN dbo.Jobs AS j ON j.JobId = r.JobId
        WHERE r.JobId = @existingJobId AND r.RunNo = @runNo) AS RunStatus,
       (SELECT Reason FROM dbo.JobRuns WHERE JobId = @existingJobId AND RunNo = @runNo) AS RunReason,
       (SELECT ChangesJson FROM dbo.JobRuns WHERE JobId = @existingJobId AND RunNo = @runNo) AS ChangesJson;
COMMIT;

-- [S3] Worker: claim the next due job with a new lease token. Several workers can run this at the same time:
--      READPAST skips rows another worker has locked, UPDLOCK keeps two workers from taking the same row.
--      Taking over an expired lease counts as a takeover; the worker fails the job at the 3rd ([S9]).
DECLARE @leaseToken uniqueidentifier = NEWID();
WITH next_job AS (
    SELECT TOP (1) *
    FROM dbo.Jobs WITH (ROWLOCK, UPDLOCK, READPAST)
    WHERE IsDeleted = 0
      AND (   (Status = 'Queued'     AND NextAttemptAtUtc  <= SYSUTCDATETIME())
           OR (Status = 'Processing' AND LeaseExpiresAtUtc <  SYSUTCDATETIME()))   -- a crashed worker's job
    ORDER BY NextAttemptAtUtc
)
UPDATE next_job
SET TakeoverCount     = TakeoverCount + CASE WHEN Status = 'Processing' THEN 1 ELSE 0 END,
    Status            = 'Processing',
    LeaseToken        = @leaseToken,
    LeaseExpiresAtUtc = DATEADD(second, 60, SYSUTCDATETIME()),
    StartedAtUtc      = COALESCE(StartedAtUtc, SYSUTCDATETIME())
OUTPUT inserted.JobId, inserted.RunNo, inserted.LeaseToken, inserted.TakeoverCount, inserted.ConsumerId,
       inserted.DocumentTypeCode, inserted.ShippingLineCode, inserted.Stage, inserted.BlobPath,
       inserted.PageCount, inserted.Attempts, inserted.CorrelationId, inserted.ResolvedDocumentTypeCode;

-- [S4a] Worker: record the Azure call BEFORE the POST. No row returned: the lease was lost — do not POST.
--       50013: this run already has two Unknown calls — fail it with RETRIES_EXHAUSTED ([S9]) instead of paying
--       for a third. UX_Op_Open refuses a second open operation of the same kind for the run.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF (SELECT COUNT(*) FROM dbo.AzureOperations AS o WITH (UPDLOCK, HOLDLOCK)
    JOIN dbo.Jobs AS j ON j.JobId = o.JobId AND j.RunNo = o.RunNo
    WHERE j.JobId = @jobId AND o.State = 'Unknown') >= 2
    THROW 50013, 'Two Unknown Azure calls in this run.', 1;

INSERT dbo.AzureOperations (JobId, RunNo, Kind, ModelId)
OUTPUT inserted.OperationId
SELECT j.JobId, j.RunNo, @kind, @modelId
FROM dbo.Jobs AS j
WHERE j.JobId = @jobId AND j.Status = 'Processing' AND j.LeaseToken = @leaseToken;

COMMIT;

-- [S4b] Worker: Azure answered 202. Recording a fact Azure already created is always safe, so this needs
--       no lease: if another worker took the job over meanwhile, it finds the location and resumes polling
--       instead of paying for a second analysis.
UPDATE dbo.AzureOperations
SET State = 'Accepted', OperationLocation = @operationLocation, AcceptedAtUtc = SYSUTCDATETIME()
WHERE OperationId = @operationId AND State = 'Sending';

-- [S4c] Worker: close a call that never got its 202 recorded — the POST got another answer (@state = 'Failed')
--       or none (@state = 'Unknown': Azure may have accepted and billed it), or the worker found it Sending
--       after a takeover ('Unknown'). Only the lease holder, and only while it is still Sending without a
--       location. 0 rows: [S4b] recorded the 202 meanwhile — read [S19] again and resume polling.
UPDATE o
SET State = @state, AzureErrorCode = @azureErrorCode, CompletedAtUtc = SYSUTCDATETIME()
FROM dbo.AzureOperations AS o
JOIN dbo.Jobs AS j ON j.JobId = o.JobId AND j.RunNo = o.RunNo
WHERE o.OperationId = @operationId AND o.State = 'Sending' AND o.OperationLocation IS NULL
  AND j.Status = 'Processing' AND j.LeaseToken = @leaseToken
  AND @state IN ('Failed', 'Unknown');
SELECT @@ROWCOUNT AS Closed;

-- [S5] Worker: heartbeat renews every 15 seconds throughout processing and shutdown draining.
--      0 rows: the lease was lost — stop at once and write nothing else. A Retry-After longer than 5 minutes is
--      not waited out under the lease: the worker releases the job with [S7] (delay = Retry-After) instead.
UPDATE dbo.Jobs
SET LeaseExpiresAtUtc = DATEADD(second, 60, SYSUTCDATETIME())
WHERE JobId = @jobId AND Status = 'Processing' AND LeaseToken = @leaseToken;
SELECT @@ROWCOUNT AS Renewed;

-- [S6] Worker: a retryable error (429, 5xx, timeout) before the 5th attempt; the code decides from Attempts
--      whether this is the 5th failure ([S9]). Back-off 30 s, 1, 2, 4 min — or Retry-After when longer.
--      An Accepted operation stays open, so the retry resumes polling instead of posting again.
UPDATE dbo.Jobs
SET Attempts          = Attempts + 1,
    Status            = 'Queued',
    NextAttemptAtUtc  = DATEADD(second,
                            CASE WHEN @retryAfterSeconds > 15 * POWER(2, Attempts + 1) THEN @retryAfterSeconds
                                 ELSE 15 * POWER(2, Attempts + 1) END,
                            SYSUTCDATETIME()),
    LeaseToken        = NULL,
    LeaseExpiresAtUtc = NULL,
    ErrorCode         = @errorCode
WHERE JobId = @jobId AND Status = 'Processing' AND LeaseToken = @leaseToken AND Attempts < 4;
SELECT @@ROWCOUNT AS Requeued;     -- 0: the lease was lost (the code never calls [S6] on the 5th failure)

-- [S7] Worker: release the job without using an attempt or counting a takeover — a configuration error
--      (401/403 from Azure: delay 5 min, then the breaker pauses the worker and alerts), a Retry-After longer
--      than 5 minutes (delay = Retry-After, no error), or a graceful shutdown (delay 0, no error).
UPDATE dbo.Jobs
SET Status = 'Queued', LeaseToken = NULL, LeaseExpiresAtUtc = NULL,
    NextAttemptAtUtc = DATEADD(second, @delaySeconds, SYSUTCDATETIME()), ErrorCode = @errorCode
WHERE JobId = @jobId AND Status = 'Processing' AND LeaseToken = @leaseToken;

-- [S8a] Worker: the extraction succeeded at Azure. BEFORE any mapping, the raw analyzeResult is saved to Blob;
--       then the operation, its ledger row and Stage = 'Map' commit together. From here on a crash or a mapping
--       failure can't lose the cost, and mapping resumes from the saved result. No price in force: the row is
--       recorded unpriced and the "unpriced usage" alert fires ([B9]); nothing waits for it. The row keeps the
--       month Azure did the work in — Azure billed it there — even if that month is already closed ([B8]).
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @quotaConsumerId smallint=(SELECT ConsumerId FROM dbo.Jobs WHERE JobId=@jobId);
EXEC dbo.AcquirePageQuotaLock @quotaConsumerId;


UPDATE dbo.Jobs
SET Stage = 'Map', ModelId = @modelId, LeaseExpiresAtUtc = DATEADD(second, 60, SYSUTCDATETIME())
WHERE JobId = @jobId AND Status = 'Processing' AND LeaseToken = @leaseToken AND Stage = 'Extract';
IF @@ROWCOUNT = 0 THROW 50010, 'Lease lost: another worker owns this job.', 1;

UPDATE dbo.AzureOperations
SET State = 'Succeeded', Pages = @pages, CompletedAtUtc = @azureCompletedAtUtc, RawResultPath = @rawResultPath
WHERE OperationId = @operationId AND State = 'Accepted';
IF @@ROWCOUNT = 0 THROW 50012, 'The operation is not open.', 1;

DECLARE @price decimal(9,4) = (SELECT TOP (1) PricePer1000 FROM dbo.AzureMeterPrices
                               WHERE MeterCode = 'DI_CUSTOM_EXTRACT'
                                 AND EffectiveFrom <= CAST(@azureCompletedAtUtc AS date)
                               ORDER BY EffectiveFrom DESC);

INSERT dbo.UsageLedger (ConsumerId, JobId, RunNo, OperationId, MeterCode, ModelId, Pages, PricePer1000,
                        OccurredAtUtc, CorrelationId)
SELECT ConsumerId, JobId, RunNo, @operationId, 'DI_CUSTOM_EXTRACT', @modelId, @pages, @price,
       @azureCompletedAtUtc, CorrelationId
FROM dbo.Jobs WHERE JobId = @jobId;

DECLARE @quotaRunNo smallint=(SELECT RunNo FROM dbo.Jobs WHERE JobId=@jobId);
EXEC dbo.SettlePageReservation @jobId,@quotaRunNo;
COMMIT;

-- [S8] Worker: the result is mapped. Result, webhook event, run outcome and job state commit together.
--      50010: lease lost — stop. Mapping that keeps failing is retried like any error ([S6]); its cost is
--      already in the ledger.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @quotaConsumerId smallint=(SELECT ConsumerId FROM dbo.Jobs WHERE JobId=@jobId);
EXEC dbo.AcquirePageQuotaLock @quotaConsumerId;


UPDATE dbo.Jobs
SET Status = 'Completed', CompletedAtUtc = SYSUTCDATETIME(), LeaseToken = NULL, LeaseExpiresAtUtc = NULL,
    ErrorCode = NULL, ErrorMessage = NULL
WHERE JobId = @jobId AND Status = 'Processing' AND LeaseToken = @leaseToken AND Stage = 'Map';
IF @@ROWCOUNT = 0 THROW 50010, 'Lease lost: another worker owns this job.', 1;

UPDATE r
SET Status = 'Completed', CompletedAtUtc = SYSUTCDATETIME(), MappingFingerprint = @mappingFingerprint,
    ResultType = COALESCE(@resultType, CASE r.ResolvedDocumentTypeCode WHEN 'BL' THEN 'bill-of-lading.v1'
                                                     WHEN 'MANIFEST' THEN 'cargo-manifest.v1' END)
FROM dbo.JobRuns AS r JOIN dbo.Jobs AS j ON j.JobId = r.JobId AND j.RunNo = r.RunNo
WHERE r.JobId = @jobId;

INSERT dbo.JobResults (JobId, RunNo, ContractVersion, ResultJson, FlaggedFields)
SELECT JobId, RunNo, '1.0', @resultJson, @flaggedFields FROM dbo.Jobs WHERE JobId = @jobId;

INSERT dbo.WebhookDeliveries (EventId, JobId, RunNo, ConsumerId, EventType, PayloadJson)
SELECT @eventId, j.JobId, j.RunNo, j.ConsumerId, 'job.completed', @payloadJson
FROM dbo.Jobs AS j
JOIN dbo.Consumers AS c ON c.ConsumerId = j.ConsumerId
WHERE j.JobId = @jobId AND c.CallbackUrl IS NOT NULL;              -- polling-only consumers get no webhook

DECLARE @quotaRunNo smallint=(SELECT RunNo FROM dbo.Jobs WHERE JobId=@jobId);
EXEC dbo.SettlePageReservation @jobId,@quotaRunNo;
COMMIT;

-- [S9] Worker: a fatal outcome, the 5th retryable failure, or the 3rd takeover. The job and its run fail
--      together, and the run gets its own immutable job.failed event. The message is cut to 400 characters.
--      @operationId: the Accepted Azure call that ended without a result (the analysis failed, or a poll
--      answered 404), closed Failed in this same transaction — a crash can then never leave a failed call
--      behind a job that is still Processing, which the next worker would analyze (and pay for) again. NULL
--      when no call is involved.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @quotaConsumerId smallint=(SELECT ConsumerId FROM dbo.Jobs WHERE JobId=@jobId);
EXEC dbo.AcquirePageQuotaLock @quotaConsumerId;


UPDATE dbo.Jobs
SET Status = 'Failed', Attempts = Attempts + CASE WHEN @countsAsAttempt = 1 THEN 1 ELSE 0 END,
    LeaseToken = NULL, LeaseExpiresAtUtc = NULL, CompletedAtUtc = SYSUTCDATETIME(),
    ErrorCode = @errorCode, ErrorMessage = LEFT(@errorMessage, 400)
WHERE JobId = @jobId AND Status = 'Processing' AND LeaseToken = @leaseToken;

IF @@ROWCOUNT = 1
BEGIN
    UPDATE dbo.AzureOperations
    SET State = 'Failed', AzureErrorCode = @azureErrorCode, CompletedAtUtc = SYSUTCDATETIME()
    WHERE OperationId = @operationId AND JobId = @jobId AND State = 'Accepted';

    UPDATE r
    SET Status = 'Failed', ErrorCode = @errorCode, ErrorMessage = LEFT(@errorMessage, 400),
        CompletedAtUtc = SYSUTCDATETIME()
    FROM dbo.JobRuns AS r JOIN dbo.Jobs AS j ON j.JobId = r.JobId AND j.RunNo = r.RunNo
    WHERE r.JobId = @jobId;

    INSERT dbo.WebhookDeliveries (EventId, JobId, RunNo, ConsumerId, EventType, PayloadJson)
    SELECT @eventId, j.JobId, j.RunNo, j.ConsumerId, 'job.failed', @payloadJson
    FROM dbo.Jobs AS j
    JOIN dbo.Consumers AS c ON c.ConsumerId = j.ConsumerId
    WHERE j.JobId = @jobId AND c.CallbackUrl IS NOT NULL;
END

DECLARE @quotaRunNo smallint=(SELECT RunNo FROM dbo.Jobs WHERE JobId=@jobId);
EXEC dbo.SettlePageReservation @jobId,@quotaRunNo;
COMMIT;

-- [S10] Webhook sender: claim due deliveries with a new lease token (same pattern as [S3]). The batch is sent
--       4 at a time with a 15 s timeout, well inside the 5-minute lease; no send starts in the lease's last
--       90 seconds (the rows not started wait for the lease to expire and are claimed again).
DECLARE @leaseToken uniqueidentifier = NEWID();
WITH due AS (
    SELECT TOP (10) *
    FROM dbo.WebhookDeliveries WITH (ROWLOCK, UPDLOCK, READPAST)
    WHERE Status = 'Pending' AND NextAttemptAtUtc <= SYSUTCDATETIME()
      AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc < SYSUTCDATETIME())
    ORDER BY NextAttemptAtUtc
)
UPDATE due
SET LeaseToken = @leaseToken, LeaseExpiresAtUtc = DATEADD(minute, 5, SYSUTCDATETIME())
OUTPUT inserted.DeliveryId, inserted.LeaseToken, inserted.EventId, inserted.JobId, inserted.RunNo,
       inserted.ConsumerId, inserted.EventType, inserted.PayloadJson, inserted.Attempts;

-- [S11] Webhook sender: record one attempt. 2xx -> Delivered. Anything else — a redirect included, since
--       redirects are never followed — retries after 1, 5, 15, 60 min, then every 6 hours; the 8th failure is
--       dead-lettered. Connection failures alternate one immediate retry with the existing backoff.
--       :IMMEDIATE in LastError persists the scheduled retry kind. Only the lease holder records Pending rows.
UPDATE dbo.WebhookDeliveries
SET Attempts          = Attempts + 1,
    LastStatusCode    = @statusCode,               -- NULL on a timeout or connection error
    LastError         = CASE WHEN @statusCode IS NULL
                          AND @error IN ('CONNECTION_FAILED','TIMEOUT','TLS_FAILED')
                          AND Attempts + 1 < 8
                          AND (LastError IS NULL OR LastError NOT LIKE '%:IMMEDIATE')
                         THEN @error + ':IMMEDIATE' ELSE @error END,
    LeaseToken        = NULL,
    LeaseExpiresAtUtc = NULL,
    Status            = CASE WHEN @statusCode BETWEEN 200 AND 299 THEN 'Delivered'
                             WHEN Attempts + 1 >= 8 THEN 'DeadLettered'
                             ELSE 'Pending' END,
    DeliveredAtUtc    = CASE WHEN @statusCode BETWEEN 200 AND 299 THEN SYSUTCDATETIME() END,
    NextAttemptAtUtc  = DATEADD(minute, CASE WHEN @statusCode IS NULL
                         AND @error IN ('CONNECTION_FAILED','TIMEOUT','TLS_FAILED')
                         AND Attempts + 1 < 8
                         AND (LastError IS NULL OR LastError NOT LIKE '%:IMMEDIATE') THEN 0
                         ELSE CASE Attempts + 1 WHEN 1 THEN 1 WHEN 2 THEN 5 WHEN 3 THEN 15
                                                          WHEN 4 THEN 60 ELSE 360 END END, SYSUTCDATETIME())
WHERE DeliveryId = @deliveryId AND Status = 'Pending' AND LeaseToken = @leaseToken;

-- [S12] Operator: replay the dead-lettered deliveries of one consumer after its endpoint is fixed — only
--       events of each job's current run; consumers bound to older runs read theirs with [S13].
UPDATE d
SET Status = 'Pending', Attempts = 0, NextAttemptAtUtc = SYSUTCDATETIME(),
    LeaseToken = NULL, LeaseExpiresAtUtc = NULL, LastError = NULL
FROM dbo.WebhookDeliveries AS d
JOIN dbo.Jobs AS j ON j.JobId = d.JobId AND j.RunNo = d.RunNo
WHERE d.ConsumerId = @consumerId AND d.Status = 'DeadLettered';

-- [S13] API: GET /api/v1/jobs/{id}?runNo=n — the outcome of ONE run: the run the consumer is bound to (runNo),
--       or the current run when runNo is omitted. A finished run answers with its own status, error and result,
--       even after a newer run started. Another consumer's job, or a run that doesn't exist, is a 404.
--       A run still in progress has no error: the job's ErrorCode is then an internal retry reason
--       (AZURE_AUTH, AZURE_THROTTLED, ...) or a previous run's, and is never shown to the consumer.
SELECT j.JobId, r.RunNo, r.ClientRequestId,
       CASE WHEN r.Status = 'Active' THEN j.Status       ELSE r.Status       END AS Status,
       CASE WHEN r.Status = 'Active' THEN NULL           ELSE r.ErrorCode    END AS ErrorCode,
       CASE WHEN r.Status = 'Active' THEN NULL           ELSE r.ErrorMessage END AS ErrorMessage,
       r.CreatedAtUtc, r.CompletedAtUtc, r.PageCount, res.ContractVersion, res.ResultJson, res.FlaggedFields,
       j.DocumentTypeCode AS RequestedDocumentTypeCode, r.ResolvedDocumentTypeCode, r.ResultType,
       r.Reason AS RunReason, r.ChangesJson
FROM dbo.Jobs AS j
JOIN dbo.JobRuns AS r ON r.JobId = j.JobId AND r.RunNo = COALESCE(@runNo, j.RunNo)
LEFT JOIN dbo.JobResults AS res ON res.JobId = r.JobId AND res.RunNo = r.RunNo
WHERE j.JobId = @jobId AND j.ConsumerId = @consumerId AND j.IsDeleted = 0;

-- [S14] Model activation: lock the new model, take its group (document type, line, kind) FROM it, retire
--       the active model of that group and activate the new one — in one transaction. A classifier needs
--       its class map first.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @doc varchar(20), @line varchar(20), @kind varchar(10);
SELECT @doc = DocumentTypeCode, @line = ShippingLineCode, @kind = Kind
FROM dbo.ModelRegistry WITH (UPDLOCK, HOLDLOCK)
WHERE ModelId = @newModelId AND Status = 'Testing' AND TestFieldAccuracy >= @minimumAccuracy;
IF @doc IS NULL THROW 50001, 'The new model is not in Testing or is below the accuracy gate.', 1;
IF @kind = 'extract' AND NOT EXISTS (
    SELECT 1 FROM dbo.ModelMappingBindings b WITH (UPDLOCK,HOLDLOCK)
    JOIN dbo.MappingProfiles p WITH (UPDLOCK,HOLDLOCK) ON p.ProfileId=b.ProfileId
    WHERE b.ModelId=@newModelId AND p.DocumentTypeCode=@doc)
    THROW 51205, 'Extract activation requires a matching mapping binding; register with -MappingFile.', 1;
IF @kind = 'classify'
BEGIN
    -- HOLDLOCK protects the class map and target groups until activation commits.
    IF NOT EXISTS (SELECT 1 FROM dbo.ClassifierClasses WITH (UPDLOCK, HOLDLOCK)
                   WHERE ClassifierModelId = @newModelId AND TargetDocumentTypeCode IS NOT NULL)
        THROW 50003, 'A classifier needs at least one routable class before activation.', 1;
    IF EXISTS (SELECT 1 FROM dbo.ClassifierClasses AS c WITH (UPDLOCK, HOLDLOCK)
               WHERE c.ClassifierModelId = @newModelId
                 AND ((c.TargetDocumentTypeCode IS NULL AND c.ShippingLineCode IS NOT NULL)
                   OR c.TargetDocumentTypeCode = 'AUTO'
                   OR (@doc <> 'AUTO' AND (c.TargetDocumentTypeCode IS NULL OR c.TargetDocumentTypeCode <> @doc))
                   OR (c.TargetDocumentTypeCode IS NOT NULL AND NOT EXISTS
                       (SELECT 1 FROM dbo.ModelRegistry AS t WITH (UPDLOCK, HOLDLOCK)
                        WHERE t.DocumentTypeCode = c.TargetDocumentTypeCode AND t.Kind = 'extract'
                          AND (t.ShippingLineCode = c.ShippingLineCode OR (t.ShippingLineCode IS NULL AND c.ShippingLineCode IS NULL))))))
        THROW 50003, 'Classifier targets must identify supported extraction groups or intentional AUTO rejection.', 1;
END;

UPDATE dbo.ModelRegistry WITH (HOLDLOCK)
SET Status = 'Retired', RetiredAtUtc = SYSUTCDATETIME()
WHERE DocumentTypeCode = @doc AND Kind = @kind AND Status = 'Active'
  AND (ShippingLineCode = @line OR (ShippingLineCode IS NULL AND @line IS NULL));

UPDATE dbo.ModelRegistry SET Status = 'Active', ActivatedAtUtc = SYSUTCDATETIME() WHERE ModelId = @newModelId;
COMMIT;

-- [S15] Retention (a daily background job, 02:00 UTC), in batches of 5,000. Originals: Blob lifecycle rule,
--       90 days; raw results: deleted after [S8], lifecycle rule 30 days. Results and submissions: 13 months.
--       Delivered or superseded webhooks: 90 days. The ledger, the runs and the operation records are never
--       purged. A soft-deleted job keeps no file name (it can name a client).
EXEC dbo.ReconcilePageReservations;

DECLARE @cutoff datetime2(3) = DATEADD(month, -13, SYSUTCDATETIME());

WHILE 1 = 1
BEGIN
    DELETE TOP (5000) FROM dbo.JobResults WHERE CreatedAtUtc < @cutoff;
    IF @@ROWCOUNT < 5000 BREAK;
END

WHILE 1 = 1
BEGIN
    DELETE TOP (5000) FROM dbo.JobSubmissions WHERE CreatedAtUtc < @cutoff;
    IF @@ROWCOUNT < 5000 BREAK;
END

WHILE 1 = 1
BEGIN
    UPDATE TOP (5000) dbo.Jobs
    SET IsDeleted = 1, FileName = NULL
    WHERE IsDeleted = 0 AND Status IN ('Completed', 'Failed') AND CompletedAtUtc < @cutoff;
    IF @@ROWCOUNT < 5000 BREAK;
END

WHILE 1 = 1
BEGIN
    DELETE TOP (5000) FROM dbo.WebhookDeliveries
    WHERE Status IN ('Delivered', 'Superseded')
      AND COALESCE(DeliveredAtUtc, CreatedAtUtc) < DATEADD(day, -90, SYSUTCDATETIME());
    IF @@ROWCOUNT < 5000 BREAK;
END

-- [S16] API: GET /api/v1/catalog. Extraction models make the list a consumer shows; an active classifier for
--       a document type makes "detect the line" available for it. Cached for 60 seconds.
SELECT DocumentTypeCode, ShippingLineCode, Kind, ModelId, MinFieldConfidence
FROM dbo.ModelRegistry
WHERE Status = 'Active' AND (DocumentTypeCode <> 'AUTO' OR Kind = 'classify')
ORDER BY DocumentTypeCode, Kind DESC, ShippingLineCode;

-- [S17] Webhook sender: what goes with one event — the job's correlation id (sent back in X-Correlation-Id)
--       and the result of the event's own run, which the body carries next to the event (NULL for a failed
--       run). One row while the job exists.
SELECT j.CorrelationId, r.ResultJson
FROM dbo.Jobs AS j
LEFT JOIN dbo.JobResults AS r ON r.JobId = j.JobId AND r.RunNo = @runNo
WHERE j.JobId = @jobId;

-- [S18] Worker: the classifier succeeded. The operation and its ledger row commit together with the decision:
--       a mapped, confident class moves the job to Stage = 'Extract'; otherwise (@resolvedDocumentTypeCode NULL) the job and
--       its run fail UNCLASSIFIED with their job.failed event, in the same transaction — so a crash can never
--       lead to classifying (and paying) again. Pricing and month as in [S8a].
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @quotaConsumerId smallint=(SELECT ConsumerId FROM dbo.Jobs WHERE JobId=@jobId);
EXEC dbo.AcquirePageQuotaLock @quotaConsumerId;

IF @resolvedDocumentTypeCode = 'AUTO' OR EXISTS
    (SELECT 1 FROM dbo.Jobs WHERE JobId = @jobId AND DocumentTypeCode <> 'AUTO'
     AND @resolvedDocumentTypeCode IS NOT NULL AND DocumentTypeCode <> @resolvedDocumentTypeCode)
    THROW 50014, 'Resolved document type does not match the requested route.', 1;

UPDATE dbo.Jobs
SET Stage             = CASE WHEN @resolvedDocumentTypeCode IS NULL THEN Stage ELSE 'Extract' END,
    ShippingLineCode  = @resolvedLine,
    ResolvedDocumentTypeCode = CASE WHEN DocumentTypeCode = 'AUTO' THEN @resolvedDocumentTypeCode ELSE DocumentTypeCode END,
    Status            = CASE WHEN @resolvedDocumentTypeCode IS NULL THEN 'Failed' ELSE Status END,
    LeaseToken        = CASE WHEN @resolvedDocumentTypeCode IS NULL THEN NULL ELSE LeaseToken END,
    LeaseExpiresAtUtc = CASE WHEN @resolvedDocumentTypeCode IS NULL THEN NULL ELSE DATEADD(second, 60, SYSUTCDATETIME()) END,
    CompletedAtUtc    = CASE WHEN @resolvedDocumentTypeCode IS NULL THEN SYSUTCDATETIME() END,
    ErrorCode         = CASE WHEN @resolvedDocumentTypeCode IS NULL THEN 'UNCLASSIFIED' END,
    ErrorMessage      = CASE WHEN @resolvedDocumentTypeCode IS NULL THEN LEFT(@errorMessage, 400) END
WHERE JobId = @jobId AND Status = 'Processing' AND LeaseToken = @leaseToken AND Stage = 'Classify';
IF @@ROWCOUNT = 0 THROW 50010, 'Lease lost: another worker owns this job.', 1;

UPDATE dbo.AzureOperations
SET State = 'Succeeded', Pages = @pages, ClassName = @className, ClassConfidence = @classConfidence,
    CompletedAtUtc = @azureCompletedAtUtc
WHERE OperationId = @operationId AND State = 'Accepted';
IF @@ROWCOUNT = 0 THROW 50012, 'The operation is not open.', 1;

DECLARE @price decimal(9,4) = (SELECT TOP (1) PricePer1000 FROM dbo.AzureMeterPrices
                               WHERE MeterCode = 'DI_CUSTOM_CLASSIFY'
                                 AND EffectiveFrom <= CAST(@azureCompletedAtUtc AS date)
                               ORDER BY EffectiveFrom DESC);

INSERT dbo.UsageLedger (ConsumerId, JobId, RunNo, OperationId, MeterCode, ModelId, Pages, PricePer1000,
                        OccurredAtUtc, CorrelationId)
SELECT ConsumerId, JobId, RunNo, @operationId, 'DI_CUSTOM_CLASSIFY', @classifierModelId, @pages, @price,
       @azureCompletedAtUtc, CorrelationId
FROM dbo.Jobs WHERE JobId = @jobId;

UPDATE r SET ResolvedDocumentTypeCode = j.ResolvedDocumentTypeCode
FROM dbo.JobRuns AS r JOIN dbo.Jobs AS j ON j.JobId = r.JobId AND j.RunNo = r.RunNo
WHERE r.JobId = @jobId;

IF @resolvedDocumentTypeCode IS NULL
BEGIN
    UPDATE r
    SET Status = 'Failed', ErrorCode = 'UNCLASSIFIED', ErrorMessage = LEFT(@errorMessage, 400),
        CompletedAtUtc = SYSUTCDATETIME()
    FROM dbo.JobRuns AS r JOIN dbo.Jobs AS j ON j.JobId = r.JobId AND j.RunNo = r.RunNo
    WHERE r.JobId = @jobId;

    INSERT dbo.WebhookDeliveries (EventId, JobId, RunNo, ConsumerId, EventType, PayloadJson)
    SELECT @eventId, j.JobId, j.RunNo, j.ConsumerId, 'job.failed', @payloadJson
    FROM dbo.Jobs AS j
    JOIN dbo.Consumers AS c ON c.ConsumerId = j.ConsumerId
    WHERE j.JobId = @jobId AND c.CallbackUrl IS NOT NULL;
END

DECLARE @quotaRunNo smallint=(SELECT RunNo FROM dbo.Jobs WHERE JobId=@jobId);
EXEC dbo.SettlePageReservation @jobId,@quotaRunNo;
COMMIT;

-- [S19] Worker, right after [S3]: where the current run stands — always one row. An open operation:
--       Accepted -> resume polling; Sending -> close it Unknown with [S4c] and submit once more ([S4a] refuses
--       after the second Unknown). Stage = 'Map' -> map the saved raw result (MapRawResultPath) with the model
--       that produced it (MapModelId, its threshold from [S22]) and run [S8].
SELECT j.RunNo, j.Stage,
       o.OperationId, o.Kind, o.ModelId, o.State, o.OperationLocation, o.AcceptedAtUtc,
       COALESCE(u.UnknownInRun, 0) AS UnknownInRun,
       m.OperationId AS MapOperationId, m.RawResultPath AS MapRawResultPath, m.ModelId AS MapModelId,
       j.DocumentTypeCode AS RequestedDocumentTypeCode, j.ResolvedDocumentTypeCode, j.ShippingLineCode
FROM dbo.Jobs AS j
OUTER APPLY (SELECT TOP (1) x.* FROM dbo.AzureOperations AS x
             WHERE x.JobId = j.JobId AND x.RunNo = j.RunNo AND x.State IN ('Sending', 'Accepted')
             ORDER BY x.OperationId DESC) AS o
OUTER APPLY (SELECT COUNT(*) AS UnknownInRun FROM dbo.AzureOperations AS y
             WHERE y.JobId = j.JobId AND y.RunNo = j.RunNo AND y.State = 'Unknown') AS u
OUTER APPLY (SELECT TOP (1) z.OperationId, z.RawResultPath, z.ModelId FROM dbo.AzureOperations AS z
             WHERE z.JobId = j.JobId AND z.RunNo = j.RunNo AND z.Kind = 'extract' AND z.State = 'Succeeded'
             ORDER BY z.OperationId DESC) AS m
WHERE j.JobId = @jobId;

-- [S20] Worker: map a classifier's class to a shipping line, with the classifier's confidence threshold
--       (cached with the registry for 60 seconds).
SELECT c.ShippingLineCode, m.MinFieldConfidence AS MinClassConfidence, c.TargetDocumentTypeCode
FROM dbo.ClassifierClasses AS c
JOIN dbo.ModelRegistry AS m ON m.ModelId = c.ClassifierModelId
WHERE c.ClassifierModelId = @classifierModelId AND c.ClassName = @className;

-- [S21] API: the active consumers, cached in memory for 60 seconds. The API key is checked in memory: SHA-256 of
--       the presented key compared in constant time with ApiKeyHash1 and ApiKeyHash2 (two slots for a rotation),
--       so no key or hash is ever sent to SQL as a parameter. A consumer switched off (IsActive = 0) drops out
--       of the cache within 60 seconds.
SELECT ConsumerId, Code, TenantCode, ApiKeyHash1, ApiKeyHash2, MaxPagesPerJob, DailyPageCap,
       CONVERT(bit, CASE WHEN CallbackUrl IS NULL THEN 0 ELSE 1 END) AS HasCallback
FROM dbo.Consumers
WHERE IsActive = 1;

-- [S23] Webhook sender: where and how each consumer is called — the registered callback URL (never one from
--       a request), the Key Vault secret names of its signing secret and of its private CA, if any. Cached for
--       60 seconds. A consumer switched off, or without a callback any more, gets no POST: its deliveries record
--       failed attempts and end DeadLettered, and nothing is sent.
SELECT ConsumerId, Code, CallbackUrl, WebhookSecretName, CallbackCaCertName
FROM dbo.Consumers
WHERE IsActive = 1 AND CallbackUrl IS NOT NULL;

-- [S24] Worker, every minute: how long the oldest DUE job has waited without being claimed (NULL: none is due).
--       Due means what [S3] claims: Queued and past its NextAttemptAtUtc, or Processing with an expired lease.
--       A job in its back-off, or released until later, is not due yet and never counts. Over 5 minutes, the
--       "Queue stuck" alert fires (section 12).
SELECT DATEDIFF(second, MIN(d.DueAtUtc), SYSUTCDATETIME()) AS OldestDueSeconds
FROM (SELECT CASE WHEN Status = 'Queued' THEN NextAttemptAtUtc ELSE LeaseExpiresAtUtc END AS DueAtUtc
      FROM dbo.Jobs
      WHERE IsDeleted = 0
        AND (   (Status = 'Queued'     AND NextAttemptAtUtc  <= SYSUTCDATETIME())
             OR (Status = 'Processing' AND LeaseExpiresAtUtc <  SYSUTCDATETIME()))) AS d;

-- [S22] Worker: the field-confidence threshold of the model that produced a result, whatever its status now —
--       a model retired between the POST and the mapping still maps with its own threshold. Cached with the
--       registry for 60 seconds.
SELECT MinFieldConfidence
FROM dbo.ModelRegistry
WHERE ModelId = @modelId;

-- [S25] Intake only: fresh active ids for the latest completed route; never the cached catalog.
-- Replays skip this read's profile resolution entirely. S2 validates the route/run under locks.
SELECT j.RunNo, r.ResolvedDocumentTypeCode, j.ShippingLineCode,
       c.ModelId AS ClassifierModelId, e.ModelId AS ExtractModelId
FROM dbo.Jobs AS j
JOIN dbo.JobRuns AS r ON r.JobId = j.JobId AND r.RunNo = j.RunNo AND r.Status = 'Completed'
LEFT JOIN dbo.ModelRegistry AS c ON @documentTypeCode = 'AUTO' AND c.DocumentTypeCode = 'AUTO'
    AND c.Kind = 'classify' AND c.Status = 'Active' AND c.ShippingLineCode IS NULL
LEFT JOIN dbo.ModelRegistry AS e ON e.DocumentTypeCode = r.ResolvedDocumentTypeCode
    AND e.Kind = 'extract' AND e.Status = 'Active'
    AND (e.ShippingLineCode = j.ShippingLineCode OR (e.ShippingLineCode IS NULL AND j.ShippingLineCode IS NULL))
WHERE j.ConsumerId = @consumerId AND j.DocumentTypeCode = @documentTypeCode
    AND j.FileSha256 = @fileSha256 AND j.IdemLine = @idemLine AND j.IsDeleted = 0
    AND j.Status = 'Completed' AND @reprocess = 0
    AND NOT EXISTS (SELECT 1 FROM dbo.JobSubmissions WHERE ConsumerId = @consumerId AND ClientRequestId = @clientRequestId);

-- [S26] Consumer usage: one RCSI statement snapshot, no cache. @asOfUtc fixes UTC reporting boundaries.
SELECT @asOfUtc AS AsOfUtc, c.QuotaMode, c.QuotaWarningPercent AS WarningPercent,
       COALESCE(g.Pages,0) Granted, COALESCE(ch.Pages,0) Used, COALESCE(r.Pages,0) Reserved,
       b.AvailablePages Remaining, t.BalanceAfter CycleBase, t.RecordedAtUtc LastTopUpAtUtc,
       t.Pages LastTopUpPages, c.MaxPagesPerJob, c.DailyPageCap DailyCap,
       COALESCE(d.Pages,0) SubmittedTodayPages, COALESCE(ch.TodayPages,0) ChargedTodayPages,
       (SELECT CONVERT(char(7), m.StartAt, 126) [month], COALESCE(h.Pages,0) chargedPages,
               COALESCE(h.Documents,0) chargedDocuments
        FROM (SELECT DATEADD(month, -n.n, DATEFROMPARTS(YEAR(@asOfUtc),MONTH(@asOfUtc),1)) StartAt
              FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),
                          (12),(13),(14),(15),(16),(17),(18),(19),(20),(21),(22),(23)) n(n)
              WHERE n.n < @months) m
        OUTER APPLY (SELECT SUM(x.Pages) Pages, COUNT_BIG(*) Documents FROM dbo.CustomerPageCharges x
                     WHERE x.ConsumerId=c.ConsumerId AND x.RecordedAtUtc>=m.StartAt
                       AND x.RecordedAtUtc<DATEADD(month,1,m.StartAt)) h
        ORDER BY m.StartAt FOR JSON PATH) MonthsJson
FROM dbo.Consumers c
JOIN dbo.ConsumerPageBalance b ON b.ConsumerId=c.ConsumerId
OUTER APPLY (SELECT SUM(Pages) Pages FROM dbo.PageAllowanceGrants WHERE ConsumerId=c.ConsumerId) g
OUTER APPLY (SELECT SUM(Pages) Pages,
            SUM(CASE WHEN RecordedAtUtc>=CONVERT(date,@asOfUtc) AND RecordedAtUtc<DATEADD(day,1,CONVERT(date,@asOfUtc)) THEN Pages ELSE 0 END) TodayPages
            FROM dbo.CustomerPageCharges WHERE ConsumerId=c.ConsumerId) ch
OUTER APPLY (SELECT SUM(Pages) Pages FROM dbo.PageReservations WHERE ConsumerId=c.ConsumerId AND State='Reserved') r
OUTER APPLY (SELECT TOP(1) g.BalanceAfter,g.RecordedAtUtc,g.Pages FROM dbo.PageAllowanceGrants g
             WHERE g.ConsumerId=c.ConsumerId AND g.Kind='TopUp'
               AND NOT EXISTS (SELECT 1 FROM dbo.PageAllowanceGrants r WHERE r.Kind='Reversal' AND r.OriginalGrantId=g.GrantId)
             ORDER BY g.RecordedAtUtc DESC,g.GrantId DESC) t
OUTER APPLY (SELECT SUM(CONVERT(bigint,PageCount)) Pages FROM dbo.JobRuns WHERE ConsumerId=c.ConsumerId
             AND CreatedAtUtc>=CONVERT(date,@asOfUtc) AND CreatedAtUtc<DATEADD(day,1,CONVERT(date,@asOfUtc))) d
WHERE c.ConsumerId=@consumerId;

-- [S27] Customer reporting periods, NOT FIFO. Parameters: @consumerId smallint, @grantId bigint NULL (all), 0 (prelude), positive (TopUp).
-- OrderingPolicy TimestampGrantBeforeChargeV1: (RecordedAtUtc, EventKind grant=0/charge=1, EventId).
-- All TopUps are permanent boundaries, including reversed ones. Coverage excludes history not backfilled by 06.
-- One statement / one RCSI snapshot. Scalar subquery avoids FOR JSON's 2033-character result-row splitting.
;WITH Events AS (
 SELECT RecordedAtUtc,0 EventKind,GrantId EventId,Pages,Kind FROM dbo.PageAllowanceGrants WHERE ConsumerId=@consumerId
 UNION ALL
 SELECT RecordedAtUtc,1,ChargeId,-Pages,'Charge' FROM dbo.CustomerPageCharges WHERE ConsumerId=@consumerId
), Ordered AS (
 SELECT *,ROW_NUMBER() OVER(ORDER BY RecordedAtUtc,EventKind,EventId) Seq,
 SUM(Pages) OVER(ORDER BY RecordedAtUtc,EventKind,EventId ROWS UNBOUNDED PRECEDING) Ledger
 FROM Events
), Assigned AS (
 SELECT *,COALESCE(MAX(CASE WHEN Kind='TopUp' THEN Seq END) OVER(ORDER BY Seq ROWS UNBOUNDED PRECEDING),0) CycleSeq
 FROM Ordered
), Buckets AS (
 SELECT CycleSeq,
 SUM(CASE WHEN Kind='Adjustment' THEN Pages ELSE CONVERT(bigint,0) END) AdjustmentPages,
 SUM(CASE WHEN Kind='Reversal' THEN Pages ELSE CONVERT(bigint,0) END) ReversalPages,
 -SUM(CASE WHEN Kind='Charge' THEN Pages ELSE CONVERT(bigint,0) END) ChargedPages,
 SUM(CASE WHEN Kind='Charge' THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END) ChargedRuns
 FROM Assigned GROUP BY CycleSeq
), Boundaries AS (
 SELECT Seq,EventId TopUpGrantId,RecordedAtUtc StartAtUtc,Pages TopUpPages,Ledger OpeningLedgerBalance,
 LEAD(EventId) OVER(ORDER BY Seq) NextTopUpGrantId,LEAD(RecordedAtUtc) OVER(ORDER BY Seq) EndAtUtc
 FROM Ordered WHERE Kind='TopUp'
), Starts AS (
 SELECT * FROM Boundaries
 UNION ALL
 SELECT 0,NULL,NULL,CONVERT(bigint,0),CONVERT(bigint,0),
 (SELECT TOP(1) TopUpGrantId FROM Boundaries ORDER BY Seq),(SELECT MIN(StartAtUtc) FROM Boundaries)
), Cycles AS (
 SELECT s.Seq,s.TopUpGrantId,s.StartAtUtc,s.NextTopUpGrantId,s.EndAtUtc,s.TopUpPages,
 CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.PageAllowanceGrants r WHERE r.ConsumerId=@consumerId AND r.Kind='Reversal' AND r.OriginalGrantId=s.TopUpGrantId) THEN 1 ELSE 0 END) IsReversed,
 g.BalanceAfter RecordedAvailableAfterTopUp,s.OpeningLedgerBalance,
 COALESCE(b.AdjustmentPages,0) AdjustmentPages,COALESCE(b.ReversalPages,0) ReversalPages,
 COALESCE(b.ChargedPages,0) ChargedPages,COALESCE(b.ChargedRuns,0) ChargedRuns,
 s.OpeningLedgerBalance+COALESCE(b.AdjustmentPages,0)+COALESCE(b.ReversalPages,0)-COALESCE(b.ChargedPages,0) ClosingLedgerBalance
 FROM Starts s LEFT JOIN Buckets b ON b.CycleSeq=s.Seq
 LEFT JOIN dbo.PageAllowanceGrants g ON g.GrantId=s.TopUpGrantId AND g.ConsumerId=@consumerId
)
SELECT (SELECT 'TimestampGrantBeforeChargeV1' orderingPolicy,'RecordedCustomerCharges' coverage,
 JSON_QUERY((SELECT
 COALESCE((SELECT SUM(Pages) FROM dbo.PageAllowanceGrants WHERE ConsumerId=@consumerId),0) grantedPages,
 COALESCE((SELECT SUM(Pages) FROM dbo.CustomerPageCharges WHERE ConsumerId=@consumerId),0) chargedPages,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerPageCharges WHERE ConsumerId=@consumerId) chargedRuns,
 COALESCE((SELECT SUM(Pages) FROM dbo.PageAllowanceGrants WHERE ConsumerId=@consumerId),0)-COALESCE((SELECT SUM(Pages) FROM dbo.CustomerPageCharges WHERE ConsumerId=@consumerId),0) ledgerBalance,
 COALESCE((SELECT SUM(Pages) FROM dbo.PageReservations WHERE ConsumerId=@consumerId AND State='Reserved'),0) currentReservedPages,
 (SELECT AvailablePages FROM dbo.ConsumerPageBalance WHERE ConsumerId=@consumerId) currentAvailablePages
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)) totals,
 JSON_QUERY(NULLIF((SELECT TopUpGrantId topUpGrantId,TODATETIMEOFFSET(StartAtUtc,'+00:00') startAtUtc,NextTopUpGrantId nextTopUpGrantId,TODATETIMEOFFSET(EndAtUtc,'+00:00') endAtUtc,TopUpPages topUpPages,IsReversed isReversed,
 RecordedAvailableAfterTopUp recordedAvailableAfterTopUp,OpeningLedgerBalance openingLedgerBalance,AdjustmentPages adjustmentPages,ReversalPages reversalPages,
 ChargedPages chargedPages,ChargedRuns chargedRuns,ClosingLedgerBalance closingLedgerBalance
 FROM Cycles WHERE Seq=0 AND (@grantId IS NULL OR @grantId=0) FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),'')) prelude,
 JSON_QUERY(COALESCE((SELECT TOP(200) TopUpGrantId topUpGrantId,TODATETIMEOFFSET(StartAtUtc,'+00:00') startAtUtc,NextTopUpGrantId nextTopUpGrantId,TODATETIMEOFFSET(EndAtUtc,'+00:00') endAtUtc,TopUpPages topUpPages,IsReversed isReversed,
 RecordedAvailableAfterTopUp recordedAvailableAfterTopUp,OpeningLedgerBalance openingLedgerBalance,AdjustmentPages adjustmentPages,ReversalPages reversalPages,
 ChargedPages chargedPages,ChargedRuns chargedRuns,ClosingLedgerBalance closingLedgerBalance
 FROM Cycles WHERE Seq>0 AND (@grantId IS NULL OR TopUpGrantId=@grantId) ORDER BY Seq DESC FOR JSON PATH,INCLUDE_NULL_VALUES),'[]')) cycles,
 CONVERT(bit,CASE WHEN @grantId IS NULL AND (SELECT COUNT_BIG(*) FROM Boundaries)>200 THEN 1 ELSE 0 END) hasMoreCycles
 FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER) Json
WHERE EXISTS(SELECT 1 FROM dbo.Consumers WHERE ConsumerId=@consumerId)
 AND (@grantId IS NULL OR @grantId=0 OR EXISTS(SELECT 1 FROM Boundaries WHERE TopUpGrantId=@grantId));

-- [S28] Documents in a reporting period. @grantId bigint: 0=prelude; @limit int 1..500; @afterChargeId bigint NULL.
-- TimestampGrantBeforeChargeV1: every grant at a timestamp precedes every charge there; last TopUp GrantId wins ties.
-- Cursor is validated in the assigned cycle before filtering. Foreign/nonexistent/wrong-cycle cursors return no row (404).
;WITH Assigned AS (
 SELECT ch.ChargeId,ch.JobId,ch.RunNo,ch.Pages,ch.RecordedAtUtc,
 COALESCE(t.GrantId,0) CycleId
 FROM dbo.CustomerPageCharges ch
 OUTER APPLY(SELECT TOP(1) g.GrantId FROM dbo.PageAllowanceGrants g
 WHERE g.ConsumerId=@consumerId AND g.Kind='TopUp' AND g.RecordedAtUtc<=ch.RecordedAtUtc
 ORDER BY g.RecordedAtUtc DESC,g.GrantId DESC)t
 WHERE ch.ConsumerId=@consumerId
), Page AS (
 SELECT TOP(@limit+1) a.ChargeId,a.JobId,a.RunNo,a.Pages,a.RecordedAtUtc,
 r.ResolvedDocumentTypeCode,r.Reason RunReason
 FROM Assigned a JOIN dbo.JobRuns r ON r.JobId=a.JobId AND r.RunNo=a.RunNo AND r.ConsumerId=@consumerId
 WHERE a.CycleId=@grantId AND (@afterChargeId IS NULL OR a.ChargeId>@afterChargeId)
 ORDER BY a.ChargeId
)
SELECT (SELECT NULLIF(@grantId,0) topUpGrantId,
 JSON_QUERY(COALESCE((SELECT TOP(@limit) ChargeId chargeId,JobId jobId,RunNo runNo,Pages pages,
 TODATETIMEOFFSET(RecordedAtUtc,'+00:00') chargedAtUtc,ResolvedDocumentTypeCode resolvedDocumentTypeCode,RunReason runReason
 FROM Page ORDER BY ChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),'[]')) documents,
 CASE WHEN (SELECT COUNT(*) FROM Page)>@limit THEN (SELECT MAX(ChargeId) FROM (SELECT TOP(@limit) ChargeId FROM Page ORDER BY ChargeId)p) END nextAfterChargeId
 FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER) Json
WHERE EXISTS(SELECT 1 FROM dbo.Consumers WHERE ConsumerId=@consumerId)
 AND (@grantId=0 OR EXISTS(SELECT 1 FROM dbo.PageAllowanceGrants WHERE ConsumerId=@consumerId AND GrantId=@grantId AND Kind='TopUp'))
 AND (@afterChargeId IS NULL OR EXISTS(SELECT 1 FROM Assigned WHERE CycleId=@grantId AND ChargeId=@afterChargeId));

-- [S29] IST operator only: Azure list-price estimates; Azure invoice is authoritative.
-- Same single RCSI snapshot and parameters as S27. Per-page cost uses charged runs only; uncharged cost is reported apart.
-- Unknown costs contribute zero to sums and are counted as unpriced; prices use UTC today.
-- Customer reporting periods, NOT FIFO. Parameters: @consumerId smallint, @grantId bigint NULL (all), 0 (prelude), positive (TopUp).
-- OrderingPolicy TimestampGrantBeforeChargeV1: (RecordedAtUtc, EventKind grant=0/charge=1, EventId).
-- All TopUps are permanent boundaries, including reversed ones. Coverage excludes history not backfilled by 06.
-- One statement / one RCSI snapshot. Scalar subquery avoids FOR JSON's 2033-character result-row splitting.
;WITH Ledger AS (
 SELECT l.MeterCode,l.Pages,l.PricePer1000,l.AzureCostUsd,ch.ChargeId,
 COALESCE(t.GrantId,0) CycleId
 FROM dbo.UsageLedger l
 LEFT JOIN dbo.CustomerPageCharges ch ON ch.ConsumerId=l.ConsumerId AND ch.JobId=l.JobId AND ch.RunNo=l.RunNo
 OUTER APPLY(SELECT TOP(1) g.GrantId FROM dbo.PageAllowanceGrants g
 WHERE g.ConsumerId=ch.ConsumerId AND g.Kind='TopUp' AND g.RecordedAtUtc<=ch.RecordedAtUtc
 ORDER BY g.RecordedAtUtc DESC,g.GrantId DESC)t
 WHERE l.ConsumerId=@consumerId
), Costs AS (
 SELECT CycleId,
 SUM(CASE WHEN MeterCode='DI_CUSTOM_CLASSIFY' THEN CONVERT(bigint,Pages) ELSE CONVERT(bigint,0) END) AzureClassifyPages,
 SUM(CASE WHEN MeterCode='DI_CUSTOM_EXTRACT' THEN CONVERT(bigint,Pages) ELSE CONVERT(bigint,0) END) AzureExtractPages,
 COALESCE(SUM(AzureCostUsd),0) AzureCostUsd,
 SUM(CASE WHEN PricePer1000 IS NULL THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END) UnpricedRows
 FROM Ledger WHERE ChargeId IS NOT NULL GROUP BY CycleId
), Events AS (
 SELECT RecordedAtUtc,0 EventKind,GrantId EventId,Pages,Kind FROM dbo.PageAllowanceGrants WHERE ConsumerId=@consumerId
 UNION ALL
 SELECT RecordedAtUtc,1,ChargeId,-Pages,'Charge' FROM dbo.CustomerPageCharges WHERE ConsumerId=@consumerId
), Ordered AS (
 SELECT *,ROW_NUMBER() OVER(ORDER BY RecordedAtUtc,EventKind,EventId) Seq,
 SUM(Pages) OVER(ORDER BY RecordedAtUtc,EventKind,EventId ROWS UNBOUNDED PRECEDING) Ledger
 FROM Events
), Assigned AS (
 SELECT *,COALESCE(MAX(CASE WHEN Kind='TopUp' THEN Seq END) OVER(ORDER BY Seq ROWS UNBOUNDED PRECEDING),0) CycleSeq
 FROM Ordered
), Buckets AS (
 SELECT CycleSeq,
 SUM(CASE WHEN Kind='Adjustment' THEN Pages ELSE CONVERT(bigint,0) END) AdjustmentPages,
 SUM(CASE WHEN Kind='Reversal' THEN Pages ELSE CONVERT(bigint,0) END) ReversalPages,
 -SUM(CASE WHEN Kind='Charge' THEN Pages ELSE CONVERT(bigint,0) END) ChargedPages,
 SUM(CASE WHEN Kind='Charge' THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END) ChargedRuns
 FROM Assigned GROUP BY CycleSeq
), Boundaries AS (
 SELECT Seq,EventId TopUpGrantId,RecordedAtUtc StartAtUtc,Pages TopUpPages,Ledger OpeningLedgerBalance,
 LEAD(EventId) OVER(ORDER BY Seq) NextTopUpGrantId,LEAD(RecordedAtUtc) OVER(ORDER BY Seq) EndAtUtc
 FROM Ordered WHERE Kind='TopUp'
), Starts AS (
 SELECT * FROM Boundaries
 UNION ALL
 SELECT 0,NULL,NULL,CONVERT(bigint,0),CONVERT(bigint,0),
 (SELECT TOP(1) TopUpGrantId FROM Boundaries ORDER BY Seq),(SELECT MIN(StartAtUtc) FROM Boundaries)
), Cycles AS (
 SELECT s.Seq,s.TopUpGrantId,s.StartAtUtc,s.NextTopUpGrantId,s.EndAtUtc,s.TopUpPages,
 CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.PageAllowanceGrants r WHERE r.ConsumerId=@consumerId AND r.Kind='Reversal' AND r.OriginalGrantId=s.TopUpGrantId) THEN 1 ELSE 0 END) IsReversed,
 g.BalanceAfter RecordedAvailableAfterTopUp,s.OpeningLedgerBalance,
 COALESCE(b.AdjustmentPages,0) AdjustmentPages,COALESCE(b.ReversalPages,0) ReversalPages,
 COALESCE(b.ChargedPages,0) ChargedPages,COALESCE(b.ChargedRuns,0) ChargedRuns,
 s.OpeningLedgerBalance+COALESCE(b.AdjustmentPages,0)+COALESCE(b.ReversalPages,0)-COALESCE(b.ChargedPages,0) ClosingLedgerBalance,
 COALESCE(c.AzureClassifyPages,0) AzureClassifyPages,COALESCE(c.AzureExtractPages,0) AzureExtractPages,
 COALESCE(c.AzureCostUsd,0) AzureCostUsd,COALESCE(c.UnpricedRows,0) UnpricedRows,
 CONVERT(decimal(28,10),CONVERT(decimal(28,10),COALESCE(c.AzureCostUsd,0))/NULLIF(CONVERT(decimal(19,0),b.ChargedPages),0)) AzureCostPerChargedPageUsd
 FROM Starts s LEFT JOIN Buckets b ON b.CycleSeq=s.Seq
 LEFT JOIN Costs c ON c.CycleId=COALESCE(s.TopUpGrantId,0)
 LEFT JOIN dbo.PageAllowanceGrants g ON g.GrantId=s.TopUpGrantId AND g.ConsumerId=@consumerId
)
SELECT (SELECT 'Estimate from Azure list prices; the Azure invoice is authoritative.' costNotice,'TimestampGrantBeforeChargeV1' orderingPolicy,'RecordedCustomerCharges' coverage,
 JSON_QUERY((SELECT
 COALESCE((SELECT SUM(Pages) FROM dbo.PageAllowanceGrants WHERE ConsumerId=@consumerId),0) grantedPages,
 COALESCE((SELECT SUM(Pages) FROM dbo.CustomerPageCharges WHERE ConsumerId=@consumerId),0) chargedPages,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerPageCharges WHERE ConsumerId=@consumerId) chargedRuns,
 COALESCE((SELECT SUM(Pages) FROM dbo.PageAllowanceGrants WHERE ConsumerId=@consumerId),0)-COALESCE((SELECT SUM(Pages) FROM dbo.CustomerPageCharges WHERE ConsumerId=@consumerId),0) ledgerBalance,
 COALESCE((SELECT SUM(Pages) FROM dbo.PageReservations WHERE ConsumerId=@consumerId AND State='Reserved'),0) currentReservedPages,
 (SELECT AvailablePages FROM dbo.ConsumerPageBalance WHERE ConsumerId=@consumerId) currentAvailablePages,
 COALESCE((SELECT SUM(AzureCostUsd) FROM Ledger),0) azureCostUsd,
 COALESCE((SELECT SUM(AzureCostUsd) FROM Ledger WHERE ChargeId IS NOT NULL),0) chargedRunsAzureCostUsd,
 COALESCE((SELECT SUM(AzureCostUsd) FROM Ledger WHERE ChargeId IS NULL),0) unchargedAzureCostUsd,
 (SELECT COUNT_BIG(*) FROM Ledger WHERE PricePer1000 IS NULL) unpricedRows,
 CONVERT(decimal(28,10),CONVERT(decimal(28,10),COALESCE((SELECT SUM(AzureCostUsd) FROM Ledger WHERE ChargeId IS NOT NULL),0))/NULLIF(CONVERT(decimal(19,0),(SELECT SUM(Pages) FROM dbo.CustomerPageCharges WHERE ConsumerId=@consumerId)),0)) azureCostPerChargedPageUsd,
 JSON_QUERY(COALESCE((SELECT p.MeterCode meterCode,p.PricePer1000 pricePer1000,p.EffectiveFrom effectiveFrom,p.Source source
 FROM dbo.AzureMeterPrices p WHERE p.MeterCode IN ('DI_CUSTOM_CLASSIFY','DI_CUSTOM_EXTRACT') AND p.EffectiveFrom<=CONVERT(date,SYSUTCDATETIME())
 AND NOT EXISTS(SELECT 1 FROM dbo.AzureMeterPrices newer WHERE newer.MeterCode=p.MeterCode AND newer.EffectiveFrom<=CONVERT(date,SYSUTCDATETIME()) AND newer.EffectiveFrom>p.EffectiveFrom)
 ORDER BY p.MeterCode FOR JSON PATH),'[]')) currentListPrices
 FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)) totals,
 JSON_QUERY(NULLIF((SELECT TopUpGrantId topUpGrantId,TODATETIMEOFFSET(StartAtUtc,'+00:00') startAtUtc,NextTopUpGrantId nextTopUpGrantId,TODATETIMEOFFSET(EndAtUtc,'+00:00') endAtUtc,TopUpPages topUpPages,IsReversed isReversed,
 RecordedAvailableAfterTopUp recordedAvailableAfterTopUp,OpeningLedgerBalance openingLedgerBalance,AdjustmentPages adjustmentPages,ReversalPages reversalPages,
 ChargedPages chargedPages,ChargedRuns chargedRuns,ClosingLedgerBalance closingLedgerBalance,
 AzureClassifyPages azureClassifyPages,AzureExtractPages azureExtractPages,AzureCostUsd azureCostUsd,UnpricedRows unpricedRows,AzureCostPerChargedPageUsd azureCostPerChargedPageUsd
 FROM Cycles WHERE Seq=0 AND (@grantId IS NULL OR @grantId=0) FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),'')) prelude,
 JSON_QUERY(COALESCE((SELECT TOP(200) TopUpGrantId topUpGrantId,TODATETIMEOFFSET(StartAtUtc,'+00:00') startAtUtc,NextTopUpGrantId nextTopUpGrantId,TODATETIMEOFFSET(EndAtUtc,'+00:00') endAtUtc,TopUpPages topUpPages,IsReversed isReversed,
 RecordedAvailableAfterTopUp recordedAvailableAfterTopUp,OpeningLedgerBalance openingLedgerBalance,AdjustmentPages adjustmentPages,ReversalPages reversalPages,
 ChargedPages chargedPages,ChargedRuns chargedRuns,ClosingLedgerBalance closingLedgerBalance,
 AzureClassifyPages azureClassifyPages,AzureExtractPages azureExtractPages,AzureCostUsd azureCostUsd,UnpricedRows unpricedRows,AzureCostPerChargedPageUsd azureCostPerChargedPageUsd
 FROM Cycles WHERE Seq>0 AND (@grantId IS NULL OR TopUpGrantId=@grantId) ORDER BY Seq DESC FOR JSON PATH,INCLUDE_NULL_VALUES),'[]')) cycles,
 CONVERT(bit,CASE WHEN @grantId IS NULL AND (SELECT COUNT_BIG(*) FROM Boundaries)>200 THEN 1 ELSE 0 END) hasMoreCycles
 FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER) Json
WHERE EXISTS(SELECT 1 FROM dbo.Consumers WHERE ConsumerId=@consumerId)
 AND (@grantId IS NULL OR @grantId=0 OR EXISTS(SELECT 1 FROM Boundaries WHERE TopUpGrantId=@grantId));


-- [S30] IST operator only: Azure list-price estimates; Azure invoice is authoritative.
-- Documents in a reporting period. @grantId bigint: 0=prelude; @limit int 1..500; @afterChargeId bigint NULL.
-- TimestampGrantBeforeChargeV1: every grant at a timestamp precedes every charge there; last TopUp GrantId wins ties.
-- Cursor is validated in the assigned cycle before filtering. Foreign/nonexistent/wrong-cycle cursors return no row (404).
;WITH Assigned AS (
 SELECT ch.ChargeId,ch.JobId,ch.RunNo,ch.Pages,ch.RecordedAtUtc,
 COALESCE(t.GrantId,0) CycleId
 FROM dbo.CustomerPageCharges ch
 OUTER APPLY(SELECT TOP(1) g.GrantId FROM dbo.PageAllowanceGrants g
 WHERE g.ConsumerId=@consumerId AND g.Kind='TopUp' AND g.RecordedAtUtc<=ch.RecordedAtUtc
 ORDER BY g.RecordedAtUtc DESC,g.GrantId DESC)t
 WHERE ch.ConsumerId=@consumerId
), Page AS (
 SELECT TOP(@limit+1) a.ChargeId,a.JobId,a.RunNo,a.Pages,a.RecordedAtUtc,
 r.ResolvedDocumentTypeCode,r.Reason RunReason,
 cost.AzureClassifyPages,cost.AzureExtractPages,cost.AzureCostUsd,cost.UnpricedRows
 FROM Assigned a JOIN dbo.JobRuns r ON r.JobId=a.JobId AND r.RunNo=a.RunNo AND r.ConsumerId=@consumerId
 OUTER APPLY(SELECT
 COALESCE(SUM(CASE WHEN l.MeterCode='DI_CUSTOM_CLASSIFY' THEN CONVERT(bigint,l.Pages) ELSE CONVERT(bigint,0) END),0) AzureClassifyPages,
 COALESCE(SUM(CASE WHEN l.MeterCode='DI_CUSTOM_EXTRACT' THEN CONVERT(bigint,l.Pages) ELSE CONVERT(bigint,0) END),0) AzureExtractPages,
 COALESCE(SUM(l.AzureCostUsd),0) AzureCostUsd,
 COUNT_BIG(CASE WHEN l.PricePer1000 IS NULL THEN 1 END) UnpricedRows
 FROM dbo.UsageLedger l WHERE l.ConsumerId=@consumerId AND l.JobId=a.JobId AND l.RunNo=a.RunNo) cost
 WHERE a.CycleId=@grantId AND (@afterChargeId IS NULL OR a.ChargeId>@afterChargeId)
 ORDER BY a.ChargeId
)
SELECT (SELECT 'Estimate from Azure list prices; the Azure invoice is authoritative.' costNotice,
 (SELECT COUNT_BIG(*) FROM dbo.UsageLedger WHERE ConsumerId=@consumerId AND PricePer1000 IS NULL) unpricedRows,NULLIF(@grantId,0) topUpGrantId,
 JSON_QUERY(COALESCE((SELECT TOP(@limit) ChargeId chargeId,JobId jobId,RunNo runNo,Pages pages,
 TODATETIMEOFFSET(RecordedAtUtc,'+00:00') chargedAtUtc,ResolvedDocumentTypeCode resolvedDocumentTypeCode,RunReason runReason,
 AzureClassifyPages azureClassifyPages,AzureExtractPages azureExtractPages,AzureCostUsd azureCostUsd
 FROM Page ORDER BY ChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),'[]')) documents,
 CASE WHEN (SELECT COUNT(*) FROM Page)>@limit THEN (SELECT MAX(ChargeId) FROM (SELECT TOP(@limit) ChargeId FROM Page ORDER BY ChargeId)p) END nextAfterChargeId
 FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER) Json
WHERE EXISTS(SELECT 1 FROM dbo.Consumers WHERE ConsumerId=@consumerId)
 AND (@grantId=0 OR EXISTS(SELECT 1 FROM dbo.PageAllowanceGrants WHERE ConsumerId=@consumerId AND GrantId=@grantId AND Kind='TopUp'))
 AND (@afterChargeId IS NULL OR EXISTS(SELECT 1 FROM Assigned WHERE CycleId=@grantId AND ChargeId=@afterChargeId));
-- [S31] Authenticated consumer is alive: accelerate only unleased connection failures.
-- LastError's :IMMEDIATE suffix marks the single immediate retry scheduled by S11.
UPDATE dbo.WebhookDeliveries WITH (ROWLOCK, UPDLOCK, READPAST)
SET NextAttemptAtUtc = SYSUTCDATETIME()
WHERE ConsumerId = @consumerId AND Status = 'Pending' AND LastStatusCode IS NULL
  AND LastError IN ('CONNECTION_FAILED','TIMEOUT','TLS_FAILED',
                    'CONNECTION_FAILED:IMMEDIATE','TIMEOUT:IMMEDIATE','TLS_FAILED:IMMEDIATE')
  AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc < SYSUTCDATETIME())
  AND NextAttemptAtUtc > SYSUTCDATETIME();
