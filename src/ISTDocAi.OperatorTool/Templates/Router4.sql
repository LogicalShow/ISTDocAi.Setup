SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK) WHERE ModelId='$id'
               AND (DocumentTypeCode<>'$doc' OR Kind<>'$kind' OR ShippingLineCode IS NOT NULL))
        THROW 50001, 'Model group mismatch.', 1;
$binding
    IF EXISTS (SELECT 1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK) WHERE ModelId='$id' AND Status='Active')
        SELECT '$id already Active; no changes.';
    ELSE
    BEGIN
$prepare
        IF NOT EXISTS (SELECT 1 FROM dbo.ModelRegistry WHERE ModelId='$id' AND (Status IN ('Draft','Testing') OR (DocumentTypeCode='MANIFEST' AND Status='Retired')))
            THROW 50001, 'Register the model first; only Draft/Testing models or Retired MANIFEST models can be activated.', 1;
        UPDATE dbo.ModelRegistry SET Status='Testing',TestFieldAccuracy=$accuracySql WHERE ModelId='$id';
        EXEC sys.sp_executesql N'$s14', N'@newModelId varchar(64), @minimumAccuracy decimal(5,4)',
             @newModelId='$id', @minimumAccuracy=$gate;
        SELECT '$id activated.';
    END;
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;