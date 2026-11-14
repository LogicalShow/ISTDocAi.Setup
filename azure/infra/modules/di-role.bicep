param diName string
param principalId string
@allowed(['ServicePrincipal', 'Group'])
param principalType string
var cognitiveRole = 'a97b65f3-24c7-4388-baec-2e87135dc908'
resource di 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = { name: diName }
resource cognitive 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: di
  name: guid(di.id, principalId, cognitiveRole)
  properties: { principalId: principalId, principalType: principalType, roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveRole) }
}
