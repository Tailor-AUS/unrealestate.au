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

Customer-facing site: <https://unrealestate.au> (apex domain bound to the
`aigents-web-production` Azure Container App in `aigents-rg`, `australiaeast`).
Vision/thesis: `docs/VISION.md`. User stories: `docs/USER_STORIES.md`.

## Stack at a glance

- **.NET 9** + **.NET Aspire 9** orchestration (`Aigents.AppHost`).
- **Web** — Blazor Server (interactive server render with prerender) — `Aigents.Web`.
- **API** — minimal API service — `Aigents.Api`.
- **Domain / Infrastructure** — DDD-ish split (`Aigents.Domain`, `Aigents.Infrastructure`).
  Infrastructure holds `PropertyData`, `Syndication`, `CrmIntegration`, `Services/AI` (Azure OpenAI gpt-4o).
- **Local dev** — Aspire AppHost spins up SQL (Azure SQL Edge image, ARM64-friendly), Redis, MailDev via `docker-compose.yml`.
- **Hosting** — Azure Container Apps in `australiaeast` (`bluetree-f1d87971` env). Custom domain bound in `.github/workflows/cd.yml`.

## Run it locally

Full guide: `SETUP.md` (the "Local Development" section). Short version:

```bash
# 1. Start containers (SQL, Redis, MailDev)
./scripts/setup-local.sh        # or .\scripts\setup-local.ps1 on Windows

# 2. Required user-secrets on AppHost (one-time)
dotnet user-secrets set "Parameters:azure-ai-endpoint"   "https://YOUR.openai.azure.com/" --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:azure-ai-deployment" "gpt-4o"                          --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:google-client-id"     "..."                            --project src/Aigents.AppHost
dotnet user-secrets set "Parameters:google-client-secret" "..."                            --project src/Aigents.AppHost

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
  Aigents.Domain/             Entities (Listing, SyndicationStatus, …)
  Aigents.Infrastructure/     PropertyData, Syndication, AI, CRM
  Aigents.ServiceDefaults/    Shared host config

docs/                         VISION, USER_STORIES, ARCHITECTURE, research
.github/workflows/cd.yml      Build → push image → bind unrealestate.au domain
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

## Recent work on `claude/provide-production-url-b2XAK`

Landed on top of `main`:

1. **`BeforeAfterSlider`** (`Components/Shared/BeforeAfterSlider.razor` + `.razor.css` + `wwwroot/js/before-after-slider.js`)
   — pointer/touch drag, keyboard (← → Home End), thumbnail tab switcher.
2. **AI staging band** on the homepage Hero — drag to compare phone snap → AI-staged.
3. **`/list` Step 2** photos use the slider per-photo (replaced click-to-swap toggle).
4. **`Hero.OnInitialized`** now maps `/sell|/buy|/rent|/agent` to the right tab + search-type.
5. **`SellPromoBanner`** (`Components/Shared/SellPromoBanner.razor`) — mini slider + "List your home free in 3 minutes" CTA. Embedded above `/explore` content (suppressed in seller mode) and bottom of `/property/{id}`.
6. **`/sell/manage`** (`Pages/SellManage.razor` + `.razor.css`) — Sale Mode picker (DIY / Open / Exclusive), agent shortlist with comparable sales + estimated live buyer database (sold × avg-buyers + active-pipeline × half), proposal drawer with sliders for commission / marketing / weeks plus a strategy pill row, and a sent-toast.
7. Linked `/sell/manage` from the Hero seller card and added a primary "Manage sale →" button on each listing in `SellerDashboard`.

## What's mock vs. real

- **AI staging pipeline** — UI shipped; real image generation NOT wired. `UploadedPhoto.StyledDataUrl` is currently set to the same data URL as `OriginalDataUrl`; the visual difference is a CSS filter (`saturate(1.18) brightness(1.04) contrast(1.06)`) on `.photo-image.styled-filter`. Search for `Phase B` in `CreateListing.razor` for the swap point.
- **Homepage demo image pairs** — Picsum-seeded placeholders in `Hero._stagingDemo` and `SellPromoBanner` defaults. Swap once real before/after pairs exist (or pipe in user-uploaded ones).
- **`/sell/manage` agents** — fabricated `_agents` array in `SellManage.razor`. Replace with an `IAgentShortlistService` that takes (suburb, priceBand) and returns ranked results.
- **`/sell/manage` proposal submit** — `SendProposal()` only mutates local state. No backend.
- **`/my-offers`** — referenced from links but the page may not exist yet (verify before relying on it).
- **`SellerDashboard` activity feed / hybrid team / offers table** — mostly hard-coded sample data.

## Conventions

- **Commits** — Conventional-ish: `feat(scope): summary` (`feat(/list, home): …`). Imperative present tense.
- **Branch development** — All Claude work lands on a single feature branch; current is `claude/provide-production-url-b2XAK`. Don't push directly to `main`.
- **Comments** — sparing. Only when the WHY isn't obvious. Don't narrate the WHAT.
- **Razor pages** — scoped CSS via `Foo.razor.css` sibling files; `_Imports.razor` already has `Aigents.Web.Components.Shared` so shared components are usable without `@using`.
- **No new top-level docs unless asked.** This file is the exception.

## Open TODOs (next sensible moves)

In rough priority order — pick whichever the user asks for:

1. **Real `IAgentShortlistService`** — replace mock `_agents` in `SellManage.razor`. BFF + Infrastructure impl that ranks agents by recent sales near the listing's suburb/price band.
2. **Open-agency live scoreboard** — once invited, render competing proposals side-by-side (commission, marketing, weeks) with accept/reject.
3. **`/my-offers`** — DIY users need a real offers inbox + accept/reject + counter flow.
4. **AI staging pipeline (Phase B)** — wire `StyledDataUrl` to a real generated image; remove the CSS filter.
5. **Real before/after photo pairs** — replace Picsum seeds in `Hero._stagingDemo` with curated stock pairs.
6. **Seller funnel telemetry** — events for list-create / agent-invited / proposal-accepted / sale-mode-chosen so conversion is measurable.
7. **Buyer chat polish** — review `/chat?mode=buy` flow + copy.
8. **CD URL fix** — `.github/DEPLOYMENT.md` documents `aigents-web-production.azurecontainerapps.io` (missing the env subdomain). Real Azure FQDN is `aigents-web-production.bluetree-f1d87971.australiaeast.azurecontainerapps.io`. Fix the doc.

## Hard rules for any Claude in this repo

- Never create new Markdown docs unless the user explicitly asks (this file is the one exception).
- Never push to `main`; always to the active feature branch.
- Never bypass hooks (`--no-verify`, `--no-gpg-sign`).
- Don't add error handling / null checks / shims for scenarios that can't happen.
- Trust internal code; validate only at system boundaries.
- For UI changes, run the dev server and use the feature in a browser before claiming done. If you can't, say so explicitly.
- Test/build commands: `dotnet build`, `dotnet test`. The CI build on push is the current oracle for compile correctness if you can't build locally.
