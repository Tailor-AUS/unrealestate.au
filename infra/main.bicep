// ═══════════════════════════════════════════════════════════════
// AIGENTS - AZURE INFRASTRUCTURE (BICEP)
// ═══════════════════════════════════════════════════════════════
// Deploys: Container Apps, Azure AI Foundry, SQL, Redis
// ═══════════════════════════════════════════════════════════════

@description('Environment name (staging, production)')
param environmentName string

@description('Azure region')
param location string = resourceGroup().location

@description('Azure AI region. Defaults to australiaeast for data sovereignty (see docs/VISION.md "Australian Sovereignty" section). gpt-4o:2024-11-20 is available in australiaeast for Standard regional deployment per Azure Foundry models docs.')
param aiLocation string = 'australiaeast'

@description('Azure Container Registry name')
param acrName string

@description('Image tag to deploy')
param imageTag string

@secure()
@description('Google Client ID')
param googleClientId string

@secure()
@description('Google Client Secret')
param googleClientSecret string

// ───────────────────────────────────────────────────────────────
// VARIABLES
// ───────────────────────────────────────────────────────────────

var suffix = '${environmentName}'
var appName = 'aigents'
var tags = {
  environment: environmentName
  application: appName
}

// ───────────────────────────────────────────────────────────────
// LOG ANALYTICS
// ───────────────────────────────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${appName}-logs-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ───────────────────────────────────────────────────────────────
// AZURE AI FOUNDRY (Azure OpenAI)
// ───────────────────────────────────────────────────────────────

// Cognitive Services account name carries an '-au-' segment so that
// switching aiLocation from eastus -> australiaeast forces creation of
// a NEW account (Azure resources cannot move between regions). The
// previous 'aigents-ai-production' account in eastus is orphaned and
// must be deleted manually via the Azure portal.
resource cognitiveServices 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = {
  name: '${appName}-ai-au-${suffix}'
  location: aiLocation
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: '${appName}-ai-au-${suffix}'
    publicNetworkAccess: 'Enabled'
  }
}

// Deploy GPT-4o model in the Australian region.
// gpt-4.1:2025-04-14 is not available in australiaeast for Standard
// regional deployment, so we use gpt-4o:2024-11-20 (available in AU,
// drop-in compatible with the .NET ChatClient call surface). When this
// version is deprecated we'll bump to the next AU-supported gpt-4o or
// switch to gpt-5.
resource gpt4oAuDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-10-01-preview' = {
  parent: cognitiveServices
  name: 'gpt-4o-au'
  sku: {
    name: 'Standard'
    capacity: 10 // Tokens per minute (K)
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-11-20'
    }
    raiPolicyName: 'Microsoft.Default'
  }
}

// ───────────────────────────────────────────────────────────────
// CONTAINER APPS ENVIRONMENT
// ───────────────────────────────────────────────────────────────

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: '${appName}-env-${suffix}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ───────────────────────────────────────────────────────────────
// REDIS CACHE
// ───────────────────────────────────────────────────────────────

resource redis 'Microsoft.Cache/redis@2023-08-01' = {
  name: '${appName}-redis-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'Basic'
      family: 'C'
      capacity: 0
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
  }
}

// ───────────────────────────────────────────────────────────────
// SQL SERVER
// ───────────────────────────────────────────────────────────────

resource sqlServer 'Microsoft.Sql/servers@2022-05-01-preview' = {
  name: '${appName}-sql-${suffix}'
  location: location
  tags: tags
  properties: {
    administratorLogin: 'aigentsadmin'
    administratorLoginPassword: 'P@ssw0rd${uniqueString(resourceGroup().id)}!'
    version: '12.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2022-05-01-preview' = {
  parent: sqlServer
  name: 'aigentsdb'
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource sqlFirewallAllowAzure 'Microsoft.Sql/servers/firewallRules@2022-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ───────────────────────────────────────────────────────────────
// USER ASSIGNED IDENTITY & ACR ROLE ASSIGNMENT
// ───────────────────────────────────────────────────────────────

resource acrPullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${appName}-acr-id-${suffix}'
  location: location
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, acrPullIdentity.id, 'acrpull')
  scope: acr
  properties: {
    principalId: acrPullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

// ───────────────────────────────────────────────────────────────
// CONTAINER APP - API
// ───────────────────────────────────────────────────────────────

resource apiApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: '${appName}-api-${suffix}'
  location: location
  tags: tags
  dependsOn: [
    acrPullRole
  ]
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        corsPolicy: {
          allowedOrigins: ['*']
          allowedMethods: ['*']
          allowedHeaders: ['*']
        }
      }
      registries: [
        {
          server: '${acrName}.azurecr.io'
          identity: acrPullIdentity.id
        }
      ]
      secrets: [
        {
          name: 'azure-ai-endpoint'
          value: cognitiveServices.properties.endpoint
        }
        {
          name: 'azure-ai-key'
          value: cognitiveServices.listKeys().key1
        }
        {
          name: 'google-client-id'
          value: googleClientId
        }
        {
          name: 'google-client-secret'
          value: googleClientSecret
        }
        {
          name: 'sql-connection-string'
          value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=aigentsdb;User ID=aigentsadmin;Password=P@ssw0rd${uniqueString(resourceGroup().id)}!;Encrypt=True;TrustServerCertificate=False;'
        }
        {
          name: 'redis-connection-string'
          value: '${redis.properties.hostName}:6380,password=${redis.listKeys().primaryKey},ssl=True,abortConnect=False'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acrName}.azurecr.io/aigents-api:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: environmentName == 'production' ? 'Production' : 'Staging'
            }
            {
              name: 'AzureAI__Endpoint'
              secretRef: 'azure-ai-endpoint'
            }
            {
              name: 'AzureAI__ApiKey'
              secretRef: 'azure-ai-key'
            }
            {
              name: 'AzureAI__DeploymentName'
              value: 'gpt-4o-au'
            }
            {
              name: 'Google__ClientId'
              secretRef: 'google-client-id'
            }
            {
              name: 'Google__ClientSecret'
              secretRef: 'google-client-secret'
            }
            {
              name: 'ConnectionStrings__aigentsdb'
              secretRef: 'sql-connection-string'
            }
            {
              name: 'ConnectionStrings__redis'
              secretRef: 'redis-connection-string'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/alive'
                port: 8080
              }
              initialDelaySeconds: 60
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/ready'
                port: 8080
              }
              initialDelaySeconds: 60
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '100'
              }
            }
          }
        ]
      }
    }
  }
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: {
      '${acrPullIdentity.id}': {}
    }
  }
}

// ───────────────────────────────────────────────────────────────
// CONTAINER APP - WEB
// ───────────────────────────────────────────────────────────────

resource webApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: '${appName}-web-${suffix}'
  location: location
  tags: tags
  dependsOn: [
    acrPullRole
  ]
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: '${acrName}.azurecr.io'
          identity: acrPullIdentity.id
        }
      ]
      secrets: [
        {
          name: 'google-client-id'
          value: googleClientId
        }
        {
          name: 'google-client-secret'
          value: googleClientSecret
        }
        {
          name: 'sql-connection-string'
          value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=aigentsdb;User ID=aigentsadmin;Password=P@ssw0rd${uniqueString(resourceGroup().id)}!;Encrypt=True;TrustServerCertificate=False;'
        }
        {
          name: 'redis-connection-string'
          value: '${redis.properties.hostName}:6380,password=${redis.listKeys().primaryKey},ssl=True,abortConnect=False'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${acrName}.azurecr.io/aigents-web:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: environmentName == 'production' ? 'Production' : 'Staging'
            }
            {
              name: 'Google__ClientId'
              secretRef: 'google-client-id'
            }
            {
              name: 'Google__ClientSecret'
              secretRef: 'google-client-secret'
            }
            // Web/Program.cs calls AddSqlServerDbContext<AigentsDbContext>("aigentsdb")
            // and crashes the container on startup if this is missing. Without it,
            // every Bicep-deployed web revision goes Unavailable and Container Apps
            // falls back to whatever revision someone wired up by hand.
            {
              name: 'ConnectionStrings__aigentsdb'
              secretRef: 'sql-connection-string'
            }
            {
              name: 'ConnectionStrings__redis'
              secretRef: 'redis-connection-string'
            }
            {
              name: 'services__api__http__0'
              value: 'https://${apiApp.properties.configuration.ingress.fqdn}'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/alive'
                port: 8080
              }
              initialDelaySeconds: 60
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/ready'
                port: 8080
              }
              initialDelaySeconds: 60
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '100'
              }
            }
          }
        ]
      }
    }
  }
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: {
      '${acrPullIdentity.id}': {}
    }
  }
}

// ───────────────────────────────────────────────────────────────
// OUTPUTS
// ───────────────────────────────────────────────────────────────

output webUrl string = 'https://${webApp.properties.configuration.ingress.fqdn}'
output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
output azureAiEndpoint string = cognitiveServices.properties.endpoint
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output redisHostName string = redis.properties.hostName
