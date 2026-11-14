SET NOCOUNT ON; SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @id smallint=(SELECT ConsumerId FROM dbo.Consumers WHERE Code='$code');
IF @id IS NULL THROW 50018,'Consumer not found.',1;
EXEC dbo.AcquirePageQuotaLock @id;
IF NOT EXISTS(SELECT 1 FROM dbo.Consumers WHERE ConsumerId=@id AND IsActive=1) THROW 50018,'Consumer inactive.',1;
DECLARE @before bigint=(SELECT AvailablePages FROM dbo.ConsumerPageBalance WHERE ConsumerId=@id),
 @delta bigint=$Pages,@kind varchar(10)=NULL,@original bigint=NULL,@replay bit=0;
DECLARE @token varchar(64)=CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@before,'|',
 (SELECT QuotaMode,QuotaWarningPercent FROM dbo.Consumers WHERE ConsumerId=@id FOR JSON PATH),
 (SELECT GrantId,Pages FROM dbo.PageAllowanceGrants WHERE ConsumerId=@id ORDER BY GrantId FOR JSON PATH))),2);
IF '$ExpectedToken'<>'' AND @token<>'$ExpectedToken' THROW 50018,'Preview changed. Review again.',1;
SELECT CONCAT('PREVIEW_TOKEN=',@token);
IF EXISTS(SELECT 1 FROM dbo.PageQuotaOperatorRequests WHERE RequestId='$RequestId')
BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.PageQuotaOperatorRequests WHERE RequestId='$RequestId' AND ConsumerId=@id AND Payload=N'$payload') THROW 50018,'RequestId conflict; no changes.',1;
 SET @replay=1;
END;
SELECT 'Before' Phase;
$summary
IF @replay=0
BEGIN
 IF '$Action'='TopUp' SET @kind='TopUp';
 IF '$Action'='Adjust' SET @kind='Adjustment';
 IF '$Action'='Reverse'
 BEGIN
  SELECT @delta=-Pages,@original=GrantId FROM dbo.PageAllowanceGrants WHERE GrantId=$GrantId AND ConsumerId=@id AND Kind='TopUp';
  IF @original IS NULL THROW 50018,'TopUp not found for this consumer.',1;
  IF EXISTS(SELECT 1 FROM dbo.PageAllowanceGrants WHERE OriginalGrantId=@original AND Kind='Reversal') THROW 50018,'TopUp already reversed.',1;
  SET @kind='Reversal';
 END;
 IF '$Action'='SetMode' BEGIN SET @delta=$TopUpPages; IF @delta>0 SET @kind='TopUp'; END;
 IF '$Action'='SetWarningPercent' SET @delta=0;
 IF @delta<0 AND @before+@delta<0 AND $debt=0
 BEGIN
  IF $previewFlag=1
  BEGIN
   SELECT CONCAT('QUOTA_REFUSED=Pages already consumed/reserved: ', -@delta-@before, '. Explicit AllowDebt required.');
   ROLLBACK; RETURN;
  END;
  SELECT -@delta-@before PagesAlreadyConsumedOrReserved;
  THROW 50018,'Refused: pages consumed/reserved; AllowDebt required.',1;
 END;
 IF '$Action'='SetMode' AND '$Mode'='Prepaid' AND @before+@delta<=0 AND $blocked=0
 BEGIN
  IF $previewFlag=1
  BEGIN
   SELECT 'QUOTA_REFUSED=Customer blocked immediately. Use TopUpPages (recommended) or ConfirmBlocked for an extra confirmation.';
   ROLLBACK; RETURN;
  END;
  THROW 50018,'Customer blocked immediately: top up or explicitly ConfirmBlocked.',1;
 END;
 IF @kind IS NULL AND EXISTS(SELECT 1 FROM dbo.PageAllowanceGrants WHERE RequestId='$RequestId')
  THROW 50018,'RequestId already belongs to an allowance grant.',1;
 IF @kind IS NOT NULL
  INSERT dbo.PageAllowanceGrants(ConsumerId,Pages,Kind,OriginalGrantId,OperatorIdentity,Note,RequestId)
  VALUES(@id,@delta,@kind,@original,N'$operator',N'$reason','$RequestId');
 IF '$Action'='SetMode' UPDATE dbo.Consumers SET QuotaMode='$Mode' WHERE ConsumerId=@id;
 IF '$Action'='SetWarningPercent' UPDATE dbo.Consumers SET QuotaWarningPercent=$Percent WHERE ConsumerId=@id;
 INSERT dbo.PageQuotaOperatorRequests(RequestId,ConsumerId,Payload) VALUES('$RequestId',@id,N'$payload');
END;
SELECT 'After' Phase,@replay Replay,'$RequestId' RequestId;
$summary
SELECT GrantId,BalanceAfter,RequestId FROM dbo.PageAllowanceGrants WHERE RequestId='$RequestId';
IF $previewFlag=1 ROLLBACK; ELSE COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;