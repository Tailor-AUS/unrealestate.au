# 🏠 Aigents — The AI Real Estate Platform for Australian Agents

**Facebook hosts our photos for free. Instagram hosts our videos for free. Marketplace lists our items for free. So why do we pay realestate.com.au and Domain $3,000–$4,000 just to list the most valuable thing we will ever sell — our home?**

Aigents is the Australian-owned, open-source, AI-powered alternative for real estate agents who are done paying the photo tax to foreign-owned listing portals.

Built with .NET 8, Aspire, Blazor, Azure AI Foundry (GPT-4o), vertical slice architecture, and hosted in **Australia East** under Australian law.

> **Read the full thesis:** [`docs/VISION.md`](docs/VISION.md)

---

## ✨ The Seven Pillars

1. **Kill the photo tax** — flat monthly fee, no per-listing portal gouge.
2. **AI does the agent's busywork** — listing copy, valuation, buyer chat, offer analysis.
3. **Buyer-side AI is the distribution** — our own buyer audience, no REA required.
4. **Agent owns the relationship** — Aigents is the tooling, not the brokerage.
5. **One-click migration off REA / Domain** — agents own their photos; we just move them.
6. **Transparent compute-cost pricing** — pay what the AI cost us, plus a flat margin.
7. **Open source (Apache 2.0)** — the code, the costs, the algorithm, all public.

Plus: **Australian-hosted, Australian-owned, Australian-jurisdiction**, Privacy Act 1988 compliant.

---

## ✨ Features

### 🔍 Buyer-Side AI (the distribution channel)
- Chat with AI buyer's agent
- Search on-market and off-market properties
- Get suburb insights and AI-generated valuations
- Book inspections

### 📝 Agent Workflow (the product)
1. **Upload photos + address** → AI generates listing copy, valuation, and buyer Q&A
2. **Review & edit** → Customize headline, description, features
3. **Publish to Aigents** → Listing goes live, matched directly to buyers using our AI agent
4. **Buyer chats with AI** → Qualified leads handed to you with full context
5. **You close the deal** → You stay the agent of record, you keep the commission

*The same listing for the cost of GPT-4o tokens, not $3,000.*

---

## 🚀 Quick Start (Windows ARM64 / Snapdragon)

### Prerequisites
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (ARM64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Azure AI Foundry access

### Setup

```powershell
# 1. Start Docker containers (SQL Server, Redis, MailDev)
.\scripts\setup-local.ps1

# 2. Set your Azure AI credentials
dotnet user-secrets set "Parameters:azure-ai-endpoint" "https://YOUR-RESOURCE.openai.azure.com/" --project src\Aigents.AppHost
dotnet user-secrets set "Parameters:azure-ai-deployment" "gpt-4o" --project src\Aigents.AppHost

# 3. Set Google OAuth credentials
dotnet user-secrets set "Parameters:google-client-id" "YOUR-CLIENT-ID" --project src\Aigents.AppHost
dotnet user-secrets set "Parameters:google-client-secret" "YOUR-SECRET" --project src\Aigents.AppHost

# 4. Run the app
dotnet run --project src\Aigents.AppHost
```

Open the Aspire Dashboard: https://localhost:17225

### Docker Services (ARM64 Compatible)
| Service | Image | Port | Notes |
|---------|-------|------|-------|
| SQL Server | Azure SQL Edge | 1433 | ARM64 native |
| Redis | Redis Alpine | 6379 | ARM64 native |
| MailDev | MailDev | 1080/1025 | Email testing |

```powershell
# View containers
docker compose ps

# View logs
docker compose logs -f

# Stop containers
docker compose down

# Reset all data
docker compose down -v
```

---

## 📁 Project Structure

```
Aigents/
├── src/
│   ├── Aigents.AppHost/          # 🎯 Aspire orchestrator
│   ├── Aigents.Api/              # 🔌 Backend API
│   │   └── Features/             # Vertical slices
│   │       ├── Auth/             # Google SSO
│   │       ├── Chat/             # AI chat (buy journey)
│   │       ├── Leads/            # Lead management
│   │       └── Listings/         # ⭐ Sell journey
│   ├── Aigents.Web/              # 🖥️ Blazor frontend
│   ├── Aigents.Domain/           # 📋 Domain entities
│   └── Aigents.Infrastructure/   # 🔧 EF Core, Azure AI
├── infra/                        # 🏗️ Bicep templates
├── scripts/
│   ├── setup-local.ps1          # Windows setup
│   ├── setup-local.sh           # Mac/Linux setup
│   └── bootstrap-azure.sh       # Azure bootstrap
└── docker-compose.yml           # ARM64 containers
```

---

## 📊 API Endpoints

### Auth
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/auth/google` | Google SSO |
| GET | `/api/auth/me` | Get current user |

### Chat (Buy Journey)
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/chat` | Send message to AI agent |

### Listings (Sell Journey) ⭐
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/listings` | Create listing (AI generates content) |
| GET | `/api/listings/my/{userId}` | Get my listings |
| GET | `/api/listings/{id}` | Get listing details |
| PUT | `/api/listings/{id}` | Update listing |
| GET | `/api/listings/{id}/agreement` | Get agreement text |
| POST | `/api/listings/{id}/sign` | Sign open listing agreement |
| POST | `/api/listings/{id}/publish` | Distribute to local agents |

### Leads
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/leads` | List all leads |
| POST | `/api/leads/handoff` | Handoff to human agent |

---

## 🏗️ Tech Stack

| Layer | Technology |
|-------|------------|
| Orchestration | .NET Aspire |
| Frontend | Blazor Server |
| API | ASP.NET Core + Carter |
| CQRS | MediatR |
| Validation | FluentValidation |
| Database | SQL Server + EF Core |
| Cache | Redis |
| AI | **Azure AI Foundry (GPT-4o)** |
| Auth | Google OAuth |
| Local DB | Azure SQL Edge (ARM64) |

---

## 🚢 Deploy to Azure

```powershell
# 1. Login to Azure
az login

# 2. Bootstrap (first time - use Git Bash or WSL)
./scripts/bootstrap-azure.sh

# 3. Add GitHub secrets:
#    - AZURE_CREDENTIALS
#    - GOOGLE_CLIENT_ID
#    - GOOGLE_CLIENT_SECRET

# 4. Push to main - CI/CD handles the rest!
git push origin main
```

### What Gets Deployed
- ✅ Azure AI Foundry with GPT-4o
- ✅ Container Apps (API + Web)
- ✅ SQL Server
- ✅ Redis Cache
- ✅ Log Analytics

---

## 💰 Estimated Costs

| Resource | Monthly |
|----------|---------|
| Azure AI Foundry | ~$10-50 |
| Container Apps | ~$30 |
| SQL Server Basic | ~$5 |
| Redis Basic | ~$15 |
| **Total** | **~$60-100/month** |

---

## 📝 License

[Apache License 2.0](LICENSE) — Aigents is open source. The code, the costs, and the algorithms are public. Fork it, audit it, contribute back.

## 👥 Team

Built in Australia by Knox & AI
