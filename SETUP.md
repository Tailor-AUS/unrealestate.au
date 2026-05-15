# 🚀 Aigents - Complete Setup Guide

> ⚠️ **Most of this document is obsolete (Azure-era).** The deploy path is no
> longer Azure Container Apps + Bicep + SQL Server. Production now runs on the
> AloomU stage-0 rack (Postgres, Forgejo CI, Caddy reverse-proxy). See
> [`docs/HANDOFF.md`](docs/HANDOFF.md) §8 for the current hosting + deploy flow,
> and `customers/unrealestate-au/DEPLOYING.md` on the AloomU side for the
> rack-side operator's how-to.
>
> The **Local Development** section near the bottom is still valid.
> The **Azure setup**, **Bicep deploy**, **Google OAuth**, and **GitHub Secrets
> for Azure** sections are dead — kept for now only until SETUP.md is rewritten
> end-to-end. Don't follow them.

**From zero to deployed in 30 minutes.** *(legacy Azure path — obsolete)*

**Using: Azure AI Foundry (GPT-4o) + Azure Container Apps + SQL Server + Redis** *(legacy)*

---

## 📋 Prerequisites

Before you start, make sure you have:

1. **Azure Subscription** - [Create free account](https://azure.microsoft.com/free/)
2. **GitHub Account** - [Sign up](https://github.com/signup)
3. **.NET 8 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
4. **Docker Desktop** - [Download](https://www.docker.com/products/docker-desktop/)
5. **Azure CLI** - [Install](https://docs.microsoft.com/cli/azure/install-azure-cli)
6. **Git** - [Download](https://git-scm.com/downloads)

---

## Step 1: Get Google OAuth Credentials (10 mins)

### 1.1 Create Google OAuth (for Sign-In)

1. Go to https://console.cloud.google.com
2. Create a new project called `Aigents`
3. Search for **"OAuth consent screen"**
   - Select **External**
   - Fill in App name: `Aigents`
   - Add your email for support & developer contact
   - Click **Save and Continue** through the rest
4. Search for **"Credentials"**
   - Click **Create Credentials** → **OAuth client ID**
   - Application type: **Web application**
   - Name: `Aigents Web`
   - Authorized redirect URIs: (leave empty for now, we'll add later)
   - Click **Create**
5. **Copy Client ID and Client Secret**

> **Note:** Azure AI Foundry is deployed automatically via Bicep - no separate API key needed!

---

## Step 2: Create GitHub Repository (5 mins)

### 2.1 Create the Repo

1. Go to https://github.com/new
2. Repository name: `aigents`
3. Select **Private**
4. **Don't** initialize with README (we have one)
5. Click **Create repository**

### 2.2 Push Your Code

Open terminal in your `unrealestate.au` folder:

```bash
# Initialize git
git init

# Add all files
git add .

# Commit
git commit -m "Initial commit - Aigents AI Real Estate Platform"

# Add remote (replace YOUR_USERNAME)
git remote add origin https://github.com/YOUR_USERNAME/aigents.git

# Push
git branch -M main
git push -u origin main
```

---

## Step 3: Set Up Azure (10 mins)

### 3.1 Login to Azure

```bash
az login
```

### 3.2 Create Service Principal for GitHub

```bash
# Get your subscription ID
az account show --query id -o tsv

# Create service principal and save the output
az ad sp create-for-rbac \
  --name "aigents-github" \
  --role contributor \
  --scopes /subscriptions/YOUR_SUBSCRIPTION_ID \
  --sdk-auth
```

**Copy the entire JSON output** - you'll need it for GitHub.

### 3.3 Bootstrap Azure Resources

```bash
# Deploy bootstrap (creates Resource Group + Container Registry)
az deployment sub create \
  --location australiaeast \
  --template-file infra/bootstrap.bicep
```

Note the outputs - especially `acrName`.

---

## Step 4: Configure GitHub Secrets (5 mins)

Go to your GitHub repo → **Settings** → **Secrets and variables** → **Actions**

Add these **Repository Secrets**:

| Secret Name | Value |
|-------------|-------|
| `AZURE_CREDENTIALS` | The entire JSON from Step 3.2 |
| `GOOGLE_CLIENT_ID` | Your Google Client ID |
| `GOOGLE_CLIENT_SECRET` | Your Google Client Secret |

> **That's it!** No AI API key needed - Azure AI Foundry is deployed automatically.

---

## Step 5: Create GitHub Environments (3 mins)

Go to your GitHub repo → **Settings** → **Environments**

### Create "staging" environment:
1. Click **New environment**
2. Name: `staging`
3. No protection rules needed
4. Click **Configure environment**

### Create "production" environment:
1. Click **New environment**
2. Name: `production`
3. Check **Required reviewers** → Add yourself
4. Click **Configure environment**

---

## Step 6: Update Workflow with Your ACR Name (2 mins)

Edit `.github/workflows/cd.yml`:

```yaml
env:
  DOTNET_VERSION: '8.0.x'
  AZURE_RESOURCE_GROUP: 'aigents-rg'
  ACR_NAME: 'YOUR_ACR_NAME_HERE'  # ← Update this!
```

Commit and push:

```bash
git add .github/workflows/cd.yml
git commit -m "Update ACR name"
git push
```

---

## Step 7: First Deployment 🚀 (5 mins)

### Option A: Automatic (push to main)

The CI/CD will trigger automatically on push to main.

### Option B: Manual

1. Go to **Actions** tab in GitHub
2. Click **CD** workflow
3. Click **Run workflow**
4. Select `staging` environment
5. Click **Run workflow**

Watch the deployment in the Actions tab!

### What Gets Deployed:

- ✅ **Azure AI Foundry** with GPT-4o model
- ✅ **Container Apps** (API + Web)
- ✅ **SQL Server** database
- ✅ **Redis** cache
- ✅ **Log Analytics** for monitoring

---

## Step 8: Update Google OAuth Redirect (2 mins)

After deployment, get your Web app URL:

```bash
az containerapp show \
  -n aigents-web-staging \
  -g aigents-rg \
  --query properties.configuration.ingress.fqdn \
  -o tsv
```

Go back to Google Cloud Console → Credentials → Your OAuth Client

Add **Authorized redirect URIs**:
```
https://YOUR_WEB_URL/signin-google
```

---

## ✅ You're Live!

Your app is now running at:
- **Web**: `https://aigents-web-staging.RANDOM.australiaeast.azurecontainerapps.io`
- **API**: `https://aigents-api-staging.RANDOM.australiaeast.azurecontainerapps.io`

---

## 🔄 Deploying Updates

### Automatic (recommended)
Just push to `main` - staging deploys automatically.

### Production
1. Go to **Actions** → **CD**
2. Click **Run workflow**
3. Select `production`
4. A reviewer must approve

---

## 💰 Estimated Costs

| Resource | Monthly Cost |
|----------|-------------|
| Azure AI Foundry (GPT-4o) | ~$10-50 (pay per token) |
| Container Apps (2 apps) | ~$20-50 |
| SQL Server (Basic) | ~$5 |
| Redis (Basic) | ~$15 |
| Container Registry | ~$5 |
| **Total** | **~$55-125/month** |

---

## 🔧 Local Development (Windows ARM64 / Snapdragon)

### Quick Setup (PowerShell)

```powershell
# Start containers (SQL Server, Redis, MailDev)
.\scripts\setup-local.ps1

# Set Azure AI credentials
dotnet user-secrets set "Parameters:azure-ai-endpoint" "https://YOUR-RESOURCE.openai.azure.com/" --project src\Aigents.AppHost
dotnet user-secrets set "Parameters:azure-ai-deployment" "gpt-4o" --project src\Aigents.AppHost

# Set Google OAuth
dotnet user-secrets set "Parameters:google-client-id" "..." --project src\Aigents.AppHost
dotnet user-secrets set "Parameters:google-client-secret" "..." --project src\Aigents.AppHost

# Run with Aspire
dotnet run --project src\Aigents.AppHost
```

### Docker Containers (ARM64 Compatible)

The `docker-compose.yml` uses ARM64-native images that work on both Apple Silicon and Snapdragon:

| Service | Image | Port |
|---------|-------|------|
| SQL Server | Azure SQL Edge | 1433 |
| Redis | Redis Alpine | 6379 |
| MailDev | MailDev | 1080 (web), 1025 (smtp) |

```powershell
# View running containers
docker compose ps

# View logs
docker compose logs -f sqlserver

# Stop all
docker compose down

# Reset data
docker compose down -v
```

Open https://localhost:17225 for Aspire Dashboard.

---

## 🆘 Troubleshooting

### "Container app failed to start"
Check logs:
```bash
az containerapp logs show -n aigents-api-staging -g aigents-rg --follow
```

### "Google sign-in not working"
- Verify redirect URI matches exactly
- Check HTTPS is used

### "AI not responding"
Check Azure AI Foundry deployment:
```bash
az cognitiveservices account deployment list \
  -n aigents-ai-staging \
  -g aigents-rg
```

### "Database connection failed"
- SQL firewall may need updating
- Check connection string in secrets

---

## 📚 Next Steps

1. **Custom Domain**: Add your domain in Azure Container Apps
2. **Monitoring**: Set up Azure Application Insights
3. **Scaling**: Adjust min/max replicas in Bicep
4. **Backups**: Configure SQL Server backups

---

**Need help?** Open an issue in the GitHub repo!
