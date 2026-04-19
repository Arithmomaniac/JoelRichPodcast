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
        useCommonAlertSchema: false
      }
    ]
  }
}

// Alert: Function execution failures
resource functionFailureAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${functionAppName}-function-failure'
  location: location
  properties: {
    displayName: 'Joel Rich Podcast: Function execution failed'
    description: 'A podcast pipeline function invocation failed. Check App Insights for details.'
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
            | project timestamp, name, resultCode, duration, id
            | summarize failureCount = count(), functions = strcat_array(make_set(name, 10), ', '), lastResultCode = take_any(resultCode) by bin(timestamp, 5m)
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          dimensions: [
            { name: 'functions', operator: 'Include', values: ['*'] }
            { name: 'lastResultCode', operator: 'Include', values: ['*'] }
          ]
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
    displayName: 'Joel Rich Podcast: Errors in application logs'
    description: 'LogError calls appeared in traces — may indicate blob upload, table upsert, scraper, or torah-dl API failures.'
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
            | project timestamp, message = substring(message, 0, 200)
            | summarize errorCount = count(), errors = strcat_array(make_set(message, 10), ' | ') by bin(timestamp, 15m)
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          dimensions: [
            { name: 'errors', operator: 'Include', values: ['*'] }
          ]
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

// Alert: Torah-dl resolution failures — only fires for NEW URLs not seen in the previous 7 days.
// Uses overrideQueryTimeRange to look back 8 days so the query can compare today's failures
// against the previous week. Since Audio Roundup posts weekly, this effectively fires once
// per new post (when new URLs fail) rather than repeating daily for the same failures.
resource resolutionFailureDigest 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${functionAppName}-resolution-failures'
  location: location
  properties: {
    displayName: 'Joel Rich Podcast: Torah-dl resolution failures'
    description: 'New Audio Roundup URLs could not be resolved to direct audio links. Only fires for URLs not seen in the previous 7 days.'
    severity: 3
    enabled: true
    evaluationFrequency: 'P1D'
    windowSize: 'P1D'
    overrideQueryTimeRange: 'P8D'
    scopes: [appInsightsId]
    criteria: {
      allOf: [
        {
          query: '''
            let knownFailures = traces
            | where timestamp < ago(1d)
            | where message has "Could not resolve:"
            | extend url = tostring(customDimensions["Url"])
            | extend failedUrl = iff(isempty(url), extract(@"Could not resolve: (.+?)( \(|$)", 1, message), url)
            | where isnotempty(failedUrl)
            | distinct failedUrl;
            traces
            | where timestamp >= ago(1d)
            | where message has "Could not resolve:"
            | extend url = tostring(customDimensions["Url"]), title = tostring(customDimensions["Title"])
            | project timestamp, failedUrl = iff(isempty(url), extract(@"Could not resolve: (.+?)( \(|$)", 1, message), url)
            | join kind=leftanti knownFailures on failedUrl
            | summarize failureCount = count(), failedUrls = strcat_array(make_set(failedUrl, 20), ', ') by bin(timestamp, 1d)
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          dimensions: [
            { name: 'failedUrls', operator: 'Include', values: ['*'] }
          ]
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

// Alert: No links extracted from Audio Roundup (format may have changed again)
resource noLinksAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${functionAppName}-no-links-found'
  location: location
  properties: {
    displayName: 'Joel Rich Podcast: No links extracted from Audio Roundup'
    description: 'The feed parser found an Audio Roundup post but extracted zero links. The HTML format may have changed. Check the RSS content:encoded HTML and update TorahMusingsFeedParser.ParseHtmlLinks().'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT6H'
    windowSize: 'PT6H'
    scopes: [appInsightsId]
    criteria: {
      allOf: [
        {
          query: '''
            traces
            | where message has "No links found in Audio Roundup"
            | summarize hitCount = count() by bin(timestamp, 6h)
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
