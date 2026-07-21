# Growth pointers — path to 100k active users

Truth surface for GTM/growth context. **Agent files pointers only; owner executes
outreach** (`file-don't-draft`). No agency/person names, emails, or phones here.

North-star epic: [#22](https://github.com/Tailor-AUS/unrealestate.au/issues/22).

**Owner directive (22 Jul 2026):** goal is **100,000 active users** — not $10k MRR.
That aligns with the NFP / free-portal thesis in VISION + CUSTOMERS (cost-recovery
AI only). Flat-fee packaging is **deferred** unless owner reopens it.

## Definition of "active user" (proposed — owner ratifies)

Until ratified, instrument all three; report the ratified one as the north-star KPI.

| Candidate | Counts as active | Why |
|---|---|---|
| **A — MAU (recommended)** | Unique authenticated user with ≥1 meaningful action in rolling 30 days | Cleanest product KPI; resists bot inflation |
| **B — WAU** | Same, rolling 7 days | Tighter ops pulse; noisier |
| **C — Engaged visitor** | Unique device/cookie with ≥1 search, listing view, enquiry, or AI chat turn in 30 days | Captures pre-signup demand; weaker identity |

**Meaningful actions (proposed):** search/filter, view listing detail, start AI chat,
submit enquiry, book inspection, create/edit listing, agent proposal.

**100k MAU math (illustrative):** ~3.3k new retained actives/day net of churn over
~30 days is the wrong framing — better: grow **supply (live listings)** and
**demand (buyer sessions)** in lockstep in BNE/GC, then expand geo.

## Growth model (locked until owner changes it)

Free portal → two-sided network:

1. **Supply** — sellers + agents publishing live listings (SEO pages)
2. **Demand** — buyers searching, AI-chatting, enquiring, booking inspections
3. **Loops** — shareable listing URLs, agent lead handoff, open-source forks (secondary)

Revenue (AI cost pass-through) is **not** the north star; keep infra cost visible
so growth does not bankrupt inference.

## Beachhead → scale

| Phase | Geo / segment | Exit criteria (order-of-magnitude) |
|---|---|---|
| 0 | Prod healthy + metrics live | Front door 200; active-user counter trusted |
| 1 | Brisbane + Gold Coast | 1k MAU; ≥100 live listings |
| 2 | SEQ / major QLD | 10k MAU |
| 3 | National AU residential | 100k MAU |

## Instrumentation gaps (product work)

- Property activity counters are **mock** today (`PropertyDetails.razor` / HANDOFF §11).
- Need durable events → warehouse/query for MAU (OpenTelemetry already in ServiceDefaults; product events not defined).
- Public marketing analytics (privacy-respecting) separate from auth MAU.

## ICP criteria (pointers only — shortlists off-GitHub)

Primary for 100k users: **buyers + DIY sellers** in BNE/GC, with agents as supply
accelerators (not the only invoice).

Owner-held shortlists stay off-repo. [#28](https://github.com/Tailor-AUS/unrealestate.au/issues/28)
gets a storage-location pointer only.

## Owner gates that block growth

- holdings#4 **K32** — `unrealestate.au` / API timed out from orchestrator (22 Jul)
- holdings#4 **K33** — local AppHost secrets for agent smoke
- Ratify active-user definition (this doc)
- SEO/indexability of public listing pages (ship + verify)
