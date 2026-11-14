-- Target: DocAi database. Required: db_owner OR docai_operator + docai_quota_operator.
-- Registers a Prepaid consumer with one initial TopUp. Safe to rerun unchanged.
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Code nvarchar(max)=N'FWD-UNF',
 @Product nvarchar(max)=N'FWD', @Tenant nvarchar(max)=N'UNF',
 @Name nvarchar(max)=N'Forwarding', @HashHex nvarchar(max)=N'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
 @Callback nvarchar(max)=N'https://kit.example/api/DocAi/Webhook', @SecretName nvarchar(max)=N'webhook-c4809da3612d4b82803d301bfc6a1b22',
 @SetupId uniqueidentifier='c4809da3-612d-4b82-803d-301bfc6a1b22', @InitialPages bigint=500,
 @Operator nvarchar(max)=N'DOMAIN\operator', @Note nvarchar(max)=N'Initial allowance at registration';
IF @Code IS NULL OR @Product IS NULL OR @Tenant IS NULL OR @Name IS NULL OR @HashHex IS NULL OR @Callback IS NULL
 OR @SecretName IS NULL OR @SetupId IS NULL OR @InitialPages IS NULL OR @Operator IS NULL OR @Note IS NULL
 THROW 50018,'Registration values are required.',1;
IF DATALENGTH(@Product)/2 NOT BETWEEN 1 AND 20 OR @Product COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z0-9]%'
 OR DATALENGTH(@Tenant)/2 NOT BETWEEN 1 AND 20 OR @Tenant COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z0-9]%'
 OR DATALENGTH(@Code)/2>30 OR CONVERT(varbinary(max),@Code)<>CONVERT(varbinary(max),@Product+N'-'+@Tenant)
 THROW 50018,'Invalid registration codes.',1;
IF DATALENGTH(@HashHex)<>128 OR @HashHex COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9A-F]%'
 THROW 50018,'Invalid registration hash.',1;
IF @InitialPages NOT BETWEEN 1 AND 1000000 OR @SetupId='00000000-0000-0000-0000-000000000000'
 THROW 50018,'Invalid registration pages or SetupId.',1;
IF LEN(LTRIM(RTRIM(@Name)))=0 OR DATALENGTH(@Name)/2>200 OR LEN(LTRIM(RTRIM(@Operator)))=0 OR DATALENGTH(@Operator)/2>200
 OR LEN(LTRIM(RTRIM(@Note)))=0 OR DATALENGTH(@Note)/2>400
 THROW 50018,'Invalid registration text.',1;
IF DATALENGTH(@Callback)/2>400 OR LOWER(LEFT(@Callback,8))<>N'https://'
 OR LOWER(RIGHT(@Callback,18))<>N'/api/docai/webhook' OR LEN(@Callback)<=26
 OR @Callback LIKE N'%?%' OR @Callback LIKE N'%#%' OR @Callback LIKE N'% %' OR @Callback LIKE N'%\%'
 THROW 50018,'Invalid registration callback.',1;
DECLARE @Authority nvarchar(max)=SUBSTRING(@Callback,9,CHARINDEX(N'/',@Callback+N'/',9)-9);
IF LEN(@Authority)=0 OR @Authority LIKE N'%@%'
 THROW 50018,'Invalid registration callback authority.',1;
IF CONVERT(varbinary(max),@SecretName)<>CONVERT(varbinary(max),N'webhook-'+LOWER(REPLACE(CONVERT(nvarchar(36),@SetupId),N'-',N'')))
 THROW 50018,'Invalid registration secret name.',1;
DECLARE @Text nvarchar(max)=@Code+@Name+@Callback+@SecretName+@Operator+@Note, @Index int=1;
WHILE @Index<=DATALENGTH(@Text)/2
BEGIN
 IF UNICODE(SUBSTRING(@Text,@Index,1))<32 OR UNICODE(SUBSTRING(@Text,@Index,1)) BETWEEN 127 AND 159
  THROW 50018,'Control characters forbidden.',1;
 SET @Index+=1;
END;
DECLARE @Hash binary(32)=CONVERT(binary(32),@HashHex,2), @id smallint, @Count bigint,
 @Payload nvarchar(1200)=CONCAT(N'Register|',@Code,N'|',@InitialPages,N'|Prepaid|10|',@Operator,N'|',@Note);
BEGIN TRY
 BEGIN TRANSACTION;
 SELECT @Count=COUNT_BIG(*) FROM dbo.Consumers WITH (TABLOCKX,HOLDLOCK);
 SELECT @id=ConsumerId FROM dbo.Consumers WHERE Code=@Code;
 IF EXISTS(SELECT 1 FROM dbo.Consumers WHERE (ApiKeyHash1=@Hash OR ApiKeyHash2=@Hash) AND (@id IS NULL OR ConsumerId<>@id))
  THROW 50018,'API key hash belongs to another consumer.',1;
 IF @id IS NOT NULL
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.Consumers WHERE ConsumerId=@id
   AND CONVERT(varbinary(max),CONVERT(nvarchar(max),Code))=CONVERT(varbinary(max),@Code)
   AND CONVERT(varbinary(max),CONVERT(nvarchar(max),ProductCode))=CONVERT(varbinary(max),@Product)
   AND CONVERT(varbinary(max),CONVERT(nvarchar(max),TenantCode))=CONVERT(varbinary(max),@Tenant)
   AND ApiKeyHash1=@Hash AND CONVERT(varbinary(max),CallbackUrl)=CONVERT(varbinary(max),@Callback)
   AND CONVERT(varbinary(max),CONVERT(nvarchar(max),WebhookSecretName))=CONVERT(varbinary(max),@SecretName))
   THROW 50018,'Existing consumer differs; registration refused.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.PageQuotaOperatorRequests WHERE RequestId=@SetupId AND ConsumerId=@id
   AND CONVERT(varbinary(max),Payload)=CONVERT(varbinary(max),@Payload))
   THROW 50018,'Registration request differs; no changes.',1;
  PRINT 'already registered';
 END
 ELSE
 BEGIN
  IF EXISTS(SELECT 1 FROM dbo.Consumers WHERE ProductCode=@Product AND TenantCode=@Tenant)
   THROW 50018,'Product and tenant already registered under another code.',1;
  IF EXISTS(SELECT 1 FROM dbo.PageQuotaOperatorRequests WITH (UPDLOCK,HOLDLOCK) WHERE RequestId=@SetupId)
   THROW 50018,'SetupId already registered; no changes.',1;
  INSERT dbo.Consumers(Code,Name,ProductCode,TenantCode,ApiKeyHash1,CallbackUrl,WebhookSecretName,QuotaMode,QuotaWarningPercent)
   VALUES(@Code,@Name,@Product,@Tenant,@Hash,@Callback,@SecretName,'Prepaid',10);
  SET @id=CONVERT(smallint,SCOPE_IDENTITY());
  EXEC dbo.AcquirePageQuotaLock @ConsumerId=@id;
  INSERT dbo.PageAllowanceGrants(ConsumerId,Pages,Kind,OriginalGrantId,OperatorIdentity,Note,RequestId)
   VALUES(@id,@InitialPages,'TopUp',NULL,@Operator,@Note,@SetupId);
  INSERT dbo.PageQuotaOperatorRequests(RequestId,ConsumerId,Payload) VALUES(@SetupId,@id,@Payload);
 END;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
SELECT c.ConsumerId,c.Code,c.Name,c.ProductCode,c.TenantCode,c.IsActive,c.CallbackUrl,c.WebhookSecretName,
 c.QuotaMode,c.QuotaWarningPercent,b.AvailablePages
FROM dbo.Consumers c JOIN dbo.ConsumerPageBalance b ON b.ConsumerId=c.ConsumerId WHERE c.ConsumerId=@id;