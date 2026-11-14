SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK) WHERE ModelId='$ModelId'
               AND (DocumentTypeCode<>'BL' OR Kind<>'extract' OR ShippingLineCode IS NOT NULL))
        THROW 50001, 'Model group mismatch.', 1;
    IF EXISTS (SELECT 1 FROM dbo.ModelRegistry WHERE ModelId='$ModelId' AND Status='Active')
    BEGIN
$bindingExec
    END;
    IF EXISTS (SELECT 1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK) WHERE ModelId='$ModelId' AND Status='Active')
        SELECT '$ModelId already Active; no changes.';
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.ModelRegistry WHERE ModelId='$ModelId')
        BEGIN
            DECLARE @version int;
            SELECT @version=ISNULL(MAX(CONVERT(int,Version)),0)+1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK)
            WHERE DocumentTypeCode='BL' AND ShippingLineCode IS NULL AND Kind='extract';
            INSERT dbo.ModelRegistry(ModelId,DocumentTypeCode,ShippingLineCode,Kind,Version,Status)
            VALUES('$ModelId','BL',NULL,'extract',@version,'Testing');
        END;
$bindingExec
        IF NOT EXISTS (SELECT 1 FROM dbo.ModelRegistry WHERE ModelId='$ModelId' AND Status IN ('Draft','Testing','Retired'))
            THROW 50001, 'Only Draft, Testing or Retired models can be activated.', 1;
        -- Retired models in this same group can be re-tested and activated for rollback.
        UPDATE dbo.ModelRegistry SET Status='Testing',TestFieldAccuracy=$accuracy,MinFieldConfidence=$confidence
        WHERE ModelId='$ModelId';
        EXEC sys.sp_executesql N'$s14', N'@newModelId varchar(64), @minimumAccuracy decimal(5,4)',
             @newModelId='$ModelId', @minimumAccuracy=$gate;
        SELECT '$ModelId activated.';
    END;
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;