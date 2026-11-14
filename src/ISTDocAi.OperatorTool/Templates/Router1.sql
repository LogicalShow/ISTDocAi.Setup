    IF NOT EXISTS (SELECT 1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK) WHERE ModelId='$classifier')
        INSERT dbo.ModelRegistry(ModelId,DocumentTypeCode,ShippingLineCode,Kind,Version,Status,MinFieldConfidence,TestFieldAccuracy)
        VALUES('$classifier','AUTO',NULL,'classify',$ClassifierVersion,'Draft',$classConfidence,$classAccuracy);
    DECLARE @classes TABLE(ClassName varchar(64), TargetDocumentTypeCode varchar(20) NULL, ShippingLineCode varchar(20) NULL);
    INSERT @classes VALUES('$blClass','BL',$line),('$manifestClass','MANIFEST',NULL),('$otherClass',NULL,NULL);
    IF EXISTS (SELECT 1 FROM @classes AS desired JOIN dbo.ClassifierClasses AS existing WITH (UPDLOCK,HOLDLOCK)
               ON existing.ClassifierModelId='$classifier' AND existing.ClassName=desired.ClassName
               WHERE ISNULL(existing.TargetDocumentTypeCode,'')<>ISNULL(desired.TargetDocumentTypeCode,'')
                  OR ISNULL(existing.ShippingLineCode,'')<>ISNULL(desired.ShippingLineCode,''))
        THROW 50001, 'Existing class targets differ. No rows changed.', 1;
    INSERT dbo.ClassifierClasses(ClassifierModelId,ClassName,TargetDocumentTypeCode,ShippingLineCode)
    SELECT '$classifier',desired.ClassName,desired.TargetDocumentTypeCode,desired.ShippingLineCode FROM @classes AS desired
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ClassifierClasses AS existing WITH (UPDLOCK,HOLDLOCK)
                      WHERE existing.ClassifierModelId='$classifier' AND existing.ClassName=desired.ClassName);