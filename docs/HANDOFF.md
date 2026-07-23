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
| Database | PostgreSQL 16 (AloomU stage-0 substrate; locally via Aspire `AddPostgres`) |
| Cache / pub-sub | Redis |
| AI | Azure OpenAI gpt-4.1 (buyer chat + listing generator) |
| Auth | ASP.NET Core Identity native (email + password, email confirmation, password reset, login alerts). Optional WebAuthn passkey (Fido2NetLib). API issues JWT (HS256, 30-day expiry); Web uses cookie auth. **No third-party IdP.** |
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
.forgejo/workflows/build-push.yml  Forgejo Actions: build + push to git.aloomu.au
.github/workflows/ci.yml    GitHub Actions: PR build + test (no deploy side)
docker-compose.yml          Local dev infra (Postgres, Redis, MailDev)
docker-compose.aloomu.yml   Stage-0 stanza for AloomU's compose stack
```

---

## 4. Domain Model (EF entities, all in `Aigents.Domain/Entities/`)

| Entity | Key fields | Notes |
|---|---|---|
| `User` | Id (Guid, IdentityUser<Guid>), Email, Name, PreferredMode, LastLoginAt/Ip/UserAgent | Extends ASP.NET Core Identity; created on registration with email confirmation |
| `Fido2Credential` | Id, UserId, CredentialId, PublicKey, Nickname | Stored WebAuthn passkeys (one user → many) |
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

Postgres-targeted EF migrations in `Aigents.Infrastructure/Data/Migrations/`. The
context is an `IdentityDbContext<User, IdentityRole<Guid>, Guid>`, so all
`AspNet*` Identity tables are owned by the same DbContext as the domain
tables. The API auto-applies migrations on startup (`db.Database.MigrateAsync()`).

Manual run: `dotnet ef database update --project src/Aigents.Infrastructure --startup-project src/Aigents.Api`

---

## 5. API Surface (`Aigents.Api/Features/`)

| Feature folder | Routes | Notes |
|---|---|---|
| `Auth` | `POST /api/auth/{register,login,confirm-email,resend-confirmation,forgot-password,reset-password,logout}`, `GET /api/auth/me` | ASP.NET Core Identity native, JWT issued on login |
| `Auth (passkey)` | `POST /api/auth/passkey/{register/options,register/complete,login/options,login/complete}`, `GET /api/auth/passkey/list`, `DELETE /api/auth/passkey/{id}` | WebAuthn / Fido2NetLib parallel auth path |
| `Listings` | CRUD `/api/listings`, `GET /api/listings/property-report` | Core listing operations |
| `Seller` | `/api/seller/*` | Seller-specific queries |
| `Buyer` | `/api/buyer/*` | Buyer search + enquiry |
| `Inspections` | `/api/inspections/*` | Book slot, generate QR check-in token, check-in URL = `https://unrealestate.au/checkin/{token}` |
| `Chat` | `POST /api/chat` + Blazor server service | Authenticated conversation service; mode=buy\|sell selects system prompt |
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
| **Azure OpenAI** (gpt-4.1, australiaeast) | AI chat (buy + sell), listing description generation | `AzureAI__Endpoint`, `AzureAI__DeploymentName` | Live |
| **AloomU SMTP** (`mail.aloomu.au:587 STARTTLS`) | Transactional email — confirmation, password reset, login alerts, passkey-added notices | `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password` (file-mounted secret) | Stage-0 ready |
| **MapsOnline** | Property data lookup | hardcoded in `MapsOnlineService.cs` | Live |
| **QLD Cadastre** (ArcGIS REST) | Parcel boundary overlay on Leaflet map | public URL, no key | Live |
| **Domain.com.au API** | Property listings adapter | `DomainPropertyAdapter.cs` | Wired, mock fallback |
| **CoreLogic / RP Data** | AVM + comparable sales | `MockCoreLogicAdapter.cs` | **Mock only** |
| **AgentBox / Rex / VaultRE** | Agent CRM sync | `CrmIntegration/Adapters/` | Adapters exist, not fully wired |
| **Google Maps JS API** | Address autocomplete | `GoogleMaps__ApiKey` (`_FILE` on AloomU) | Configured at runtime; browser key must be referrer-restricted |

---

## 7. Configuration / Secrets

### Required at runtime

| Key | Where set | Value in prod |
|---|---|---|
| `ConnectionStrings__unrealestate` | Aspire / env var (`_FILE` on AloomU) | Postgres connection string |
| `ConnectionStrings__redis` | Aspire / env var | Redis connection string |
| `AzureAI__Endpoint` | Aspire parameter / env var | `https://aigents-ai-au-production.openai.azure.com/` |
| `AzureAI__DeploymentName` | Aspire parameter / env var | `gpt-4.1` |
| `AzureAI__ApiKey` | env var (`_FILE` on AloomU) | from `unrealestate_azure_ai_key` |
| `Jwt__Secret` | env var (`_FILE` on AloomU) | from `unrealestate_jwt_secret`; min 32 chars |
| `Jwt__Issuer` / `Jwt__Audience` | `appsettings.json` | `unrealestate.au` |
| `Smtp__Host` / `Smtp__Port` / `Smtp__Username` | env var | `mail.aloomu.au` / `587` / `admin@unrealestate.au` |
| `Smtp__Password` | env var (`_FILE`) | from `unrealestate_smtp_password` |
| `GoogleMaps__ApiKey` | Aspire parameter / env var (`_FILE`) | from `unrealestate_google_maps_api_key`; HTTP-referrer restricted |
| `App__PublicUrl` | env var | `https://unrealestate.au` — used to build email links |
| `Fido2__ServerDomain` / `Fido2__Origins` | env var / `appsettings.json` | `unrealestate.au` / `https://unrealestate.au` |

### Forgejo secrets (Stage-0 CI on AloomU)

CI pushes images to `git.aloomu.au/unrealestate-au/{web,api}:<sha>`. Forgejo
service principal `unrealestate-ci` holds the `package:write` token.

| Secret | Used for |
|---|---|
| `FORGEJO_CI_TOKEN` | Push images to `git.aloomu.au/unrealestate-au` |

---

## 8. Hosting (AloomU Stage-0)

| Resource | Name | Notes |
|---|---|---|
| Compose stack | AloomU rack | `unrealestate-web`, `unrealestate-api` + shared Postgres, Redis, MinIO, Caddy |
| Public URL | `https://unrealestate.au` | Caddy → `unrealestate-web:8080`, Let's Encrypt TLS |
| API URL | `https://api.unrealestate.au` | Caddy → `unrealestate-api:8080`, Let's Encrypt TLS |
| Registry | `git.aloomu.au/unrealestate-au/{web,api}` | Forgejo OCI registry; CI principal `unrealestate-ci` |
| Azure OpenAI | `aigents-ai-au-production` | australiaeast, gpt-4.1, retained as AI fallback |
| Deploy trigger | `:prod` tag on Forgejo | Webhook → AloomU deployer sidecar → compose pull+up with health-gated rollback |

### How deploy works

```
git push main (GitHub: public OSS source of truth)
  → Forgejo pull-mirror sync (cron, AloomU-side)
  → Forgejo Actions .forgejo/workflows/build-push.yml on `aloomu-stage0` runner
  → docker build+push:
       git.aloomu.au/unrealestate-au/{web,api}:<sha>
       git.aloomu.au/unrealestate-au/{web,api}:latest
  ──────────────────────────────────────────────────────────
                  (manual promotion gate)
  ──────────────────────────────────────────────────────────
  → customers/unrealestate-au/promote.sh <sha> on the AloomU rack
    (soon to be: `git tag vX.Y.Z && git push --tags` — aloomu issue #17)
  → docker buildx imagetools create →
       git.aloomu.au/unrealestate-au/{web,api}:prod (atomic manifest update)
  → Forgejo "package updated" webhook fires
  → AloomU deployer sidecar:
       compose pull → up → wait 30s/90s for healthcheck →
       audit-log → rollback if unhealthy (BLOCKED if EF migration ran)
```

`:prod` is never pushed by CD — only by **`customers/unrealestate-au/promote.sh <sha>`
run on the AloomU rack by Knox/AloomU**. This is the explicit promotion gate.

> **Why not `.github/workflows/promote.yml`?** It used to exist (PR #6) but was
> retired in `claude/forgejo-build-cleanup` once Option B (GitHub-canonical,
> Forgejo-mirror) landed. Forgejo 7.0.16 has a bug where the deployer's
> org-level package webhook persists `events: []` even after explicit
> `events: ["package"]` POST/PATCH calls — so even if `promote.yml` retagged
> `:prod` from the GitHub side, the deployer sidecar on the rack wouldn't be
> notified to pull + swap. Until Forgejo upgrades (or aloomu issue #17 lands
> the `git tag vX.Y.Z` flow), rack-side `promote.sh` is the only viable path.

Auto-rollback is **blocked** when a forward-only EF migration ran in
the failing deploy (Identity tables have versioned columns; new schema
+ old code = broken). On block, AloomU pages Knox for manual roll-
forward. `/healthz` is the deploy health gate — keep it stable, public,
and 200 on success.

**Web deploys reset live SignalR sessions.** Blazor Server's render loop
runs over a SignalR circuit; a `web` container swap kills every active
circuit. Users mid-session see a "Reconnecting…" toast and lose unsaved
client-side state (form drafts, scroll position, in-flight chat turns).
Schedule web deploys for low-traffic hours until blue-green lands.
API-only promotions (`promote.sh --service=api <sha>`) don't disturb circuits.

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
dotnet user-secrets set "Parameters:azure-ai-deployment" "gpt-4.1"     --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:smtp-host"     "mail.aloomu.au"        --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:smtp-username" "admin@unrealestate.au" --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:smtp-password" "<from secret store>"   --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:jwt-secret"    "<32-byte random>"       --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:google-maps-api-key" "<restricted browser key>" --project src/Aigents.AppHost

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
| Property activity strip | **Real** — authenticated views, persisted enquiries/offers; no fabricated saves | `PropertyDetails.razor`, `ProductEventRecorder.cs` |
| Public listing publish | **Real** — owner-authorized activation; outbound agent notification is explicitly disabled until a delivery executor exists | `PublishListing.cs` |
| CRM sync (AgentBox/Rex/VaultRE) | Adapters exist, **not fully wired** | `Aigents.Infrastructure/CrmIntegration/Adapters/` |
| Buyer enquiries + inspections | **Real** — typed buyer fields and a validated reply channel persist via `ListingService.CreateInquiryAsync` | `ListingInquiry.AgentId` is nullable; no fabricated Agent row |
| Seller dashboard stats + buyer activity | **Real** — owner-scoped listings and typed inquiries from PostgreSQL; prototype sections do not render | `SellerDashboard.razor` |
| AI chat (buy + sell) | **Real** — authenticated shared conversation service hits Azure OpenAI | `ChatConversationService.cs`, `AzureAiService.cs` |
| Auth (sovereign-from-day-1) | **Real** — Identity native + transactional SMTP, optional WebAuthn passkey | `Aigents.Api/Features/Auth/NativeAuth.cs`, `PasskeyAuth.cs`; `Aigents.Web/Components/Account/Pages/*` |

---

## 12. Known Schema Issues (fix before migrating data)

> **Expand-contract rule** — auto-rollback on AloomU is blocked when a
> forward-only EF migration ran (see §8). So every schema change ships as
> **two migrations across two deploys**: (1) additive/nullable expand —
> code reads + writes the new shape, old shape still works, deploy is
> rollback-safe; (2) destructive contract — drop the old column / FK /
> table only once you've confirmed the expand deploy is stable. Never
> bundle "add new + drop old" into a single migration.

1. **Buyer-inquiry expand prepared** — `GrowthFoundation` makes `AgentId`
   nullable and adds `BuyerName`, `BuyerEmail`, `BuyerPhone`, `OfferAmount`,
   and `InquiryType`.
   New code writes buyer fields with no transient Agent. After the expand deploy
   is stable, inspect and remove any legacy orphan Agent rows in a separate
   contract migration.
2. **`/my-offers` not persisted** — currently fully in-memory mock. It can now
   be wired to the expanded `ListingInquiry` shape.

---

## 13. Source Control State

- **Repo**: `github.com/Tailor-AUS/unrealestate.au` (renamed from `aigents-dotnet` on 2026-05-13; GitHub redirects the old slug)
- **Active feature branch**: `feat/100k-growth-foundation`
- **Main branch**: `main` — CI deploys from here
- **Convention**: `feat(scope): summary` commit messages, no push to `main` directly

### Hardcoded GitHub URLs (updated 2026-05-13)

URLs in `Hero.razor`, `Agents.razor`, `CONTRIBUTING.md`, `SETUP.md`, `docs/agents/FUNDAMENTALS.md`, `scripts/setup-cicd.ps1` updated from `github.com/Tailor-AUS/aigents-dotnet` → `github.com/Tailor-AUS/unrealestate.au` in `claude/repo-rename-cleanup`. GitHub redirects the old slug, so anything missed still resolves.

---

## 14. Migration Checklist (GitHub + Azure → Aloomu)

> **AloomU status update (2026-05-15)** — repo migrated to Forgejo
> (`git.aloomu.au/unrealestate-au/unrealestate.au`, 2026-05-13; 8 branches
> preserved). Forgejo CI principal `unrealestate-ci` wired; first build
> off `main` produced `web:607a0bc…` + `api:607a0bc…` (+ `:latest`).
> File-mounted secrets present + mounted on the rack; containers
> `(healthy)`. Compose stanza in sync with `docker-compose.stage0.yml`.
> Going forward: **GitHub is the public OSS source of truth; Forgejo
> pull-mirrors from GitHub** (mirror flip happens once this PR's
> workflow file lands on GitHub `main`). Reference how-to:
> `git.aloomu.au/knox/aloomu/.../customers/unrealestate-au/DEPLOYING.md`.

- [x] Migrate from SQL Server to Postgres (done in `claude/rebrand-unrealestate-au-szd3P`)
- [x] Replace Google OAuth with sovereign Identity native + WebAuthn (done in `claude/aloomu-stage0-followup`)
- [x] Wire AloomU SMTP for transactional email (done in `claude/aloomu-stage0-followup`)
- [x] Register Forgejo CI principal (`unrealestate-ci`) at `git.aloomu.au` and store the `package:write` token *(done 2026-05-13; first build SHA `607a0bc…` confirms wiring)*
- [x] Build + push images to `git.aloomu.au/unrealestate-au/{web,api}:<sha>` *(Forgejo Actions `.forgejo/workflows/build-push.yml` on `aloomu-stage0` runner; first end-to-end push 2026-05-13)*
- [x] Provide stanza updates to AloomU – onboarding for merge into `docker-compose.stage0.yml` *(AloomU confirmed shape matches as of 2026-05-15; only diff is `:prod` pin vs. `<GIT_SHA>` template, which is the deploy contract)*
- [x] Migrate source repo to `git.aloomu.au/unrealestate-au/unrealestate.au` *(done 2026-05-13; Forgejo to flip from "regular repo" to "pull-mirror of GitHub" once `.forgejo/workflows/build-push.yml` lands in GitHub `main`)*
- [ ] DNS: confirm GoDaddy MX/SPF/DKIM/DMARC records on `unrealestate.au` (AloomU side)
- [ ] DNS: point `unrealestate.au` A record + `www` CNAME to AloomU edge once Caddy snippet flips
- [x] Rename/update remaining `github.com/Tailor-AUS/aigents-dotnet` links in code + docs (done 2026-05-13)
- [x] Remove Azure Bicep (`infra/`) + Azure-era scripts (`setup-cicd.{ps1,sh}`, `bootstrap-azure.{ps1,sh}`) — done 2026-05-15 in `claude/azure-era-cleanup`
- [x] Update `CLAUDE.md` "Hosting" section *(done in `claude/forgejo-primary-build-ci`; split into Hosting + Source control + Build CI bullets, item 4 of "Recent platform-shape changes" rewritten for Forgejo-native CD)*

---

## 15. Not-for-profit / Open Source Notes

- Apache 2.0 licence
- No user data sold, no margin charged, no equity held
- All AI runs in Australia East for data sovereignty
- Google Maps browser key is runtime-configured and must remain HTTP-referrer restricted; rotate the formerly embedded key before deployment
