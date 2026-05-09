# AloomU customer onboarding — unrealestate.au
Version: 0.1  Last updated: 2026-05-06  Owner: Knox Hart, AloomU CEO  Audience: Knox + Tailor team + future AloomU operator team

This is the onboarding record for **unrealestate.au**, the first production workload migrating from Azure to AloomU substrate.

> **Independence flag — read first.** Knox Hart is both AloomU CEO and a founder of Tailor / unrealestate.au. This document is the written audit trail that the engagement is arms-length. Pricing must be at market rate or documented-discount with explicit expiration per Section 9 and the independence-frame requirements in Section 14. The purpose of this document is to ensure future AloomU operators and investors can see that customer #1 was onboarded correctly.

---

## 1. Customer profile

| Field | Value |
|---|---|
| Legal entity name | Tailor Pty Ltd *(ACN/ABN to be confirmed — populate before contract)* |
| ACN | TBC |
| ABN | TBC |
| Trading name(s) / brand(s) | **unrealestate.au** (primary); previously traded as "Aigents" (being retired) |
| Registered office | Brisbane, Queensland, Australia |
| Founders | Knox Hart |
| Year founded | 2024 |
| Website | https://unrealestate.au |
| Australian-resident shareholder register? | **Yes** — Australian-incorporated, Australian-controlled |

**One-paragraph profile:**
Tailor is a not-for-profit community technology organisation headquartered in Brisbane. Its flagship product, unrealestate.au, is a free, open-source Australian property portal built in direct response to both major residential listing portals (REA Group / realestate.com.au and Domain / domain.com.au) being US-owned. The product charges no listing fees, holds no equity, and recovers only the literal cost of AI inference used on a user's behalf. Tailor's prior product — an open fuel-price app — is used by 50,000+ Australians. unrealestate.au is at public launch stage, currently targeting Brisbane and Gold Coast agents and buyers.

---

## 2. Product

**What is the product?**

unrealestate.au is a three-sided property marketplace:

- **Sellers** list for $0. The platform generates AI listing copy, AI-driven AVM valuations, and manages the sale mode (DIY / Open Listing / Exclusive). Sellers can manage offers, agent proposals, and buyer enquiries from a single dashboard.
- **Buyers** browse and filter properties, chat with an AI buyer's agent (Brisbane + Gold Coast coverage), book inspections, and submit offers — all without a realestate.com.au account.
- **Agents** access an open-listings portal, submit proposals to sellers, and receive warm buyer leads directly matched to their active listings. No subscription, no tier upsell — they pay only the AI inference cost, no margin.

The platform is Apache 2.0 open source. Self-hosters can fork and run it on their own infrastructure with their own AI keys.

**Target market:**
Australian real estate agents and home buyers/sellers, initially Brisbane and Gold Coast. Phase 2: SEQ (Sunshine Coast, Toowoomba, Ipswich). Phase 3: Sydney and Melbourne. Phase 4: national.

**Stage:** Live in production (unrealestate.au is publicly accessible)

**Public launch date / milestone:** Soft-launched. Hard marketing launch TBD — tied to AloomU migration completion.

**Existing customers / users / pilots:** Pre-scale. No paid customers (not-for-profit model). Early listing volume expected from Brisbane/Gold Coast agents during launch push.

---

## 3. Workload requirements on AloomU

| Service | Use? | Use case | Volume estimate |
|---|---|---|---|
| Reason (LLM inference, multi-step reasoning, agentic) | **Yes** | AI buyer's agent chat (mode=buy) + AI listing generator (mode=sell). GPT-4o class model required. Currently hitting Azure OpenAI in australiaeast. | ~100–500 chat queries/day at launch; grow with listing volume |
| Vision (image / OCR / object detection / video) | **Planned (not now)** | AI property photo staging — currently mocked with CSS filter. Real image generation is Phase B. | Low initially; potentially high per listing once wired |
| Voice (speech-to-text / text-to-speech / voice analytics) | **No** | Not in current roadmap | — |
| RAG (retrieval over sovereign data) | **No (future)** | Could index listing corpus for better buyer-AI context. Not wired yet. | — |
| Agentic workflows (multi-step tool use) | **Yes** | Buyer AI queries property data providers (MapsOnline, QLD Cadastre), generates AVM, answers buyer questions with tool calls | Per chat session |
| Static site hosting | **No** | App is Blazor Server (dynamic), not a static site | — |
| Backend application hosting (Docker compose tier) | **Yes — critical** | Two Docker containers: `unrealestate-web` (Blazor Server, port 8080) + `unrealestate-api` (Minimal API, port 8080). Both are standard .NET 9 multi-stage images. | Low-medium. Web: ~50–200 concurrent users at launch. API: same order |
| Database | **Yes — critical** | SQL Server today (EF Core + 3 migrations). If AloomU substrate is Postgres, migration needed (EF supports Postgres; requires provider swap + migration regeneration). | ~1 GB at launch; grows with listing photos (currently stored as base64 in DB — known issue, should move to object storage) |
| Object storage (sovereign S3-equivalent) | **Yes — needed soon** | Property listing photos are currently stored as base64 data URLs in the SQL DB. This is a known scalability problem. Object storage is the right fix. | Low initially; ~5–20 MB per listing, expected hundreds of listings at launch |
| Email (admin@unrealestate.au) | **Yes** | Transactional email for listing confirmations, inspection bookings, offer notifications. Currently MailDev in local dev; no production email wired. | Low — ~50–500 emails/day at launch |
| Latency expectations | Interactive (sub-second for page loads; 1–5 second for AI responses acceptable) | | |
| Peak vs average load | Expected: bursts during property-listing push campaigns. No known seasonal peaks yet. Baseline is low. | | |

---

## 4. Sovereignty mandate

Why sovereign?

- ✅ **Founder values (sovereignty as a founding principle).** The entire product thesis is that A$1.7B/yr is leaving Australia to fund US-owned portals. Hosting on Azure (US-owned) or AWS (US-owned) is strategically incoherent with that thesis. AloomU is the sovereign-AU resolution.
- ✅ **Marketing differentiation.** unrealestate.au's public trust narrative rests on "Australian-owned, Australian-hosted, Australian law." Azure undermines that claim. AloomU makes it provably true.
- ✅ **Data sovereignty commitment to users.** VISION.md states: "all customer data, listings, photos, and AI inference runs in Australia East. No data crosses the border." That commitment is currently only partially honoured (Azure is US-incorporated; data may transit US infrastructure for control-plane operations). AloomU makes the commitment watertight.

**Specific regulatory exposure:** Privacy Act 1988 (Cth) / Australian Privacy Principles. No SOCI Act exposure at current scale. No AUKUS or cleared-personnel workload.

**Public sovereignty claims the customer is making:**
From VISION.md (published): *"Aigents is built in Australia, hosted in Australia, governed by Australian law, and owned by Australians."* and *"all customer data, listings, photos, and AI inference runs in the Australia East Azure region."*

On migration to AloomU, the claim upgrades from "Azure's Australian region" to "AloomU's Brisbane rack" — fully sovereign compute, Australian-incorporated provider. The claim becomes more defensible, not less. unrealestate.au should update its public sovereignty statement on migration to name AloomU as the compute provider (subject to co-marketing consent in Section 10).

---

## 5. Hosting scope on AloomU

- [ ] Static landing page / marketing site
- [x] **Full website (Blazor Server — dynamic, interactive-server render)**
- [x] **Backend application (Docker compose — web + api containers)**
- [x] **Database tier (SQL Server today; flag for Postgres migration if AloomU substrate is Postgres)**
- [x] **AI inference (Reason — GPT-4o class for buyer chat + listing generator)**
- [x] **Object / file storage (needed for listing photo migration off base64-in-DB)**
- [x] **Email (admin@unrealestate.au — transactional, not bulk marketing)**
- [x] **Code hosting (migrate from GitHub to Forgejo at git.unrealestate.au or shared git.aloomu.au)**
- [x] **Container registry (replace Azure Container Registry — store unrealestate-web:* and unrealestate-api:*)**
- [ ] Other

**Migration source:** Azure (Container Apps + Azure SQL + Azure Container Registry + Azure OpenAI)

**Migration sequencing preference:** Parallel run → phased cutover. Keep Azure live until AloomU has passed a 2-week stability check. DNS cutover last.

---

## 6. Domain / DNS

**Domains to be hosted:**
- `unrealestate.au` (apex — primary public site)
- `www.unrealestate.au`
- `api.unrealestate.au` (API service — currently internal in Azure, may want to expose)
- `git.unrealestate.au` (future — Forgejo code hosting)
- `mail.unrealestate.au` (future — email)

**Current DNS provider:** GoDaddy (based on existing deployment documentation)

**Customer controls DNS?** Yes

**TLS strategy:**
- [x] Let's Encrypt (publicly-trusted; recommended for unrealestate.au public surface)
- [ ] AloomU CA
- [ ] Mix

**Sub-domain pattern preference:**
- [x] `unrealestate.au` apex + `www.unrealestate.au`
- [x] `unrealestate.au` + `api.unrealestate.au` + `git.unrealestate.au` + others

---

## 7. Capacity needs

**Expected traffic at launch:** Low to moderate. Estimate 500–2,000 visitors/day in first month post-launch push. Brisbane/Gold Coast agent network as seeding channel.

**Growth projection (12 months):** If agent adoption grows as targeted, 10,000–50,000 visitors/day by end of year is plausible. AloomU Stage-0 capacity may be a constraint at the upper end — see Stage-0 acknowledgements and plan capacity check at 6-month mark.

**Peak events / load patterns:** Anticipated property-market launch campaign (social + LinkedIn). No seasonal peaks known yet. Saturday morning (Brisbane property inspection day) may drive disproportionate buyer-AI chat volume.

**SLA expectations:** 99% uptime acceptable at launch (consistent with Stage-0 substrate posture). Service degradation during Knox's maintenance windows is understood and acknowledged. Full 99.9% expected at Stage-0.5 (Q3 2027 DGX cutover).

**Geographic distribution:** Australia-only. All users are in AU. No CDN requirement at Stage-0. Latency from Brisbane rack to Sydney/Melbourne users (~5–20 ms) is acceptable for Blazor Server interactive render.

---

## 8. Stage-0 caveat acknowledgement

- [x] **Single rack at Moorooka, Brisbane.** No geographic redundancy. If the rack burns down, the unrealestate.au workload is gone unless an independent off-site backup is maintained. Tailor will maintain its own off-site backup of the DB and container images.
- [x] **Optus residential dynamic IP.** If Optus reassigns the IP, unrealestate.au goes offline until DNS is updated. Tailor acknowledges this and accepts the risk during Stage-0.
- [x] **Single SSD, no off-site backup.** AloomU does not back up data off-site at Stage-0. Tailor is responsible for its own off-site DB backup. Listing photos will be migrated to object storage with an independent backup path.
- [x] **Outbound mail deliverability degraded.** Spamhaus PBL on Optus residential range means transactional email (inspection bookings, offer notifications) may land in spam until Stage-0 M6. Tailor will consider a hybrid approach (AloomU SMTP + external relay for critical transactional mail) until deliverability resolves.
- [x] **No 24/7 NOC.** Service availability follows Knox's workstation. Short outages are real. The unrealestate.au app has no retry logic wired yet for AI calls — this should be added before relying on AloomU for production AI inference.
- [x] **Substrate maturity timeline.** Stage-0 (today) → Stage-0.5 Q3 2027 (1× DGX B200 inside QGOV facility) → Stage-1 2029 Q4 (60 MW Gladstone campus). unrealestate.au's AI workload scales with the substrate. Azure OpenAI is retained as a fallback until AloomU Reason service reaches production maturity.
- [x] **AloomU AI services at concept + POC stage.** Production-grade managed Reason/Vision/Voice lands at M9+ (DGX B200 commission). Until then, unrealestate.au runs Azure OpenAI for AI inference and AloomU for hosting. This is an explicitly hybrid posture during the transition.

---

## 9. Commercial terms

**Engagement type:**
- [ ] Paying customer
- [ ] Design partner
- [ ] Launch partner
- [x] **Internal / founder-overlap** — Knox is founder of both AloomU and Tailor/unrealestate.au.

> **Independence safeguard — mandatory documentation:** Knox is the founder of both AloomU and Tailor. This is the highest-risk category for AloomU's independence posture. The below must be filled in and treated as a binding commitment before the workload goes live on AloomU.

**Pricing model:** At-cost infrastructure recovery (mirroring unrealestate.au's own "cost-recovery only" model). Tailor pays AloomU the actual marginal infrastructure cost of hosting the workload — compute, storage, egress, AI tokens. No below-cost subsidy, no above-market markup.

**Initial commitment duration:** 12 months rolling, reviewed annually.

**Payment terms:** Monthly invoice, net-14.

**Below-market pricing flag:** This engagement must not be subsidised below AloomU's marginal cost of service. unrealestate.au's not-for-profit posture does not entitle it to below-cost hosting. AloomU's independence from its own founder's projects is the single most important proof-point for external investors and future customers. Discount rationale: none. Rate: at-cost infrastructure recovery. This is documented here so any future AloomU operator or auditor can verify the terms were arms-length.

---

## 10. Co-marketing

- [x] **Public-naming consent.** AloomU may name unrealestate.au publicly as a customer in marketing materials, grant applications, and pitch decks — with the explicit framing that it is a founder-associated project, not an independent third-party reference customer. This distinction is important for AloomU's independence narrative.
- [x] **Logo usage.** AloomU may display the unrealestate.au logo on aloomu.au/customers/ with the founder-overlap disclosure.
- [x] **Joint announcements.** Coordinate on the migration announcement. Framing: "unrealestate.au migrates from Azure to AloomU — demonstrating that Australian sovereignty in cloud is viable and ready for production."
- [ ] **Reference-customer calls.** Not appropriate as a primary reference customer given founder overlap. Future independent customers should be the reference cases.
- [x] **Case study.** AloomU may publish a technical case study (migration from Azure to sovereign AU substrate). Subject to Tailor review. Founder-overlap must be disclosed in the case study.

**Restrictions:** AloomU should not represent unrealestate.au as an arms-length third-party customer reference when talking to investors or prospective customers. The founder-overlap must be disclosed. unrealestate.au is evidence of technical capability, not commercial independence. Customer #2 — the first genuinely independent commercial customer — is the independence milestone.

---

## 11. Technical contacts

| Role | Name | Email | Phone | Notes |
|---|---|---|---|---|
| Primary technical contact | Knox Hart | TBC | TBC | Founder — sole technical operator at launch |
| Security contact | Knox Hart | TBC | TBC | Incidents + vulnerability disclosure |
| Billing contact | Knox Hart | TBC | TBC | |
| Executive sponsor | Knox Hart | TBC | TBC | Same person — flag for succession planning |

> **Single point of failure note:** All contacts are currently Knox. This is a Stage-0 reality. Before public launch push, Tailor should document a basic runbook so that if Knox is unavailable, at least the "how to restart the containers" procedure is written down.

---

## 12. Migration plan

**Source-of-record for current hosting:** Azure (australiaeast region)
- `aigents-web-production` Container App → replace with AloomU Docker host
- `aigents-api-production` Container App → replace with AloomU Docker host
- Azure SQL (via Bicep-deployed SQL Server) → migrate to AloomU SQL (Postgres migration if needed)
- Azure Container Registry (`aigentsacr.azurecr.io`) → replace with AloomU container registry
- Azure OpenAI (`aigents-ai-au-production`) → **retain as fallback** during AloomU AI ramp; swap to AloomU Reason when production-grade

**Cutover strategy:**
- [x] Parallel run (Azure + AloomU live simultaneously, gradual traffic shift)

**Rollback plan:** Keep Azure Container Apps active for minimum 2 weeks after DNS cutover. DNS TTL set to 5 minutes before cutover to allow rapid rollback. Azure resources not deleted until 30-day stability window passes.

**Migration milestones:**

| Date | Milestone | Owner |
|---|---|---|
| TBC | AloomU Docker host provisioned; unrealestate-web + unrealestate-api containers running | Knox (AloomU) |
| TBC | DB migrated to AloomU (EF migrations applied; data copied from Azure SQL) | Knox (AloomU + Tailor) |
| TBC | Container registry migrated; CI/CD pipeline points to AloomU registry | Knox |
| TBC | Object storage provisioned; listing photos migrated off base64-in-DB | Knox + Tailor dev |
| TBC | Email (admin@unrealestate.au) live on AloomU SMTP | Knox |
| TBC | DNS cutover — `unrealestate.au` A record → AloomU static IP | Knox |
| TBC | 2-week stability window passes; Azure resources decommissioned | Knox |
| TBC | Code hosting migrated from GitHub to Forgejo at git.unrealestate.au | Knox + Tailor |

---

## 13. Onboarding milestones

| Milestone | Target date | Owner | Status |
|---|---|---|---|
| Onboarding doc completed | 2026-05-06 | Knox | In progress |
| DNS configured + TLS active | TBC | Knox | Pending |
| Docker containers live on AloomU | TBC | Knox | Pending |
| Database migrated + EF migrations applied | TBC | Knox | Pending |
| Object storage live + photos migrated | TBC | Knox + Tailor | Pending |
| Email (unrealestate.au) live | TBC | Knox | Pending |
| AI inference — hybrid posture confirmed (AloomU host + Azure OpenAI fallback) | TBC | Knox | Pending |
| DNS cutover | TBC | Knox | Pending |
| Azure decommission | TBC | Knox | Pending |
| Code hosting migrated to Forgejo | TBC | Knox | Pending |
| Public co-marketing announcement | TBC | Knox + Tailor | Pending |

---

## 14. Independence frame

AloomU is an independent Australian-incorporated proprietary limited company providing sovereign cloud and AI services on commercial terms. unrealestate.au is operated by Tailor Pty Ltd. Knox Hart is a founder of both entities.

**The independence requirement is heightened, not relaxed, by the founder overlap.**

unrealestate.au is customer #1. For AloomU to demonstrate commercial independence to future investors, customers, and grant bodies, customer #1 must be onboarded at arms-length commercial terms. A subsidised or free arrangement for the founder's own project would be the exact pattern that breaks the independence frame and makes AloomU look like an internal IT shop rather than a commercial sovereign substrate.

The customer acknowledges:
- [x] Pricing is at market rate (at-cost infrastructure recovery; no below-cost subsidy). Documented in Section 9.
- [x] AloomU is a commercial supplier, not an IT extension of Tailor's organisation.
- [x] AloomU has its own roadmap, customer pipeline, and strategic direction independent of unrealestate.au.
- [x] The founder-overlap is disclosed in all co-marketing materials. unrealestate.au is not presented to external parties as an arms-length reference customer.
- [x] The first genuinely independent commercial customer (customer #2) is AloomU's independence-proof milestone. unrealestate.au onboarding does not satisfy that milestone.

---

## 15. Risk register

| Risk | Likelihood | Impact | Mitigation | Owner |
|---|---|---|---|---|
| Stage-0 substrate outage during unrealestate.au launch campaign | Medium | High | Azure retained as rollback for 30 days post-cutover; 5-min DNS TTL for rapid failback | Knox |
| Optus IP change → DNS + TLS interruption for unrealestate.au | Medium | High | Dynamic-DNS automation; quarterly IP check; static IP from Optus preferred | Knox |
| SQL Server → Postgres migration breaks EF queries | Medium | Medium | EF Core supports Postgres via Npgsql; run full regression against migrations before cutover; keep Azure SQL live during parallel run | Knox + Tailor |
| Photos stored as base64 in DB → DB bloat / slow migration | High (already a problem) | Medium | Migrate to object storage before cutover; this is a known tech debt item | Tailor dev |
| Azure OpenAI fallback dependency post-migration | Low–medium | Medium | Azure OpenAI retained during AloomU Reason ramp; switchover only when AloomU AI is production-stable | Knox |
| Founder-overlap optics damage AloomU's independence narrative | Medium | High | Explicit at-cost commercial terms, documented here, founder-overlap disclosed in all co-marketing | Knox |
| unrealestate.au growth exceeds Stage-0 capacity | Medium | Medium | Capacity review at 6-month post-launch; bridge to commercial colo if DGX timeline slips | Knox |
| Email deliverability (Spamhaus PBL) affects inspection/offer notifications | High (known Stage-0 issue) | Medium | Hybrid relay (AloomU SMTP + external transactional relay) until M6 | Knox |
| Single technical contact (Knox) — bus factor 1 | High | High | Runbook documenting container restart + DNS rollback procedures; document before launch push | Knox + Tailor |

---

## 16. Approval / sign-off

| Party | Name | Signature / email confirmation | Date |
|---|---|---|---|
| Customer (Tailor Pty Ltd) | Knox Hart | TBC | TBC |
| AloomU | Knox Hart | TBC | TBC |

> **Note on same-person sign-off:** Both parties are currently represented by the same individual. Before any material commercial commitment (paid invoice, contract signature), an independent review of these terms by a third party (accountant, solicitor, or AloomU advisory board member) is recommended to protect the independence frame. This is not bureaucracy — it is the receipts that prove the arrangement was genuine.
