# CLAUDE.md — handoff brief for any Claude session in this repo

> Read this first. It's the briefing you'd give a new colleague who just walked in.
> Auto-loaded by Claude Code on session start. Keep it terse — link out, don't paste in.

## What this is

**unrealestate.au** — Australian-owned, not-for-profit, open-source real-estate
portal built on the **Aigents** platform. Run by Tailor (the team behind the
50,000-user Aussie fuel-price app). Thesis: ~A$1.7B/yr drains overseas to advertise
Australian homes on US-owned portals (REA, CoStar/Domain). We're the free
Australian alternative — sellers list for $0, buyers and agents browse for $0,
hosted in Australia East.

Customer-facing site: <https://unrealestate.au> served by AloomU's Caddy
reverse-proxy in front of the `unrealestate-web` container.
Vision/thesis: `docs/VISION.md`. User stories: `docs/USER_STORIES.md`.
Stage-0 deployment + auth detail: `docs/HANDOFF.md`.

## Stack at a glance

- **.NET 9** + **.NET Aspire 9** orchestration (`Aigents.AppHost`, local dev only).
- **Web** — Blazor Server (interactive server render with prerender) — `Aigents.Web`.
- **API** — minimal API service — `Aigents.Api`.
- **Domain / Infrastructure** — DDD-ish split (`Aigents.Domain`, `Aigents.Infrastructure`).
  Infrastructure holds `PropertyData`, `Syndication`, `CrmIntegration`, `Services/AI` (Azure OpenAI gpt-4.1).
- **Auth** — sovereign-from-day-1: ASP.NET Core Identity native (email + password,
  email confirmation, password reset, login alerts). Optional WebAuthn passkey
  via Fido2NetLib. **No third-party IdP.** Web uses cookie auth; API issues JWT.
- **Database** — PostgreSQL 16 (`unrealestate` database on AloomU's Postgres).
- **Email** — transactional via AloomU's mailserver (`mail.aloomu.au:587`
  STARTTLS, MailKit). `admin@unrealestate.au` / `noreply@unrealestate.au`.
- **Local dev** — Aspire AppHost spins up Postgres, Redis, MailDev via `docker-compose.yml`.
- **Hosting** — AloomU rack (Docker Compose stack: `unrealestate-{web,api}` +
  shared Postgres / Redis / MinIO / Caddy). Images in
  `git.aloomu.au/unrealestate-au/{web,api}:<sha>`, pushed by GitHub Actions
  (`.github/workflows/cd.yml`) on merge to `main`.

## Run it locally

Full guide: `SETUP.md` (the "Local Development" section). Short version:

```bash
# 1. Start infra containers (Postgres, Redis, MailDev)
./scripts/setup-local.sh        # or .\scripts\setup-local.ps1 on Windows

# 2. Required user-secrets on AppHost (one-time)
dotnet user-secrets set "Parameters:azure-ai-endpoint"   "https://YOUR.openai.azure.com/" --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:azure-ai-deployment" "gpt-4.1"                         --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:smtp-host"           "mail.aloomu.au"                  --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:smtp-username"       "admin@unrealestate.au"           --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:smtp-password"       "<from password manager>"         --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:jwt-secret"          "<random 32-byte string>"          --project src/Aigents.AppHost

# 3. Run the whole graph through Aspire
dotnet run --project src/Aigents.AppHost
```

The Aspire dashboard prints the Web/API URLs (typically `https://localhost:7xxx`).
For UI-only tweaks you can also run the Web project alone:
`dotnet run --project src/Aigents.Web`.

Tests, if/when added: `dotnet test`. Build everything: `dotnet build`.

## Repo map

```
src/
  Aigents.AppHost/            Aspire orchestration entry point
  Aigents.Web/                Blazor Server UI
    Components/
      Pages/                  Routed pages (see "Routes" below)
      Shared/                 Reusable components: Hero, BeforeAfterSlider,
                              SellPromoBanner, AddressAutocomplete, Chat, …
      Layout/                 MainLayout
    wwwroot/js/               JS interop (leaflet, globe-viz, before-after-slider, …)
  Aigents.Api/                Minimal API
    Features/Auth/              NativeAuth (Identity) + PasskeyAuth (WebAuthn)
  Aigents.Domain/             Entities (User : IdentityUser<Guid>, Listing, …)
  Aigents.Infrastructure/     PropertyData, Syndication, AI, CRM, Services/Auth, Services/Email
    Data/Migrations/            EF migrations (auto-applied on API startup)
  Aigents.ServiceDefaults/    Shared host config

docs/                         VISION, USER_STORIES, ARCHITECTURE, HANDOFF, research
docker-compose.aloomu.yml     Stage-0 stanza Knox merges into AloomU's compose stack
.github/workflows/cd.yml      Build → push image to git.aloomu.au/unrealestate-au
SETUP.md                      End-to-end deployment + local-dev guide
```

## Routes & user-story coverage

All three personas have full coverage; the Hero's audience tabs surface them.

| Persona | Routes | Notes |
|---------|--------|-------|
| Seller  | `/list`, `/sell/manage`, `/seller/dashboard` (alias `/my-listings`), `/chat?mode=sell` | `/sell/manage` is the post-listing DIY/Open/Exclusive picker + agent shortlist. |
| Buyer   | `/explore`, `/property/{guid}`, `/chat?mode=buy` | Buyer pages also surface a `SellPromoBanner` to convert browsers into sellers. |
| Agent   | `/agents`, `/agents/register`, `/agents/listings` (alias `/agents/browse`), `/agents/listing/{id}` | |
| Cross   | `/`, `/buy`, `/sell`, `/rent`, `/agent` (all render `Home.razor` → `Hero.razor`; `Hero.OnInitialized` maps the path to the right audience tab + search type). | |
| Account | `/Account/{Login,Register,RegisterConfirmation,ConfirmEmail,ResendConfirmation,ForgotPassword,ResetPassword,Logout,AccessDenied}` | Identity native flow; SSR forms backed by `SignInManager` / `UserManager`. |

## Recent platform-shape changes (post-AloomU migration)

Now on `main`:

1. **Sovereign-from-day-1 auth.** `Features/Auth/GoogleAuth.cs` deleted; replaced
   by `NativeAuth.cs` (register / confirm / login / forgot / reset / me, all
   issuing JWT) and `PasskeyAuth.cs` (Fido2NetLib WebAuthn). Web has SSR
   account pages under `Components/Account/Pages`.
2. **Postgres + AloomU substrate.** `AddSqlServer` → `AddPostgres`; Azure SQL
   Edge image and Bicep deploy retired. `docker-compose.aloomu.yml` is the
   stanza Knox feeds into the AloomU compose stack.
3. **Transactional email via AloomU SMTP.** MailKit-backed sender with branded
   templates for confirmation, reset, login alerts, and passkey-added notices.
4. **CD pipeline points at Forgejo.** `.github/workflows/cd.yml` now logs in
   to `git.aloomu.au` and pushes `unrealestate-au/{web,api}:<sha>` + `:latest`.
   The Azure Bicep / Container Apps deploy steps are gone.
5. **`:prod` is the AloomU deploy trigger.** `.github/workflows/promote.yml`
   (`workflow_dispatch`) is the only thing that ever writes the `:prod` tag.
   Atomic via `docker buildx imagetools create`. CD never pushes `:prod`.
   Promotion fires the deployer sidecar on AloomU which health-gates the
   swap; auto-rollback is blocked when a forward-only EF migration ran.
6. **EF migration regenerated** for Postgres; auto-applied on API start.
   Includes Identity tables (`AspNetUsers`, etc.) + new `Fido2Credentials`.
   `/healthz` is the deploy health gate — accepts GET + HEAD, must stay
   200 + public + same path. Treat as stable contract.

## Deploy constraints (per AloomU 2026-05-14)

- **EF migrations are one-way for rollback.** Auto-rollback is blocked
  when a forward-only migration ran in the failing deploy (new schema +
  old code would brick Identity). Use **expand-contract**: one deploy
  ships the additive change (nullable column, new table) and is safe to
  roll back; a *later* deploy does the destructive cleanup (drop the old
  column) once it's confident. Never bundle "add new + drop old" into a
  single migration.
- **Every `web` deploy resets live SignalR sessions** — users mid-session
  get a "Reconnecting…" toast and lose unsaved client-side state.
  Schedule web deploys for low-traffic hours until blue-green lands.

## What's mock vs. real

- **AI staging pipeline** — UI shipped; real image generation NOT wired. `UploadedPhoto.StyledDataUrl` is currently set to the same data URL as `OriginalDataUrl`; the visual difference is a CSS filter (`saturate(1.18) brightness(1.04) contrast(1.06)`) on `.photo-image.styled-filter`. Search for `Phase B` in `CreateListing.razor` for the swap point.
- **Homepage demo image pairs** — Picsum-seeded placeholders in `Hero._stagingDemo` and `SellPromoBanner` defaults. Swap once real before/after pairs exist (or pipe in user-uploaded ones).
- **`/sell/manage` agents** — fabricated `_agents` array in `SellManage.razor`. Replace with an `IAgentShortlistService` that takes (suburb, priceBand) and returns ranked results.
- **`/sell/manage` open-agency scoreboard** — `GenerateAgentCounter()` is a deterministic mock that hashes the agent's Guid. Swap when proposals API exists. Accept/decline only mutates local state.
- **`/sell/manage` proposal submit** — `SendProposal()` only mutates local state. No backend.
- **`/my-offers`** — entire offer inbox is in-memory mock (`_offers` array in `MyOffers.razor`). Accept/counter/reject only mutate local state.
- **`/property/{id}` activity strip** — viewer/enquiry/offer/save counters are mock; real telemetry not wired. Buyer enquiries + inspection requests **do persist** via `ListingService.UpdateListingAsync` (they create a `ListingInquiry` row, mirroring the existing offer hack — note the entity still requires an Agent FK so each enquiry creates a transient Agent row).
- **`SellerDashboard` stats** — counts (active listings, total offers, best offer, enquiries, "+N new in 24h") are now computed from real `_dashboardListings` inquiries. Activity feed / hybrid team / offers table below are still hard-coded sample data.

## Conventions

- **Commits** — Conventional-ish: `feat(scope): summary` (`feat(/list, home): …`). Imperative present tense.
- **Branch development** — Work lands on a `claude/<topic>` branch and is PR'd to `main`. Don't push directly to `main`.
- **Comments** — sparing. Only when the WHY isn't obvious. Don't narrate the WHAT.
- **Razor pages** — scoped CSS via `Foo.razor.css` sibling files; `_Imports.razor` already has `Aigents.Web.Components.Shared` so shared components are usable without `@using`.
- **No new top-level docs unless asked.** This file is the exception.

## Open TODOs (next sensible moves)

In rough priority order — pick whichever the user asks for:

1. **Real `IAgentShortlistService`** — replace mock `_agents` in `SellManage.razor`. BFF + Infrastructure impl that ranks agents by recent sales near the listing's suburb/price band.
2. **`ListingInquiry` schema cleanup** — current entity requires an `AgentId`, so buyer enquiries hack a transient Agent row into the FK. Make `AgentId` nullable + add buyer fields (`BuyerName`, `BuyerEmail`, `BuyerPhone`, `InquiryType`) so enquiry/inspection/offer can share one persistence path cleanly. Will need an EF migration.
3. **Real proposals API** — `_agentResponses` + `GenerateAgentCounter` in `SellManage.razor` are pure client mocks; replace with a real proposal entity + service when an agent can actually reply.
4. **Persist `/my-offers` data** — currently fully in-memory mock. Hook to `ListingInquiry` rows once schema cleanup (item 2) lands.
5. **AI staging pipeline (Phase B)** — wire `StyledDataUrl` to a real generated image; remove the CSS filter.
6. **Real before/after photo pairs** — replace Picsum seeds in `Hero._stagingDemo` with curated stock pairs.
7. **Activity-strip telemetry** — replace mock viewer/enquiry/save counts on `/property/{id}` with real events.
8. **Seller funnel telemetry** — events for list-create / agent-invited / proposal-accepted / offer-accepted / enquiry-received so conversion is measurable.
9. **Buyer chat polish** — review `/chat?mode=buy` flow + copy.

## Hard rules for any Claude in this repo

- Never create new Markdown docs unless the user explicitly asks (this file is the one exception).
- Never push to `main`; always to the active feature branch.
- Never bypass hooks (`--no-verify`, `--no-gpg-sign`).
- Don't add error handling / null checks / shims for scenarios that can't happen.
- Trust internal code; validate only at system boundaries.
- For UI changes, run the dev server and use the feature in a browser before claiming done. If you can't, say so explicitly.
- Test/build commands: `dotnet build`, `dotnet test`. The CI build on push is the current oracle for compile correctness if you can't build locally.
