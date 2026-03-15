@description('Application Insights resource ID')
param appInsightsId string

@description('Azure region')
param location string

@description('Email address for alert notifications')
param alertEmail string

@description('Function App name (for alert naming)')
param functionAppName string

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${functionAppName}-alerts-ag'
  location: 'global'
  properties: {
    groupShortName: 'PodcastAlrt'
    enabled: true
    emailReceivers: [
      {
        name: 'PodcastAdmin'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// Alert: Function execution failures
resource functionFailureAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${functionAppName}-function-failure'
  location: location
  properties: {
    displayName: '${functionAppName}: Function execution failed'
    description: 'Fires when any function invocation fails (exception or error status).'
    severity: 1
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    scopes: [appInsightsId]
    criteria: {
      allOf: [
        {
          query: '''
            requests
            | where success == false
            | where cloud_RoleName =~ "${functionAppName}"
            | summarize failureCount = count() by bin(timestamp, 5m)
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

// Alert: Errors logged (catches blob/table/scraper errors)
resource errorLogAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${functionAppName}-error-logs'
  location: location
  properties: {
    displayName: '${functionAppName}: Errors in application logs'
    description: 'Fires when LogError calls appear in traces — covers blob upload, table upsert, and scraper failures.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    scopes: [appInsightsId]
    criteria: {
      allOf: [
        {
          query: '''
            traces
            | where severityLevel >= 3
            | where cloud_RoleName =~ "${functionAppName}"
            | summarize errorCount = count() by bin(timestamp, 15m)
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

// Alert: Daily digest of torah-dl resolution failures.
// Since Audio Roundup posts weekly (Tuesdays), this effectively produces
// a weekly notification of which URLs could not be resolved.
resource resolutionFailureDigest 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${functionAppName}-resolution-failures'
  location: location
  properties: {
    displayName: '${functionAppName}: Torah-dl resolution failures'
    description: 'Summarizes URLs that torah-dl could not resolve in the past 24 hours. Review to identify patterns and propose upstream fixes.'
    severity: 3
    enabled: true
    evaluationFrequency: 'P1D'
    windowSize: 'P1D'
    scopes: [appInsightsId]
    criteria: {
      allOf: [
        {
          query: '''
            traces
            | where message has "Could not resolve:"
            | project timestamp, url = tostring(customDimensions["Url"]), title = tostring(customDimensions["Title"])
            | summarize failureCount = count(), urls = make_set(url, 50) by bin(timestamp, 1d)
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}
