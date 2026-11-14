SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH('dbo.ClassifierClasses','TargetDocumentTypeCode') IS NULL
        THROW 50001, 'Apply the AUTO/MANIFEST upgrade first.', 1;
    IF EXISTS (SELECT 1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK) WHERE ModelId='$manifest'
               AND (DocumentTypeCode<>'MANIFEST' OR Kind<>'extract' OR ShippingLineCode IS NOT NULL))
$classifierGroupCheck
        THROW 50001, 'An existing model ID belongs to a different group. No rows changed.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK) WHERE ModelId='$manifest')
        INSERT dbo.ModelRegistry(ModelId,DocumentTypeCode,ShippingLineCode,Kind,Version,Status,MinFieldConfidence,TestFieldAccuracy)
        VALUES('$manifest','MANIFEST',NULL,'extract',$manifestVersionSql,'Testing',$manifestConfidence,$manifestAccuracy);
$binding
$classifierRegistration
    COMMIT;
    SELECT 'Router models/classes registered without activation.';
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;