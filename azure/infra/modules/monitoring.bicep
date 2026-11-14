param base string
param location string
param insightsId string
param appHost string
param enableAlerts bool = false
param alertEmails array
param monthlyBudget int
param budgetStartDate string

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${base}-alerts'
  location: 'global'
  properties: {
    groupShortName: 'DocAi'
    enabled: true
    emailReceivers: [for (email, i) in alertEmails: {
      name: 'operator-${i}'
      emailAddress: email
      useCommonAlertSchema: true
    }]
  }
}
resource budget 'Microsoft.Consumption/budgets@2024-08-01' = {
  name: '${base}-monthly'
  properties: {
    category: 'Cost'
    amount: monthlyBudget
    timeGrain: 'Monthly'
    timePeriod: { startDate: '${budgetStartDate}T00:00:00Z' }
    notifications: {
      Actual80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: alertEmails
        contactGroups: [actionGroup.id]
      }
      Actual100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: alertEmails
        contactGroups: [actionGroup.id]
      }
    }
  }
}
var alerts = [
  {
    name: 'certificate-expiring', severity: 2, frequency: 'PT1H', window: 'P1D'
    query: 'traces | where customDimensions.event == "identity.certificate_expiring"'
  }
  {
    name: 'queue-stuck', severity: 2, frequency: 'PT1M', window: 'PT5M'
    query: 'traces | where customDimensions.event == "queue.stuck"'
  }
  {
    name: 'breaker-open', severity: 1, frequency: 'PT1M', window: 'PT5M'
    query: 'traces | where customDimensions.event == "breaker.opened"'
  }
  {
    name: 'unpriced-usage', severity: 2, frequency: 'PT5M', window: 'PT1H'
    query: 'traces | where customDimensions.event == "usage.unpriced"'
  }
  {
    name: 'failure-rate', severity: 2, frequency: 'PT5M', window: 'PT1H'
    query: '''
      traces
      | where customDimensions.event in ("job.completed", "job.failed")
      | summarize finished=count(), failed=countif(customDimensions.event == "job.failed")
      | where (finished >= 20 and 100.0 * failed / finished > 5.0) or failed >= 3
      '''
  }
  {
    name: 'dead-letter', severity: 3, frequency: 'PT5M', window: 'PT5M'
    query: 'traces | where customDimensions.event == "webhook.dead_lettered"'
  }
  {
    name: 'registry-drift', severity: 2, frequency: 'PT5M', window: 'PT5M'
    query: 'traces | where customDimensions.event == "registry.drift"'
  }
  {
    name: 'result-missing', severity: 2, frequency: 'PT5M', window: 'PT5M'
    query: 'traces | where customDimensions.event == "analysis.result_missing"'
  }
  {
    name: 'crash-loop', severity: 2, frequency: 'PT5M', window: 'PT1H'
    query: '''
      traces
      | where customDimensions.event == "job.taken_over" or tostring(customDimensions.errorCode) == "WORKER_CRASH_LOOP"
      | summarize takeovers=countif(customDimensions.event == "job.taken_over"), crashes=countif(tostring(customDimensions.errorCode) == "WORKER_CRASH_LOOP")
      | where crashes > 0 or takeovers > 1
      '''
  }
  {
    name: 'unknown-calls', severity: 3, frequency: 'PT1H', window: 'P1D'
    query: 'traces | where customDimensions.event == "operation.unknown"'
  }
]
resource logAlerts 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = [for alert in alerts: if (enableAlerts) {
  name: '${base}-${alert.name}'
  location: location
  kind: 'LogAlert'
  properties: {
    displayName: '${base} ${alert.name}'
    enabled: true
    severity: alert.severity
    scopes: [insightsId]
    evaluationFrequency: alert.frequency
    windowSize: alert.window
    autoMitigate: true
    criteria: {
      allOf: [{
        query: alert.query
        timeAggregation: 'Count'
        operator: 'GreaterThan'
        threshold: 0
        failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
      }]
    }
    actions: { actionGroups: [actionGroup.id] }
  }
}]
resource readiness 'Microsoft.Insights/webtests@2022-06-15' = if (enableAlerts && !empty(appHost)) {
  name: '${base}-readiness'
  location: location
  tags: { 'hidden-link:${insightsId}': 'Resource' }
  kind: 'standard'
  properties: {
    SyntheticMonitorId: '${base}-readiness'
    Name: '${base}-readiness'
    Kind: 'standard'
    Enabled: true
    Frequency: 300
    Timeout: 30
    RetryEnabled: true
    Locations: [{ Id: 'emea-fr-pra-edge' }, { Id: 'emea-nl-ams-azr' }, { Id: 'emea-gb-db3-azr' }]
    Request: { RequestUrl: 'https://${appHost}/health/ready', HttpVerb: 'GET', FollowRedirects: false }
    ValidationRules: { ExpectedHttpStatusCode: 200, SSLCheck: true, SSLCertRemainingLifetimeCheck: 7 }
  }
}
resource readinessAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = if (enableAlerts && !empty(appHost)) {
  name: '${base}-readiness-failed'
  location: 'global'
  properties: {
    severity: 1
    enabled: true
    autoMitigate: true
    scopes: [readiness!.id, insightsId]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria'
      webTestId: readiness!.id
      componentId: insightsId
      failedLocationCount: 2
    }
    actions: [{ actionGroupId: actionGroup.id }]
    description: 'Readiness failed in at least two of three test locations.'
  }
}
