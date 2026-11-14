targetScope = 'resourceGroup'
@minLength(2)
@maxLength(10)
param prefix string = 'docai'
@allowed(['dev', 'prod'])
param environment string
@allowed(['appService', 'onPremises', 'local'])
param hostingMode string = 'appService'
param onPremisesPrincipalObjectId string = ''
param location string = resourceGroup().location
@allowed(['create', 'existing'])
param documentIntelligenceMode string = 'create'
param existingDocumentIntelligenceName string = ''
param existingDocumentIntelligenceResourceGroup string = resourceGroup().name
param appServiceSku string = 'B1'
param sqlSku string = 'S0'
@allowed(['Standard_LRS', 'Standard_ZRS', 'Standard_GRS', 'Standard_RAGRS'])
param storageSku string = 'Standard_LRS'
param sqlAdminGroupName string
param sqlAdminGroupObjectId string
param developerGroupObjectId string = ''
param purgeProtection bool = environment == 'prod'
param disableInsightsLocalAuth bool = false
@allowed(['Audit', 'Deny', 'Disabled'])
param policyEffect string = 'Audit'
@description('When false, nobody is notified of the operational conditions in plan section 12. Turning alerts on is a one-value change plus a redeploy.')
param enableAlerts bool = false
param alertEmails array
@minValue(1)
param monthlyBudget int = 150
@description('First day of budget start month, YYYY-MM-01; keep stable on redeploy.')
param budgetStartDate string

var cloudHosting = hostingMode == 'appService'
var localHosting = hostingMode == 'local'
var telemetryEnabled = !localHosting
var runtimePrincipalId = cloudHosting ? app!.identity.principalId : onPremisesPrincipalObjectId
module onPremGuard 'modules/onprem-guard.bicep' = if (hostingMode == 'onPremises') {
  name: 'onprem-principal-guard'
  params: { principalObjectId: trim(onPremisesPrincipalObjectId) }
}

module localGuard 'modules/local-guard.bicep' = if (localHosting) {
  name: 'local-developer-group-guard'
  params: { developerGroupObjectId: trim(developerGroupObjectId) }
}

var base = toLower('${prefix}-${environment}')
var unique = uniqueString(resourceGroup().id, prefix, environment)
var storageName = '${toLower(replace(prefix, '-', ''))}${environment}${take(unique, 8)}'
var roles = {
  cognitive: 'a97b65f3-24c7-4388-baec-2e87135dc908'
  blob: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  secrets: '4633458b-17de-408a-b874-0445c86b69e6'
  officer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
}
module prodGuard 'modules/prod-guard.bicep' = if (environment == 'prod') {
  name: '${base}-prod-guard'
  params: { developerGroupObjectId: developerGroupObjectId }
}
// Validate the original prefix before using its normalized base in DNS resource names.
module prefixGuard 'modules/prefix-guard.bicep' = {
  name: '${base}-prefix-guard'
  params: {
    invalidPrefix: length(filter(range(0, length(prefix)), i => !contains('abcdefghijklmnopqrstuvwxyz0123456789-', substring(prefix, i, 1)))) == 0 && contains('abcdefghijklmnopqrstuvwxyz', substring(prefix, 0, 1)) ? '' : prefix
  }
}
resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = if (telemetryEnabled) {
  name: '${base}-logs'
  location: location
  properties: { retentionInDays: 90, sku: { name: 'PerGB2018' } }
}
resource insights 'Microsoft.Insights/components@2020-02-02' = if (telemetryEnabled) {
  name: '${base}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace!.id
    RetentionInDays: 90
    DisableLocalAuth: disableInsightsLocalAuth
  }
}
resource plan 'Microsoft.Web/serverfarms@2024-11-01' = if (cloudHosting) {
  name: '${base}-plan'
  location: location
  kind: 'linux'
  sku: { name: appServiceSku, capacity: 1 }
  properties: { reserved: true }
}
resource app 'Microsoft.Web/sites@2024-11-01' = if (cloudHosting) {
  name: '${base}-app-${unique}'
  location: location
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan!.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      healthCheckPath: '/health/live'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights!.properties.ConnectionString }
        { name: 'DocAi__Telemetry__UseEntra', value: string(disableInsightsLocalAuth) }
        { name: 'ConnectionStrings__DefaultConnection', value: 'Server=tcp:${sql!.properties.fullyQualifiedDomainName},1433;Database=${database!.name};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False' }
        { name: 'DocAi__Blob__AccountUrl', value: storage.properties.primaryEndpoints.blob }
        { name: 'DocAi__Blob__Container', value: 'documents' }
        { name: 'DocAi__DocumentIntelligence__Endpoint', value: diEndpoint }
        { name: 'DocAi__Secrets__Provider', value: 'KeyVault' }
        { name: 'DocAi__KeyVault__Url', value: vault.properties.vaultUri }
        { name: 'Signals__Enabled', value: 'true' }
      ]
    }
  }
}
resource ftpPublishing 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = if (cloudHosting) {
  parent: app
  name: 'ftp'
  properties: { allow: false }
}
resource scmPublishing 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = if (cloudHosting) {
  parent: app
  name: 'scm'
  properties: { allow: false }
}
resource sql 'Microsoft.Sql/servers@2023-08-01' = if (cloudHosting) {
  name: '${base}-sql-${unique}'
  location: location
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: sqlAdminGroupName
      sid: sqlAdminGroupObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}
resource sqlEntraOnly 'Microsoft.Sql/servers/azureADOnlyAuthentications@2023-08-01' = if (cloudHosting) {
  parent: sql
  name: 'Default'
  properties: { azureADOnlyAuthentication: true }
}
resource azureFirewall 'Microsoft.Sql/servers/firewallRules@2023-08-01' = if (cloudHosting) {
  parent: sql
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}
resource database 'Microsoft.Sql/servers/databases@2023-08-01' = if (cloudHosting) {
  parent: sql
  name: '${base}-db'
  location: location
  sku: { name: sqlSku }
  properties: { collation: 'SQL_Latin1_General_CP1_CI_AS' }
}
resource storage 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: { name: storageSku }
  properties: {
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    defaultToOAuthAuthentication: true
    publicNetworkAccess: 'Enabled'
  }
}
resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: storage
  name: 'default'
}
resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = [for name in ['documents', 'results']: {
  parent: blobs
  name: name
  properties: { publicAccess: 'None' }
}]
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2025-01-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [for item in [{ container: 'documents', days: 90 }, { container: 'results', days: 30 }]: {
        name: 'delete-${item.container}'
        enabled: true
        type: 'Lifecycle'
        definition: {
          filters: { blobTypes: ['blockBlob'], prefixMatch: ['${item.container}/'] }
          actions: { baseBlob: { delete: { daysAfterCreationGreaterThan: item.days } } }
        }
      }]
    }
  }
}
resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: '${base}-kv-${take(unique, 5)}'
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: purgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
  }
}
module diGuard 'modules/di-guard.bicep' = {
  name: '${base}-di-guard'
  params: { existingDocumentIntelligenceName: documentIntelligenceMode == 'existing' ? trim(existingDocumentIntelligenceName) : 'create' }
}
resource existingDi 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  scope: resourceGroup(existingDocumentIntelligenceResourceGroup)
  name: existingDocumentIntelligenceName
}
var diEndpoint = documentIntelligenceMode == 'existing' ? existingDi.properties.endpoint : di!.properties.endpoint
var diName = documentIntelligenceMode == 'existing' ? existingDocumentIntelligenceName : di!.name
var diResourceGroup = documentIntelligenceMode == 'existing' ? existingDocumentIntelligenceResourceGroup : resourceGroup().name
resource di 'Microsoft.CognitiveServices/accounts@2025-06-01' = if (documentIntelligenceMode == 'create') {
  name: '${base}-di-${unique}'
  location: location
  kind: 'FormRecognizer'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: '${base}-di-${unique}'
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}
module dataRoles 'modules/data-roles.bicep' = if (!localHosting) {
  name: '${base}-app-roles'
  params: {
    principalId: runtimePrincipalId
    principalType: 'ServicePrincipal'
    storageName: storage.name
    vaultName: vault.name
    blobRole: roles.blob
    vaultRoleId: roles.secrets
  }
  dependsOn: [containers]
}
module developerRoles 'modules/data-roles.bicep' = if ((localHosting || environment == 'dev') && !empty(developerGroupObjectId)) {
  name: '${base}-developer-roles'
  params: {
    principalId: localHosting ? localGuard!.outputs.validated : developerGroupObjectId
    principalType: 'Group'
    storageName: storage.name
    vaultName: vault.name
    blobRole: roles.blob
    vaultRoleId: roles.secrets
    officerRoleId: roles.officer
  }
  dependsOn: [containers]
}
module appDiRole 'modules/di-role.bicep' = if (!localHosting) {
  name: '${base}-app-di-role'
  scope: resourceGroup(diResourceGroup)
  params: { diName: diName, principalId: runtimePrincipalId, principalType: 'ServicePrincipal' }
}
module developerDiRole 'modules/di-role.bicep' = if ((localHosting || environment == 'dev') && !empty(developerGroupObjectId)) {
  name: '${base}-developer-di-role'
  scope: resourceGroup(diResourceGroup)
  params: { diName: diName, principalId: localHosting ? localGuard!.outputs.validated : developerGroupObjectId, principalType: 'Group' }
}
module insightsRole 'modules/insights-role.bicep' = if (telemetryEnabled && disableInsightsLocalAuth) {
  name: '${base}-insights-role'
  params: {
    insightsName: insights!.name
    principalId: runtimePrincipalId
  }
}
var policies = [
  { name: 'ai-no-keys', id: '71ef260a-8f18-47b7-abcb-62d0673d94dc' }
  { name: 'storage-no-keys', id: '8c6a50c6-9ffd-4ae7-986f-5fa6111f9a54' }
  { name: 'sql-entra-only', id: 'b3a22bc9-66de-45fb-98fa-00f5df42f41a' }
  { name: 'servicebus-no-keys', id: 'cfb11c26-f069-4c14-8e36-56c394dae5af' }
]
resource policyAssignments 'Microsoft.Authorization/policyAssignments@2024-04-01' = [for policy in policies: {
  name: '${base}-${policy.name}'
  properties: {
    policyDefinitionId: tenantResourceId('Microsoft.Authorization/policyDefinitions', policy.id)
    parameters: { effect: { value: policyEffect } }
  }
}]
module monitoring 'modules/monitoring.bicep' = {
  name: '${base}-monitoring'
  params: {
    base: base
    location: location
    insightsId: telemetryEnabled ? insights!.id : ''
    appHost: cloudHosting ? app!.properties.defaultHostName : ''
    enableAlerts: telemetryEnabled && enableAlerts
    alertEmails: alertEmails
    monthlyBudget: monthlyBudget
    budgetStartDate: budgetStartDate
  }
}
output hosting object = cloudHosting ? {
  appServiceName: app!.name
  defaultHostName: app!.properties.defaultHostName
  appPrincipalId: runtimePrincipalId
  possibleOutboundIpAddresses: app!.properties.possibleOutboundIpAddresses
  sqlServerEndpoint: sql!.properties.fullyQualifiedDomainName
  databaseName: database!.name
} : localHosting ? {} : { servicePrincipalObjectId: runtimePrincipalId }
output blobEndpoint string = storage.properties.primaryEndpoints.blob
output keyVaultEndpoint string = vault.properties.vaultUri
output documentIntelligenceEndpoint string = diEndpoint
