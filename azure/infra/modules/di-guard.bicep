@description('Existing mode requires a non-empty existingDocumentIntelligenceName.')
@minLength(1)
param existingDocumentIntelligenceName string
output validated bool = !empty(existingDocumentIntelligenceName)
