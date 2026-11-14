using './main.bicep'
param prefix = 'docai'
param environment = 'dev'
param location = 'francecentral'
param sqlAdminGroupName = 'DocAi-Dev-SQL-Admins'
param sqlAdminGroupObjectId = 'REPLACE-WITH-OBJECT-ID'
param developerGroupObjectId = 'REPLACE-WITH-OBJECT-ID'
param enableAlerts = false
param alertEmails = ['alerts@example.invalid']
param monthlyBudget = 150
param budgetStartDate = '2026-10-01'
param purgeProtection = false

param documentIntelligenceMode = 'existing'
param existingDocumentIntelligenceName = 'IST'
param existingDocumentIntelligenceResourceGroup = 'OCR-PROVIDER'

param hostingMode = 'onPremises'
param onPremisesPrincipalObjectId = 'REPLACE-WITH-OBJECT-ID'
