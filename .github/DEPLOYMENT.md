# 🚀 Deployment Guide - Aigents.au

## Prerequisites

1. **Azure Subscription** with Owner/Contributor access
2. **GitHub Repository** with this code
3. **Domain** (aigents.au) with DNS access

## Step 1: Create Azure Resources

### Create Resource Group
```bash
az group create --name aigents-rg --location australiaeast
```

### Create Azure Container Registry
```bash
az acr create --name aigentsacr --resource-group aigents-rg --sku Basic --admin-enabled true
```

### Create Service Principal for GitHub Actions
```bash
az ad sp create-for-rbac \
  --name "aigents-github-actions" \
  --role contributor \
  --scopes /subscriptions/{subscription-id}/resourceGroups/aigents-rg \
  --sdk-auth
```

Save the JSON output - you'll need it for `AZURE_CREDENTIALS` secret.

## Step 2: Configure GitHub Secrets

Go to your GitHub repo → Settings → Secrets and variables → Actions

Add these secrets:

| Secret Name | Description | How to Get |
|-------------|-------------|------------|
| `AZURE_CREDENTIALS` | Service Principal JSON | Output from `az ad sp create-for-rbac` |
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID | `az account show --query id -o tsv` |
| `GOOGLE_CLIENT_ID` | Google OAuth Client ID | [Google Cloud Console](https://console.cloud.google.com/apis/credentials) |
| `GOOGLE_CLIENT_SECRET` | Google OAuth Secret | Same as above |

### AZURE_CREDENTIALS Format
```json
{
  "clientId": "xxx",
  "clientSecret": "xxx",
  "subscriptionId": "xxx",
  "tenantId": "xxx"
}
```

## Step 3: Configure Google OAuth

1. Go to [Google Cloud Console](https://console.cloud.google.com)
2. Create a new project or select existing
3. Go to APIs & Services → Credentials
4. Create OAuth 2.0 Client ID (Web application)
5. Add authorized redirect URIs:
   - `https://unrealestate.au/signin-google`
   - `https://www.unrealestate.au/signin-google`
   - `https://aigents-web-production.bluetree-f1d87971.australiaeast.azurecontainerapps.io/signin-google`

## Step 4: Configure DNS for aigents.au

After first deployment, get the Container App URL:
```bash
az containerapp show \
  --name aigents-web-production \
  --resource-group aigents-rg \
  --query properties.configuration.ingress.fqdn \
  --output tsv
```

Add DNS records:
| Type | Name | Value |
|------|------|-------|
| CNAME | @ | {container-app-fqdn} |
| CNAME | www | {container-app-fqdn} |

## Step 5: Deploy

### Option A: Automatic (Push to main)
```bash
git add .
git commit -m "Deploy to production"
git push origin main
```

### Option B: Manual Trigger
Go to GitHub → Actions → "CD - Deploy to Aigents.au" → Run workflow

## Monitoring

### View Logs
```bash
az containerapp logs show \
  --name aigents-web-production \
  --resource-group aigents-rg \
  --follow
```

### Check Status
```bash
az containerapp show \
  --name aigents-web-production \
  --resource-group aigents-rg \
  --query properties.runningStatus
```

## Troubleshooting

### Container not starting
```bash
az containerapp revision list \
  --name aigents-web-production \
  --resource-group aigents-rg \
  --output table
```

### View Container Logs
```bash
az containerapp logs show \
  --name aigents-web-production \
  --resource-group aigents-rg \
  --tail 100
```

### Restart Container
```bash
az containerapp revision restart \
  --name aigents-web-production \
  --resource-group aigents-rg \
  --revision {revision-name}
```

## Cost Optimization

The default configuration uses:
- **Container Apps**: Consumption tier (~$0.000012/vCPU-second)
- **Azure SQL**: Basic tier (~$5/month)
- **Redis Cache**: Basic C0 (~$16/month)
- **Azure OpenAI**: Pay-per-token

Estimated monthly cost: **~$25-50/month** for low traffic.



