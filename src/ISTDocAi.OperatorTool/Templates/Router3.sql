    IF NOT EXISTS (SELECT 1 FROM dbo.ModelRegistry WHERE ModelId='$id')
    BEGIN
        DECLARE @source varchar(64), @version int;
        SELECT @version=ISNULL(MAX(CONVERT(int,Version)),0)+1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK)
        WHERE DocumentTypeCode='AUTO' AND ShippingLineCode IS NULL AND Kind='classify';
        SELECT TOP (1) @source=ModelId FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK)
        WHERE DocumentTypeCode='AUTO' AND ShippingLineCode IS NULL AND Kind='classify' ORDER BY Version DESC;
        IF @source IS NULL THROW 50001, 'Register the AUTO classifier and its classes with step 25 first.', 1;
        IF (SELECT COUNT(*) FROM dbo.ClassifierClasses WITH (UPDLOCK,HOLDLOCK) WHERE ClassifierModelId=@source)<>3
           OR (SELECT COUNT(*) FROM dbo.ClassifierClasses WHERE ClassifierModelId=@source AND
              ((ClassName='bill-of-lading' AND TargetDocumentTypeCode='BL') OR
               (ClassName='cargo-manifest' AND TargetDocumentTypeCode='MANIFEST' AND ShippingLineCode IS NULL) OR
               (ClassName='other' AND TargetDocumentTypeCode IS NULL AND ShippingLineCode IS NULL)))<>3
            THROW 50001, 'Source classifier must have the three step-25 class mappings.', 1;
        INSERT dbo.ModelRegistry(ModelId,DocumentTypeCode,ShippingLineCode,Kind,Version,Status)
        VALUES('$id','AUTO',NULL,'classify',@version,'Testing');
        INSERT dbo.ClassifierClasses(ClassifierModelId,ClassName,TargetDocumentTypeCode,ShippingLineCode)
        SELECT '$id',ClassName,TargetDocumentTypeCode,ShippingLineCode FROM dbo.ClassifierClasses WHERE ClassifierModelId=@source;
    END;
    UPDATE dbo.ModelRegistry SET MinFieldConfidence=$confidence WHERE ModelId='$id';