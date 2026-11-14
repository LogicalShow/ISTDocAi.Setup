param principalId string
@allowed(['ServicePrincipal', 'Group'])
param principalType string
param storageName string
param vaultName string
param blobRole string
param vaultRoleId string
param officerRoleId string = ''

resource storage 'Microsoft.Storage/storageAccounts@2025-01-01' existing = { name: storageName }
resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' existing = { parent: storage, name: 'default' }
resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' existing = [for name in ['documents', 'results']: { parent: blobs, name: name }]
resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = { name: vaultName }
resource blob 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for i in range(0, 2): {
  scope: containers[i]
  name: guid(containers[i].id, principalId, blobRole)
  properties: { principalId: principalId, principalType: principalType, roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobRole) }
}]
resource secrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, principalId, vaultRoleId)
  properties: { principalId: principalId, principalType: principalType, roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', vaultRoleId) }
}
resource officer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(officerRoleId)) {
  scope: vault
  name: guid(vault.id, principalId, officerRoleId)
  properties: { principalId: principalId, principalType: principalType, roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', officerRoleId) }
}
