@minLength(1)
@description('onPremises requires a non-empty service principal object ID.')
param principalObjectId string
output validated string = principalObjectId
