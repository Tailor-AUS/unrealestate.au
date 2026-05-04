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
6. **`/sell/manage`** (`Pages/SellManage.razor` + `.razor.css`) — Sale Mode picker (DIY / Open / Exclusive), agent shortlist with comparable sales + estimated live buyer database, proposal drawer with sliders, and a live open-agency scoreboard that generates deterministic per-agent counter-proposals on submit and lets the seller accept one (auto-declines the rest).
7. **`/my-offers`** (`Pages/MyOffers.razor` + `.razor.css`) — DIY offer inbox with summary band, sortable + filterable list, accept/counter/reject + Message-buyer actions, counter drawer with delta vs offer / vs guide, and toasts.
8. **`/property/{id}` two-way funnel** — OPEN-listing pill + live activity strip (viewers/enquiries/offers/saves with pulsing indicator + last-enquiry-ago) + Ask-a-question modal + Book-inspection slot picker. Both modals **persist** via `ListingService.UpdateListingAsync` as `ListingInquiry` rows.
9. **`/chat?buyer=`** — Chat seeds an opening turn scoped to the buyer's offer thread when the seller clicks Message on `/my-offers`.
10. **Seller dashboard hardening** — stats grid (Active Listings / Total Offers / Best Offer / Enquiries) is now computed from real `_dashboardListings` inquiries instead of hardcoded; "+N new" delta pills appear when fresh activity arrived in the last 24h; per-listing "🔥 N offers" / "✨ N new in 24h" pulse pills sit above the listing header.
11. Linked `/sell/manage` from the Hero seller card and added a primary "Manage sale →" button on each listing in `SellerDashboard`.
12. Fixed the OAuth redirect URI in `.github/DEPLOYMENT.md` to use the full env-subdomain ACA hostname.

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
- **Branch development** — All Claude work lands on a single feature branch; current is `claude/provide-production-url-b2XAK`. Don't push directly to `main`.
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
