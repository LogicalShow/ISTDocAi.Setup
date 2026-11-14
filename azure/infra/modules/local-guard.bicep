@minLength(1)
@description('Local hosting requires a non-empty developerGroupObjectId.')
param developerGroupObjectId string
output validated string = developerGroupObjectId
