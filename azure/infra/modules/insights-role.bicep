param insightsName string
param principalId string
var roleId = '3913510d-42f4-4e42-8a64-420c390055eb'
resource insights 'Microsoft.Insights/components@2020-02-02' existing = { name: insightsName }
resource publisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: insights
  name: guid(insights.id, principalId, roleId)
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
  }
}
