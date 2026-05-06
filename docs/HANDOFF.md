# unrealestate.au — Migration Handoff Spec

> Written 2026-05-06. Current state of the app as of branch `claude/rebrand-unrealestate-au-szd3P`.
> Purpose: migrate hosting + source control from GitHub + Azure → Aloomu.

---

## 1. What It Is

**unrealestate.au** — free Australian property portal. Not-for-profit, open source (Apache 2.0).
Run by Tailor (same team behind the open Aussie fuel-price app, 50k+ users).
Thesis: ~A$1.7B/yr leaves Australia to US-owned portals (REA/CoStar). We're the free alternative.

Three user personas:
- **Seller** — lists for $0, manages sale mode (DIY / Open / Exclusive), tracks offers
- **Buyer** — browses, enquires, books inspections, AI chat
- **Agent** — browses open listings, submits proposals, receives buyer leads

---

## 2. Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 9 |
| Orchestration | .NET Aspire 9 (`Aigents.AppHost`) |
| Web frontend | Blazor Server (interactive-server + prerender) |
| API | ASP.NET Core Minimal API + Carter + MediatR (vertical slices) |
| ORM | Entity Framework Core 9 |
| Database | SQL Server (Azure SQL Edge image locally, Azure SQL in prod) |
| Cache / pub-sub | Redis |
| AI | Azure OpenAI gpt-4o (buyer chat + listing generator) |
| Auth | Google OAuth → JWT (HS256, 30-day expiry) |
| Container | Docker (multi-stage, .NET 9 base images) |
| Dev email | MailDev (local only) |

---

## 3. Solution Layout

```
Aigents.sln
src/
  Aigents.AppHost/          Aspire orchestration — wires all services together
  Aigents.Web/              Blazor Server UI (port 8080 in container)
  Aigents.Api/              Minimal API (port 8080 in container; exposed on 5001 locally)
  Aigents.Domain/           Entities only — no EF, no DI
  Aigents.Infrastructure/   EF DbContext, migrations, external service adapters
  Aigents.ServiceDefaults/  Shared OpenTelemetry / health-check config
infra/
  main.bicep                Azure IaC (Container Apps + OpenAI + SQL + Redis)
  acr.bicep                 Container Registry bootstrap
  bootstrap.bicep
.github/workflows/cd.yml    Build → Docker → Bicep deploy → bind unrealestate.au
docker-compose.yml          Local dev infra (SQL, Redis, MailDev)
```

---

## 4. Domain Model (EF entities, all in `Aigents.Domain/Entities/`)

| Entity | Key fields | Notes |
|---|---|---|
| `User` | Id (Guid), Email, Name, PreferredMode | Created on first Google sign-in |
| `Listing` | Id (Guid), Address, Price, Status, UserId | Core seller entity |
| `Agent` | Id (Guid), Name, Agency, Email | Created on agent register |
| `ListingInquiry` | Id, ListingId, AgentId (FK — **nullable hack needed**), Type | Overloaded: holds buyer enquiries + inspection bookings. AgentId is required but buyer enquiries stuff a transient Agent row. **Needs schema cleanup.** |
| `BuyerOffer` | Id, ListingId, Amount, BuyerName | Offer from buyer |
| `AgentProposal` | Id, ListingId, AgentId, CommissionRate | Agent's proposal to seller |
| `Inspection` | Id, ListingId, Token, ScheduledAt | Inspection slot + check-in token |
| `Conversation` / `Message` | AI chat history | Persisted per user session |
| `SyndicationStatus` | ListingId, Portal, SyncedAt | Broadcast tracking |
| `Contact` | CRM-side contact record | |
| `CallRecord` / `VoiceNote` | Agent call logging | |

### Migrations

Three EF migrations in `Aigents.Infrastructure/Data/Migrations/`:
1. `20251203_InitialCreate`
2. `20251221_ModelUpdate_SellerExperience`
3. `20251223_AddProposalsAndOffers`

Run with: `dotnet ef database update --project src/Aigents.Infrastructure --startup-project src/Aigents.Api`

---

## 5. API Surface (`Aigents.Api/Features/`)

| Feature folder | Routes | Notes |
|---|---|---|
| `Auth` | `POST /api/auth/google`, `GET /api/auth/me` | Google ID token → JWT |
| `Listings` | CRUD `/api/listings`, `GET /api/listings/property-report` | Core listing operations |
| `Seller` | `/api/seller/*` | Seller-specific queries |
| `Buyer` | `/api/buyer/*` | Buyer search + enquiry |
| `Inspections` | `/api/inspections/*` | Book slot, generate QR check-in token, check-in URL = `https://unrealestate.au/checkin/{token}` |
| `Chat` | `POST /api/chat` | Proxies to Azure OpenAI; mode=buy\|sell selects system prompt |
| `Property` | `/api/maps/*`, `/api/property/*` | Proxy to MapsOnline + QLD Cadastre |
| `Contacts` | `/api/contacts/*` | CRM contact sync |
| `Crm` | `/api/crm/*` | AgentBox / Rex / VaultRE adapters |
| `Calls` | `/api/calls/*` | Agent call log |
| `VoiceNotes` | `/api/voicenotes/*` | Voice note upload |
| `Leads` | `/api/leads/handoff` | Lead handoff to agent CRM |

---

## 6. External Service Dependencies

| Service | Used for | Config key | Status |
|---|---|---|---|
| **Azure OpenAI** (gpt-4o, australiaeast) | AI chat (buy + sell), listing description generation | `AzureAI__Endpoint`, `AzureAI__DeploymentName` | Live |
| **Google OAuth** | User sign-in | `Google__ClientId`, `Google__ClientSecret` | Live; redirect URI must include prod hostname |
| **MapsOnline** | Property data lookup | hardcoded in `MapsOnlineService.cs` | Live |
| **QLD Cadastre** (ArcGIS REST) | Parcel boundary overlay on Leaflet map | public URL, no key | Live |
| **Domain.com.au API** | Property listings adapter | `DomainPropertyAdapter.cs` | Wired, mock fallback |
| **CoreLogic / RP Data** | AVM + comparable sales | `MockCoreLogicAdapter.cs` | **Mock only** |
| **AgentBox / Rex / VaultRE** | Agent CRM sync | `CrmIntegration/Adapters/` | Adapters exist, not fully wired |
| **Google Maps JS API** | Address autocomplete | API key hardcoded in `App.razor` line 25 (`AIzaSyDz9u...`) | Live — key should move to config |

---

## 7. Configuration / Secrets

### Required at runtime

| Key | Where set | Value in prod |
|---|---|---|
| `ConnectionStrings__sql` | Aspire / env var | SQL Server connection string |
| `ConnectionStrings__redis` | Aspire / env var | Redis connection string |
| `AzureAI__Endpoint` | Aspire parameter / env var | `https://aigents-ai-au-production.openai.azure.com/` |
| `AzureAI__DeploymentName` | Aspire parameter / env var | `gpt-4o` |
| `Google__ClientId` | Aspire parameter / env var | From Google Cloud Console |
| `Google__ClientSecret` | Aspire parameter / env var | From Google Cloud Console |
| `Jwt__Secret` | env var | min 32-char random string |
| `Jwt__Issuer` | `appsettings.json` | `unrealestate.au` |
| `Jwt__Audience` | `appsettings.json` | `unrealestate.au` |

### GitHub Actions secrets (current, to be migrated)

| Secret | Used for |
|---|---|
| `AZURE_CREDENTIALS` | az login service principal |
| `AZURE_SUBSCRIPTION_ID` | Bicep deployment target |
| `GOOGLE_CLIENT_ID` | Passed to Bicep → Container App env |
| `GOOGLE_CLIENT_SECRET` | Same |

---

## 8. Current Hosting (Azure — to be replaced)

| Resource | Name | Notes |
|---|---|---|
| Resource group | `aigents-rg` | australiaeast |
| Container Apps env | `aigents-env-production` | Hosts both apps |
| Web Container App | `aigents-web-production` | Custom domain: `unrealestate.au` + `www.unrealestate.au` |
| API Container App | `aigents-api-production` | Internal ingress only |
| Container Registry | `aigentsacr.azurecr.io` | Stores `aigents-web:*` and `aigents-api:*` images |
| Azure OpenAI | `aigents-ai-au-production` | australiaeast, gpt-4o:2024-11-20, Standard |
| Log Analytics | `aigents-logs-production` | 30-day retention |

### How deploy works today

```
git push main
  → GitHub Actions cd.yml
  → dotnet build + publish
  → docker build+push to ACR (aigentsacr.azurecr.io)
  → az arm-deploy main.bicep (idempotent infra)
  → az containerapp hostname bind unrealestate.au (TXT + CNAME validation)
```

---

## 9. Docker Images

Both are standard multi-stage .NET 9 builds. No exotic base images.

```
# Web
FROM mcr.microsoft.com/dotnet/aspnet:9.0
EXPOSE 8080
ENTRYPOINT ["dotnet", "Aigents.Web.dll"]

# API
FROM mcr.microsoft.com/dotnet/aspnet:9.0
EXPOSE 8080
ENTRYPOINT ["dotnet", "Aigents.Api.dll"]
```

Health check endpoint: `GET /health` on each (returns 200 when DB + Redis reachable).

Build arg `CACHE_BUST=${{ github.sha }}` ensures Docker layer cache is busted on every push — required because `COPY . .` layer was being cached by GitHub Actions even when source changed.

---

## 10. Local Dev Setup

```bash
# 1. Start infra containers
./scripts/setup-local.sh        # or .\scripts\setup-local.ps1

# 2. Set user-secrets on AppHost (one-time)
dotnet user-secrets set "Parameters:azure-ai-endpoint"   "https://..." --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:azure-ai-deployment" "gpt-4o"      --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:google-client-id"    "..."         --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:google-client-secret" "..."        --project src/Aigents.AppHost

# 3. Run via Aspire (starts Web + API + SQL + Redis wired together)
dotnet run --project src/Aigents.AppHost

# Or run Web only (no API needed for UI-only work)
dotnet run --project src/Aigents.Web
```

Aspire dashboard at `https://localhost:15888` shows all service URLs, logs, traces.

---

## 11. What's Mock vs Real

| Feature | State | Where to find the swap point |
|---|---|---|
| AI image staging (before/after) | UI ships; generation **not wired** — same image shown both sides, CSS filter fakes it | `CreateListing.razor` — search `Phase B` |
| CoreLogic AVM / comparable sales | **Mock** — `MockCoreLogicAdapter` returns fixed data | `Aigents.Infrastructure/PropertyData/Adapters/` |
| Agent shortlist on `/sell/manage` | **Mock** — fabricated `_agents` array | `SellManage.razor` |
| Agent counter-proposals (scoreboard) | **Mock** — deterministic hash of agent Guid | `SellManage.razor → GenerateAgentCounter()` |
| `/my-offers` offer inbox | **Mock** — in-memory array | `MyOffers.razor` |
| Property activity strip (viewers/saves) | **Mock** — hardcoded counters | `PropertyDetail.razor` |
| CRM sync (AgentBox/Rex/VaultRE) | Adapters exist, **not fully wired** | `Aigents.Infrastructure/CrmIntegration/Adapters/` |
| Buyer enquiries + inspections | **Real** — persist to DB via `ListingService.UpdateListingAsync` | Creates `ListingInquiry` row (with transient Agent FK hack) |
| Seller dashboard stats | **Real** — computed from DB `_dashboardListings` | `SellerDashboard.razor` |
| AI chat (buy + sell) | **Real** — hits Azure OpenAI gpt-4o | `AzureAiService.cs` |
| Google OAuth | **Real** | `GoogleAuth.cs` |

---

## 12. Known Schema Issues (fix before migrating data)

1. **`ListingInquiry.AgentId` is required** — buyer enquiries and inspection bookings work around this by creating a throwaway `Agent` row. Make it nullable and add `BuyerName`, `BuyerEmail`, `BuyerPhone`, `InquiryType` fields. Needs one EF migration.
2. **`/my-offers` not persisted** — currently fully in-memory mock. Hooks to `ListingInquiry` once item 1 lands.

---

## 13. Source Control State

- **Repo**: `github.com/Tailor-AUS/aigents-dotnet` (rename pending → `unrealestate-au`)
- **Active feature branch**: `claude/rebrand-unrealestate-au-szd3P`
- **Main branch**: `main` — CI deploys from here
- **Convention**: `feat(scope): summary` commit messages, no push to `main` directly

### Remaining hardcoded GitHub URLs (need updating post-rename)

8 URLs in `Hero.razor`, `Agents.razor`, `CONTRIBUTING.md`, `SETUP.md`, `docs/agents/FUNDAMENTALS.md` all still point to `github.com/Tailor-AUS/aigents-dotnet`. Update once repo is renamed or migrated.

---

## 14. Migration Checklist (GitHub + Azure → Aloomu)

- [ ] Mirror repo to Aloomu source control
- [ ] Replace `cd.yml` (GitHub Actions) with Aloomu equivalent pipeline
- [ ] Push Docker images to Aloomu container registry (update image tags in deploy config)
- [ ] Provision equivalent of: 2× Container Apps (web + api), SQL Server, Redis, OpenAI endpoint
- [ ] Migrate DB: `dotnet ef database update` against new SQL instance (3 migrations, clean run)
- [ ] Port secrets (see §7) to Aloomu secrets store
- [ ] Update Google OAuth redirect URI in Google Cloud Console to new hostname
- [ ] DNS: point `unrealestate.au` A record + `www` CNAME to new host's IP/FQDN
- [ ] Rename/update all `github.com/Tailor-AUS/aigents-dotnet` links in code + docs
- [ ] Remove Azure Bicep (`infra/`) or archive it — no longer needed
- [ ] Update `CLAUDE.md` "Hosting" section

---

## 15. Not-for-profit / Open Source Notes

- Apache 2.0 licence
- No user data sold, no margin charged, no equity held
- All AI runs in Australia East for data sovereignty
- Google Maps API key in `App.razor:25` is currently hardcoded — should move to env var before wider exposure
