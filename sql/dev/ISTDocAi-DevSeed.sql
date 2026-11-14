-- Development seed. Select the migrated ISTDocAi database in SSMS. No SQLCMD mode.
-- BEGIN SETTINGS
DECLARE @ConsumerCode varchar(100)='FWD-UNF', @ProductCode varchar(100)='FWD', @TenantCode varchar(100)='UNF',
 @Name nvarchar(max)=N'Forwarding Local', @ApiKeyHash varchar(max)=NULL,
 @WebhookSecretName varchar(max)=NULL, @CallbackUrl nvarchar(max)=N'https://localhost:44324/api/DocAi/Webhook',
 @MaxPagesPerJob int=200, @DailyPageCap int=2000, @QuotaMode varchar(30)='Prepaid',
 @QuotaWarningPercent int=10, @OpeningPages bigint=100, @RotateKey bit=0;
-- END SETTINGS
DECLARE @RegistrationRequestId uniqueidentifier='cd2479ea-891c-459c-bd93-5be81332db76',
 @OpeningRequestId uniqueidentifier='7bff97ba-fbab-42cb-a6a4-a228ecf46957';
SET NOCOUNT ON; SET XACT_ABORT ON;
IF @ConsumerCode IS NULL OR @ProductCode IS NULL OR @TenantCode IS NULL
 OR DATALENGTH(@ProductCode) NOT BETWEEN 1 AND 20 OR DATALENGTH(@TenantCode) NOT BETWEEN 1 AND 20
 OR @ProductCode COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z0-9]%'
 OR @TenantCode COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z0-9]%'
 OR DATALENGTH(@ConsumerCode)>30 OR CONVERT(varbinary(max),@ConsumerCode)<>CONVERT(varbinary(max),@ProductCode+'-'+@TenantCode)
 THROW 50018,'Invalid consumer/product/tenant codes.',1;
IF @ApiKeyHash IS NULL OR DATALENGTH(@ApiKeyHash)<>64 OR @ApiKeyHash COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9A-F]%'
 THROW 50018,'ApiKeyHash must be a 64-character uppercase SHA256 hex hash.',1;
IF @WebhookSecretName IS NULL OR DATALENGTH(@WebhookSecretName) NOT BETWEEN 1 AND 127
 OR @WebhookSecretName COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Za-z0-9-]%'
 OR @Name IS NULL OR LEN(LTRIM(RTRIM(@Name)))=0 OR DATALENGTH(@Name)>400
 THROW 50018,'Invalid name or webhook secret name.',1;
IF @CallbackUrl IS NULL OR DATALENGTH(@CallbackUrl)>800 OR LOWER(LEFT(@CallbackUrl,8))<>N'https://'
 OR LOWER(RIGHT(@CallbackUrl,18))<>N'/api/docai/webhook' OR LEN(@CallbackUrl)<=26
 OR @CallbackUrl LIKE N'%?%' OR @CallbackUrl LIKE N'%#%' OR @CallbackUrl LIKE N'% %' OR @CallbackUrl LIKE N'%\%'
 THROW 50018,'Invalid HTTPS callback URL.',1;
DECLARE @Authority nvarchar(max)=SUBSTRING(@CallbackUrl,9,CHARINDEX(N'/',@CallbackUrl+N'/',9)-9);
IF LEN(@Authority)=0 OR @Authority LIKE N'%@%' THROW 50018,'Invalid callback authority.',1;
IF @MaxPagesPerJob IS NULL OR @MaxPagesPerJob NOT BETWEEN 1 AND 2000 OR @DailyPageCap IS NULL OR @DailyPageCap<1
 OR @QuotaMode IS NULL OR @QuotaMode NOT IN ('Prepaid','Unlimited') OR @QuotaWarningPercent IS NULL OR @QuotaWarningPercent NOT BETWEEN 1 AND 50
 OR @OpeningPages IS NULL OR @OpeningPages NOT BETWEEN 1 AND 1000000 OR @RotateKey IS NULL
 THROW 50018,'Invalid limits, quota settings or opening pages.',1;
DECLARE @Text nvarchar(max)=@Name+@CallbackUrl, @Index int=1;
WHILE @Index<=DATALENGTH(@Text)/2 BEGIN
 IF UNICODE(SUBSTRING(@Text,@Index,1))<32 OR UNICODE(SUBSTRING(@Text,@Index,1)) BETWEEN 127 AND 159
 THROW 50018,'Control characters forbidden.',1;
 SET @Index+=1;
END;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @Count bigint, @id smallint, @Hash binary(32)=CONVERT(binary(32),@ApiKeyHash,2), @Changed bit=0, @OpeningAdded bit=0;
 SELECT @Count=COUNT_BIG(*) FROM dbo.Consumers WITH(TABLOCKX,HOLDLOCK);
 SELECT @id=ConsumerId FROM dbo.Consumers WHERE Code=@ConsumerCode;
 IF EXISTS(SELECT 1 FROM dbo.Consumers WHERE (ApiKeyHash1=@Hash OR ApiKeyHash2=@Hash) AND (@id IS NULL OR ConsumerId<>@id))
 THROW 50018,'API key hash belongs to another consumer.',1;
 IF @id IS NULL BEGIN
  IF EXISTS(SELECT 1 FROM dbo.Consumers WHERE ProductCode=@ProductCode AND TenantCode=@TenantCode)
   THROW 50018,'Product/tenant belongs to another consumer.',1;
  INSERT dbo.Consumers(Code,Name,ProductCode,TenantCode,ApiKeyHash1,WebhookSecretName,CallbackUrl,MaxPagesPerJob,DailyPageCap,QuotaMode,QuotaWarningPercent)
  VALUES(@ConsumerCode,@Name,@ProductCode,@TenantCode,@Hash,@WebhookSecretName,@CallbackUrl,@MaxPagesPerJob,@DailyPageCap,@QuotaMode,@QuotaWarningPercent);
  SET @id=CONVERT(smallint,SCOPE_IDENTITY()); SET @Changed=1;
 END ELSE BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.Consumers WHERE ConsumerId=@id AND IsActive=1 AND Name=@Name AND ProductCode=@ProductCode AND TenantCode=@TenantCode
   AND MaxPagesPerJob=@MaxPagesPerJob AND DailyPageCap=@DailyPageCap AND QuotaMode=@QuotaMode AND QuotaWarningPercent=@QuotaWarningPercent)
   THROW 50018,'Existing consumer settings differ; no changes.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.Consumers WHERE ConsumerId=@id AND ApiKeyHash1=@Hash AND WebhookSecretName=@WebhookSecretName AND CallbackUrl=@CallbackUrl) BEGIN
   IF @RotateKey=0 THROW 50018,'Existing hash, secret name or callback differs; use RotateKey=1 explicitly.',1;
   UPDATE dbo.Consumers SET ApiKeyHash1=@Hash,WebhookSecretName=@WebhookSecretName,CallbackUrl=@CallbackUrl WHERE ConsumerId=@id;
   SET @Changed=1;
  END;
 END;
 EXEC dbo.AcquirePageQuotaLock @ConsumerId=@id;
 DECLARE @Operator nvarchar(200)=LEFT(CONCAT(SUSER_SNAME(),N'@',HOST_NAME()),200), @Note nvarchar(400)=N'Development opening pages',
  @RegisterPayload nvarchar(1200)=CONCAT(N'Register|',@ConsumerCode,N'|',@OpeningPages,N'|',@QuotaMode,N'|',@QuotaWarningPercent,N'|DevSetup|Development registration');
 DECLARE @Payload nvarchar(1200)=CONCAT(N'TopUp|',@OpeningPages,N'|0||10|0|0|0|',@Operator,N'|',@Note);
 IF EXISTS(SELECT 1 FROM dbo.PageQuotaOperatorRequests WHERE RequestId=@RegistrationRequestId)
  AND NOT EXISTS(SELECT 1 FROM dbo.PageQuotaOperatorRequests WHERE RequestId=@RegistrationRequestId AND ConsumerId=@id AND Payload=@RegisterPayload)
  THROW 50018,'Registration RequestId differs; no changes.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.PageQuotaOperatorRequests WHERE RequestId=@RegistrationRequestId)
  INSERT dbo.PageQuotaOperatorRequests(RequestId,ConsumerId,Payload) VALUES(@RegistrationRequestId,@id,@RegisterPayload);
 IF EXISTS(SELECT 1 FROM dbo.PageQuotaOperatorRequests WHERE RequestId=@OpeningRequestId) BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.PageQuotaOperatorRequests WHERE RequestId=@OpeningRequestId AND ConsumerId=@id AND Payload=@Payload)
   THROW 50018,'Opening RequestId differs; no changes.',1;
 END ELSE BEGIN
  IF EXISTS(SELECT 1 FROM dbo.PageAllowanceGrants WHERE RequestId=@OpeningRequestId) THROW 50018,'Opening RequestId already used.',1;
  INSERT dbo.PageAllowanceGrants(ConsumerId,Pages,Kind,OriginalGrantId,OperatorIdentity,Note,RequestId)
   VALUES(@id,@OpeningPages,'TopUp',NULL,@Operator,@Note,@OpeningRequestId);
  INSERT dbo.PageQuotaOperatorRequests(RequestId,ConsumerId,Payload) VALUES(@OpeningRequestId,@id,@Payload);
  SET @OpeningAdded=1;
  SET @Changed=1;
 END;
 DECLARE @Models TABLE(ModelId varchar(64),Doc varchar(20),Kind varchar(10),Version smallint,Confidence decimal(4,3),Accuracy decimal(5,4));
 INSERT @Models VALUES('bl-v3','BL','extract',3,.800,.9590),('manifest-v3','MANIFEST','extract',3,.800,.8990),('IST-Classifier-v6','AUTO','classify',2,.500,.9400);
 IF EXISTS(SELECT 1 FROM @Models d JOIN dbo.ModelRegistry m WITH(UPDLOCK,HOLDLOCK) ON m.ModelId=d.ModelId
  WHERE m.DocumentTypeCode<>d.Doc OR m.Kind<>d.Kind OR m.ShippingLineCode IS NOT NULL OR m.Version<>d.Version
   OR m.MinFieldConfidence<>d.Confidence OR m.TestFieldAccuracy IS NULL OR m.TestFieldAccuracy<>d.Accuracy)
  THROW 50018,'Existing model differs; no changes.',1;
 INSERT dbo.ModelRegistry(ModelId,DocumentTypeCode,ShippingLineCode,Kind,Version,Status,MinFieldConfidence,TestFieldAccuracy)
 SELECT d.ModelId,d.Doc,NULL,d.Kind,d.Version,'Testing',d.Confidence,d.Accuracy FROM @Models d
 WHERE NOT EXISTS(SELECT 1 FROM dbo.ModelRegistry m WITH(UPDLOCK,HOLDLOCK) WHERE m.ModelId=d.ModelId);
 IF @@ROWCOUNT>0 SET @Changed=1;
 IF EXISTS(SELECT 1 FROM @Models d LEFT JOIN dbo.MappingProfiles p WITH(UPDLOCK,HOLDLOCK) ON p.ProfileCode=d.ModelId COLLATE Latin1_General_100_BIN2 AND p.DocumentTypeCode=d.Doc
  WHERE d.Kind='extract' AND p.ProfileId IS NULL) THROW 51205,'Apply ModelMappingProfiles migration first.',1;
 IF EXISTS(SELECT 1 FROM @Models d JOIN dbo.ModelMappingBindings b WITH(UPDLOCK,HOLDLOCK) ON b.ModelId=d.ModelId
  JOIN dbo.MappingProfiles p ON p.ProfileId=b.ProfileId WHERE d.Kind='extract' AND (p.ProfileCode<>d.ModelId COLLATE Latin1_General_100_BIN2 OR p.DocumentTypeCode<>d.Doc))
  THROW 51204,'Existing mapping binding differs.',1;
 INSERT dbo.ModelMappingBindings(ModelId,ProfileId)
 SELECT d.ModelId,p.ProfileId FROM @Models d JOIN dbo.MappingProfiles p ON p.ProfileCode=d.ModelId COLLATE Latin1_General_100_BIN2 AND p.DocumentTypeCode=d.Doc
 WHERE d.Kind='extract' AND NOT EXISTS(SELECT 1 FROM dbo.ModelMappingBindings b WHERE b.ModelId=d.ModelId);
 IF @@ROWCOUNT>0 SET @Changed=1;
 DECLARE @Classes TABLE(ClassName varchar(64),Target varchar(20));
 INSERT @Classes VALUES('bill-of-lading','BL'),('cargo-manifest','MANIFEST'),('other',NULL);
 IF EXISTS(SELECT ClassName,TargetDocumentTypeCode FROM dbo.ClassifierClasses WHERE ClassifierModelId='IST-Classifier-v6'
  EXCEPT SELECT ClassName,Target FROM @Classes)
  OR EXISTS(SELECT 1 FROM dbo.ClassifierClasses WHERE ClassifierModelId='IST-Classifier-v6' AND ShippingLineCode IS NOT NULL)
  THROW 50018,'Existing classifier classes differ.',1;
 INSERT dbo.ClassifierClasses(ClassifierModelId,ClassName,TargetDocumentTypeCode,ShippingLineCode)
 SELECT 'IST-Classifier-v6',ClassName,Target,NULL FROM @Classes d
 WHERE NOT EXISTS(SELECT 1 FROM dbo.ClassifierClasses c WITH(UPDLOCK,HOLDLOCK) WHERE c.ClassifierModelId='IST-Classifier-v6' AND c.ClassName=d.ClassName);
 IF @@ROWCOUNT>0 SET @Changed=1;
 -- Use the frozen S14 activation protocol, including mapping/class checks and group retirement.
 DECLARE @ModelId varchar(64);
 DECLARE models CURSOR LOCAL FAST_FORWARD FOR SELECT ModelId FROM @Models ORDER BY CASE Kind WHEN 'extract' THEN 0 ELSE 1 END,ModelId;
 OPEN models; FETCH NEXT FROM models INTO @ModelId;
 WHILE @@FETCH_STATUS=0 BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.ModelRegistry WHERE ModelId=@ModelId AND Status='Active') BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.ModelRegistry WHERE ModelId=@ModelId AND (Status IN ('Draft','Testing') OR (Status='Retired' AND Kind='extract')))
    THROW 50018,'Model cannot be activated from its current state.',1;
   UPDATE dbo.ModelRegistry SET Status='Testing' WHERE ModelId=@ModelId;
   EXEC sys.sp_executesql N'SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @doc varchar(20), @line varchar(20), @kind varchar(10);
SELECT @doc = DocumentTypeCode, @line = ShippingLineCode, @kind = Kind
FROM dbo.ModelRegistry WITH (UPDLOCK, HOLDLOCK)
WHERE ModelId = @newModelId AND Status = ''Testing'' AND TestFieldAccuracy >= @minimumAccuracy;
IF @doc IS NULL THROW 50001, ''The new model is not in Testing or is below the accuracy gate.'', 1;
IF @kind = ''extract'' AND NOT EXISTS (
    SELECT 1 FROM dbo.ModelMappingBindings b WITH (UPDLOCK,HOLDLOCK)
    JOIN dbo.MappingProfiles p WITH (UPDLOCK,HOLDLOCK) ON p.ProfileId=b.ProfileId
    WHERE b.ModelId=@newModelId AND p.DocumentTypeCode=@doc)
    THROW 51205, ''Extract activation requires a matching mapping binding; register with -MappingFile.'', 1;
IF @kind = ''classify''
BEGIN
    -- HOLDLOCK protects the class map and target groups until activation commits.
    IF NOT EXISTS (SELECT 1 FROM dbo.ClassifierClasses WITH (UPDLOCK, HOLDLOCK)
                   WHERE ClassifierModelId = @newModelId AND TargetDocumentTypeCode IS NOT NULL)
        THROW 50003, ''A classifier needs at least one routable class before activation.'', 1;
    IF EXISTS (SELECT 1 FROM dbo.ClassifierClasses AS c WITH (UPDLOCK, HOLDLOCK)
               WHERE c.ClassifierModelId = @newModelId
                 AND ((c.TargetDocumentTypeCode IS NULL AND c.ShippingLineCode IS NOT NULL)
                   OR c.TargetDocumentTypeCode = ''AUTO''
                   OR (@doc <> ''AUTO'' AND (c.TargetDocumentTypeCode IS NULL OR c.TargetDocumentTypeCode <> @doc))
                   OR (c.TargetDocumentTypeCode IS NOT NULL AND NOT EXISTS
                       (SELECT 1 FROM dbo.ModelRegistry AS t WITH (UPDLOCK, HOLDLOCK)
                        WHERE t.DocumentTypeCode = c.TargetDocumentTypeCode AND t.Kind = ''extract''
                          AND (t.ShippingLineCode = c.ShippingLineCode OR (t.ShippingLineCode IS NULL AND c.ShippingLineCode IS NULL))))))
        THROW 50003, ''Classifier targets must identify supported extraction groups or intentional AUTO rejection.'', 1;
END;

UPDATE dbo.ModelRegistry WITH (HOLDLOCK)
SET Status = ''Retired'', RetiredAtUtc = SYSUTCDATETIME()
WHERE DocumentTypeCode = @doc AND Kind = @kind AND Status = ''Active''
  AND (ShippingLineCode = @line OR (ShippingLineCode IS NULL AND @line IS NULL));

UPDATE dbo.ModelRegistry SET Status = ''Active'', ActivatedAtUtc = SYSUTCDATETIME() WHERE ModelId = @newModelId;
COMMIT;',N'@newModelId varchar(64),@minimumAccuracy decimal(5,4)',@newModelId=@ModelId,@minimumAccuracy=.6000;
   SET @Changed=1;
  END;
  FETCH NEXT FROM models INTO @ModelId;
 END;
 CLOSE models; DEALLOCATE models;
 COMMIT;
 IF @Changed=0 PRINT 'already applied'; ELSE PRINT 'development seed applied';
 SELECT @Changed AS Changed, @OpeningAdded AS OpeningAdded;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
