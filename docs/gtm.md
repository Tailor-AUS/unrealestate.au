# Growth pointers — path to 100k active users

Truth surface for GTM/growth context. **Agent files pointers only; owner executes
outreach** (`file-don't-draft`). No agency/person names, emails, or phones here.

North-star epic: [#22](https://github.com/Tailor-AUS/unrealestate.au/issues/22).

**Owner directive (22 Jul 2026):** goal is **100,000 active users** — not $10k MRR.
That aligns with the NFP / free-portal thesis in VISION + CUSTOMERS (cost-recovery
AI only). Flat-fee packaging is **deferred** unless owner reopens it.

## Definition of "active user" (locked 22 Jul 2026)

**MAU:** a unique authenticated user with at least one meaningful action in the
rolling 30-day window. WAU is a secondary operating metric. Anonymous engaged
visitors are acquisition traffic and do not count toward the 100k north star.

**Meaningful actions:** search/filter, view an active listing, start AI chat,
submit enquiry, book inspection, create/edit listing, submit offer, or submit an
agent proposal. The stable vocabulary and canonical query are in
[`metrics.md`](metrics.md).

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

## Instrumentation status

- Durable privacy-minimised `ProductEvents` + rolling MAU/WAU query: implemented
  on `feat/100k-growth-foundation`.
- Property activity strip now uses real event/inquiry data; fabricated counters removed.
- Public marketing analytics (privacy-respecting) separate from auth MAU.

## ICP criteria (pointers only — shortlists off-GitHub)

Primary for 100k users: **buyers + DIY sellers** in BNE/GC, with agents as supply
accelerators (not the only invoice).

Owner-held shortlists stay off-repo. [#28](https://github.com/Tailor-AUS/unrealestate.au/issues/28)
gets a storage-location pointer only.

## Owner gates that block growth

- holdings#4 **K32** — `unrealestate.au` / API timed out from orchestrator (22 Jul)
- holdings#4 **K33** — local AppHost secrets for agent smoke
- Deploy and verify SEO/indexability on the live front door
