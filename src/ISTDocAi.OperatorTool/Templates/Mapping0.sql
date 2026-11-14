DECLARE @mappingRows TABLE(MapKind varchar(12) COLLATE Latin1_General_100_BIN2, CanonicalKey varchar(64) COLLATE Latin1_General_100_BIN2, AzureName varchar(128) COLLATE Latin1_General_100_BIN2);
$insertRows
IF @mappingProfile IS NULL
BEGIN
    INSERT dbo.MappingProfiles(ProfileCode,ProfileVersion,DocumentTypeCode,MappingShape,ContainersField,ContainerNumberField,SealNumberField,BillsOfLadingField,ChangeReference)
    VALUES('$ModelId',1,'$DocumentTypeCode','$($profile.MappingShape)','$($profile.ContainersField)','$($profile.ContainerNumberField)','$($profile.SealNumberField)','$($profile.BillsOfLadingField)',$reference);
    SET @mappingProfile=SCOPE_IDENTITY();
    INSERT dbo.MappingFields(ProfileId,MapKind,CanonicalKey,AzureName) SELECT @mappingProfile,MapKind,CanonicalKey,AzureName FROM @mappingRows;
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.MappingProfiles WHERE ProfileId=@mappingProfile
    AND MappingShape='$($profile.MappingShape)' AND ContainersField='$($profile.ContainersField)'
    AND ContainerNumberField='$($profile.ContainerNumberField)' AND SealNumberField='$($profile.SealNumberField)'
    AND BillsOfLadingField='$($profile.BillsOfLadingField)'
    AND ChangeReference COLLATE Latin1_General_100_BIN2=$reference COLLATE Latin1_General_100_BIN2 AND DATALENGTH(ChangeReference)=DATALENGTH($reference))
    OR EXISTS (SELECT MapKind,CanonicalKey,AzureName FROM @mappingRows EXCEPT SELECT MapKind,CanonicalKey,AzureName FROM dbo.MappingFields WHERE ProfileId=@mappingProfile)
    OR EXISTS (SELECT MapKind,CanonicalKey,AzureName FROM dbo.MappingFields WHERE ProfileId=@mappingProfile EXCEPT SELECT MapKind,CanonicalKey,AzureName FROM @mappingRows)
    THROW 51205, 'Conflicting mapping profile content; existing profiles are never overwritten.', 1;