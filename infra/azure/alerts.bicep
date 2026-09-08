// Åkerögården — Cost + health alerts (Phase 11)
// Deploy: az deployment group create --resource-group <rg> --template-file alerts.bicep --parameters @<env>.alerts-parameters.json
// Run AFTER main.bicep (references its App Service sites, Application Insights component, and
// Postgres server by name).
//
// Deliberately minimal — this is what a solo-maintained, low-traffic booking site needs day one,
// not the fuller per-resource-type alert set a team with an on-call rotation might build:
//   - a cost budget (catches an environment left running / a tier creeping up)
//   - one public-site availability ping (admin is login-gated and shares the same App Service
//     plan — if the plan is down, the public ping already catches it)
//   - Http5xx on both sites (real errors, not just slowness)
//   - Postgres cpu_credits_remaining — the one canary tied to a real decision already on record
//     (2026-07-21: accept Burstable B1ms and watch rather than load-test)
// Everything else (response-time thresholds, Postgres cpu%/storage%/connection counts, Key Vault
// availability) was considered and deliberately left out — add only if something real motivates it.

@description('Environment name — must match the value used for main.bicep, so resource names line up')
param environmentName string

@description('Azure region — must match main.bicep (the availability webtest resource itself must live in an Azure region)')
param location string = 'swedencentral'

@description('Address that receives all alert + budget notifications')
param notificationEmail string

@description('''Monthly cost budget for this resource group. Microsoft.Consumption/budgets has no
currency property — the amount is always interpreted in the billing account's own currency, NOT
USD. Verify with `az consumption budget show ... --query currentSpend.unit` before assuming;
this subscription bills in SEK, so the default below is SEK, not USD.''')
param monthlyBudgetAmount int = 600

@description('Http5xx count over 5 minutes that triggers an alert, per site')
param http5xxThreshold int = 10

@description('PostgreSQL remaining CPU credits (avg over 15 min) that triggers an alert — the leading indicator of Burstable throttling')
param postgresCpuCreditsRemainingThreshold int = 20

@description('''PostgreSQL storage used (%) that triggers an alert. Storage autogrow fires around 90%,
so 80 warns while there is still time to look rather than reporting after the platform has already
acted and billed for a larger disk.''')
param postgresStoragePercentThreshold int = 80

@description('''Availability test ping interval in seconds (300/600/900 only). 900 (15 min) keeps
cost low (~$2.88/mo at 2 EU locations) — Application Insights availability tests are billed per
test execution ($0.0005 each), unlike metric alerts, which are free up to 10 time series.''')
@allowed([300, 600, 900])
param availabilityTestFrequencySeconds int = 900

@description('EU-region test-origin locations for the availability ping (NFR-06) — two, so one edge PoP blip does not alone trigger a false alert')
param availabilityTestLocations array = [
  { Id: 'emea-nl-ams-azr' } // Amsterdam
  { Id: 'emea-gb-db3-azr' } // Dublin
]

@description('Budget start date, first-of-month — do not hardcode; utcNow is only evaluable as a parameter default')
param budgetStartDate string = utcNow('yyyy-MM-01')

var prefix = 'jmk-ag-${environmentName}'
var roles = ['public', 'admin']

// ── Existing resources (deployed by main.bicep) ────────────────────────────────
resource sites 'Microsoft.Web/sites@2023-12-01' existing = [for role in roles: {
  name: '${prefix}-${role}'
}]

resource postgresServer 'Microsoft.DBforPostgreSQL/flexibleServers@2023-12-01-preview' existing = {
  name: '${prefix}-pg'
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: '${prefix}-appinsights'
}

// ── Action group — single email destination for every alert + budget notification below ──────
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${prefix}-alerts-ag'
  location: 'global'
  properties: {
    groupShortName: 'ag-alerts' // 12-char limit
    enabled: true
    emailReceivers: [
      {
        name: 'ops'
        emailAddress: notificationEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// ── Cost budget — catches an environment left running or a resource tier creeping up ──────────
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: '${prefix}-monthly-budget'
  properties: {
    category: 'Cost'
    amount: monthlyBudgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      Actual_80Percent: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        contactEmails: [notificationEmail]
        contactGroups: [actionGroup.id]
        thresholdType: 'Actual'
      }
      Actual_100Percent: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        contactEmails: [notificationEmail]
        contactGroups: [actionGroup.id]
        thresholdType: 'Actual'
      }
    }
  }
}

// ── App Service — Http5xx per site ─────────────────────────────────────────────
resource http5xxAlerts 'Microsoft.Insights/metricAlerts@2018-03-01' = [for (role, i) in roles: {
  name: '${prefix}-${role}-http5xx'
  location: 'global'
  properties: {
    severity: 1
    enabled: true
    scopes: [sites[i].id]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Http5xx'
          metricName: 'Http5xx'
          metricNamespace: 'Microsoft.Web/sites'
          operator: 'GreaterThan'
          threshold: http5xxThreshold
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}]

// ── Postgres — cpu_credits_remaining only (see file header for why the rest was left out) ─────
resource postgresCpuCreditsAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-pg-cpu-credits-remaining'
  location: 'global'
  properties: {
    severity: 1
    enabled: true
    scopes: [postgresServer.id]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'CpuCreditsRemaining'
          metricName: 'cpu_credits_remaining'
          operator: 'LessThan'
          threshold: postgresCpuCreditsRemainingThreshold
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}

// ── Public-site availability ping (Application Insights Standard test) ────────────────────────
// Pings the public site's default *.azurewebsites.net hostname — deliberately not the custom
// domain, so this keeps working across the Phase 10d DNS cutover with no change.
resource availabilityTest 'Microsoft.Insights/webtests@2022-06-15' = {
  name: '${prefix}-public-availability'
  location: location
  kind: 'standard'
  tags: {
    'hidden-link:${appInsights.id}': 'Resource'
  }
  properties: {
    SyntheticMonitorId: '${prefix}-public-availability'
    Name: '${prefix}-public-availability'
    Enabled: true
    Frequency: availabilityTestFrequencySeconds
    Timeout: 30
    Kind: 'standard'
    RetryEnabled: true
    Locations: availabilityTestLocations
    Request: {
      RequestUrl: 'https://${sites[0].properties.defaultHostName}/'
      HttpVerb: 'GET'
      ParseDependentRequests: false
      FollowRedirects: true
    }
    ValidationRules: {
      ExpectedHttpStatusCode: 200
      SSLCheck: true
      SSLCertRemainingLifetimeCheck: 7
    }
  }
}

resource availabilityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-public-availability-alert'
  location: 'global'
  properties: {
    severity: 1
    enabled: true
    scopes: [availabilityTest.id, appInsights.id]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria'
      webTestId: availabilityTest.id
      componentId: appInsights.id
      failedLocationCount: 2 // both EU locations must fail — avoids a false alert from one edge PoP blip
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}

// ── Nightly job liveness (post-launch security review, finding #18) ───────────────────────────
// The daily automation — GDPR retention deletion, the completion-link issuance sweep and the 14-day
// reminder — runs as an in-process BackgroundService on the ADMIN site. It works: the three jobs have
// logged a run every day since launch. Nothing, however, notices if it stops.
//
// That matters more than an ordinary background job, because one of the three performs the statutory
// erasure in requirements.md §2.7. If the loop dies (an unhandled fault in ExecuteAsync, Jobs:Enabled
// flipped, the site left stopped) retention silently stops running and the only signal is an absence —
// no error is raised, and RetentionJob deliberately leaves no audit trail behind, since a deleted
// booking's audit rows are cascade-deleted with it.
//
// An absence cannot be seen by a metric alert, so this is a scheduled query rule over the traces the
// runner already writes. Cost, from the Azure retail price list for Sweden Central: EUR 0.0429/month
// at 15-minute evaluation.
//
// Threshold reasoning: three jobs log "Kör schemalagt jobb" once per daily run, so a healthy 24 hours
// contains exactly 3. Alerting at fewer than 3 catches both the loop dying and a single job being
// dropped from the runner. The 24-hour window is wider than the once-daily cadence on purpose — a
// narrower one would fire every day simply because the run happens at 03:00.
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: '${prefix}-logs'
}

resource nightlyJobsAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${prefix}-nightly-jobs-not-running'
  location: location
  properties: {
    displayName: '${prefix}-nightly-jobs-not-running'
    description: 'The daily job runner (GDPR retention, completion-link issuance, 14-day reminder) has logged fewer than three runs in 24 hours. Statutory erasure may not be running.'
    severity: 1
    enabled: true
    scopes: [logAnalyticsWorkspace.id]
    evaluationFrequency: 'PT15M'
    windowSize: 'P1D'
    criteria: {
      allOf: [
        {
          query: 'AppTraces | where Message startswith "Kör schemalagt jobb"'
          timeAggregation: 'Count'
          operator: 'LessThan'
          threshold: 3
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

// ── PostgreSQL storage pressure (post-launch security review, finding #15) ────────────────────
// Storage autogrow is now enabled, which removes the "disk fills, server goes read-only, booking
// system stops accepting writes" failure the review flagged. It does not remove the need to know:
// autogrow raises the disk silently and each step costs money, and on Burstable it cannot grow
// without bound. This is the signal that growth is happening at all.
//
// 80% rather than something tighter: autogrow triggers around 90%, so 80 gives warning BEFORE the
// platform acts rather than reporting after the fact.
resource postgresStorageAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-pg-storage-percent'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [postgresServer.id]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT1H'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'StoragePercent'
          metricName: 'storage_percent'
          operator: 'GreaterThan'
          threshold: postgresStoragePercentThreshold
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}
