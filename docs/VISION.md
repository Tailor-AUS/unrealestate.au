# Aigents — a not-for-profit community project by Tailor

## Status

**This is not a startup.** Aigents is a not-for-profit community project run by **Tailor** — the same team behind the open Australian fuel-price app already used by 50,000+ Aussies to find cheap petrol. The fuel app made petrol prices transparent and pulled the rent-extracting middlemen out of the loop. Aigents is the same idea, applied to real estate listings.

We make no profit. We hold no equity. We charge no margin. The hosted version on `aigents.au` recovers only the literal cost of the GPT tokens and infrastructure we used on a user's behalf — every cent itemised in their dashboard. The full source is Apache 2.0 on GitHub; anyone can fork it, run it themselves, or contribute back.

---

## The Pitch (One Sentence)

**A$1.7 billion leaves Australia every year so that News Corp's REA Group and CoStar's Domain — both US-headquartered — can profit from advertising Australian homes to Australian buyers. We built the open-source alternative, because we got fed up.**

---

## The Core Vision

To stop the **A$1.7 billion per year** rent-extraction from Australian housing — keeping that money in the Australian economy, and giving every Australian agent and homeowner a free, open, AI-powered way to list and find property without funding offshore middlemen.

We are not trying to build a unicorn. We are trying to make the foreign-owned listing portals irrelevant.

---

## The Users (not "customers" — we're not selling)

- **Australian real estate agents** — Brisbane and Gold Coast first. Free to use, no subscription, no tier upsell. They pay only what the AI cost us on their behalf, no margin.
- **Australian buyers** — chat with the buyer-side AI for free, get matched directly to listings, no realestate.com.au required.
- **Self-hosters** — fork the Apache 2.0 source, run it on their own Azure/Hetzner/bare-metal, bring their own AI key. Zero dependency on Tailor's hosted version.

---

## The Product

Agents upload photos and an address. Our AI generates the listing copy, the AVM-style valuation, the buyer Q&A, and the offer analysis. Our buyer-side AI matches active buyers to live listings in real time. Agents stay the agent of record, keep the relationship, and keep the commission. The hosted version bills only the literal AI/infra cost — no margin. The self-hosted version costs nothing to Tailor; you bring your own keys.

---

## The Eight Pillars

### 1. Kill the Photo Tax
Flat monthly fee replaces the $3,000–$4,000 per-listing portal fees charged by realestate.com.au and Domain. The same listing on Aigents costs the agent the GPT-4o tokens it took to generate, plus a flat margin. Nothing else.

### 2. AI Does the Agent's Busywork
- AI-generated listing copy from photos and address
- AI-driven valuation (AVM) with comparable-sales analysis
- AI buyer chat that answers property questions 24/7
- AI offer analysis that normalises "Subject to Finance / B&P" offers into a clear cash-equivalent view for the seller

### 3. Buyer-Side AI Is the Distribution
The buyer-side AI agent is the marketing channel. Buyers chatting with our AI are matched directly to live listings. We do not need realestate.com.au — our demand side feeds our supply side.

### 4. Agent Owns the Relationship
Aigents is the tooling, not the brokerage. The agent stays the agent of record, keeps the client relationship, and earns the full commission. We do not compete with our customers.

### 5. One-Click Migration off REA / Domain
Already on realestate.com.au? Click once, your listings — *your* photos, *your* copy, *your* property — migrate to Aigents. Cancel REA. Save $3,000–$4,000 per listing. Agents own the copyright in their own listing content; we just help them move it.

### 6. Cost-Recovery Only — No Margin, No Profit
You pay only what the AI and infrastructure cost us on your behalf. Every cent itemised in the dashboard. We don't add a margin, we don't run an enterprise tier, we don't sell ads. Aigents is a not-for-profit project; the hosted version is a service, not a product.

### 7. Open Source (Apache 2.0)
The code is public. The costs are public. The algorithm is public. You can fork it. We bet on execution, community, and trust — not on lock-in. An open, Australian-owned platform cannot be quietly acquired and shut down by a foreign incumbent.

### 8. Built on PACT — Protocol for Agent Consensus and Truth
Aigents is built on **[PACT](https://github.com/TailorAU/pact)** — an open-source protocol for multi-agent AI consensus, developed in Australia by Tailor. Aigents is the first production application running on PACT.

A real estate transaction is multi-agent by nature: a buyer-side AI, a seller-side AI, a valuation AI, an offer-analysis AI, and a human agent of record. PACT is how those agents talk to each other safely, auditably, and with the human always in charge.

- **Humans always win.** Any AI decision can be overridden by the human — the agent of record, the seller, the buyer. The AI advises; humans decide.
- **Structured negotiation, not freeform chat.** When multiple AI agents need to agree on a price guide, a counter-offer, or a comparable sale, they use PACT to declare intents, exchange positions, and reach consensus — not free-text chains that hallucinate.
- **Event-sourced truth.** Every AI decision is logged as a PACT operation. Auditable. Reproducible. No black-box "the AI said so."
- **Information barriers between sides.** The buyer-side AI never sees the seller's reserve. The seller-side AI never sees the buyer's maximum offer. PACT's classification framework enforces this at the protocol level.
- **Field-level granularity.** When the AI updates a listing, it updates *the description field*, not the whole record. Audit trails are precise.
- **Silence equals acceptance.** Proposals auto-merge after a TTL unless actively objected to — the same mechanic that makes async real estate negotiation actually work.
- **Zero-trust agent onboarding.** Human agents onboard via invite tokens, not account-creation forms. No spam, no fake listings.

PACT is the trust substrate that makes "we let AI run a real estate transaction" defensible. Without it, multi-agent AI is a liability; with it, it is auditable infrastructure.

---

## Australian Sovereignty

Aigents is built in Australia, hosted in Australia, governed by Australian law, and owned by Australians.

- **Data sovereignty:** all customer data, listings, photos, and AI inference runs in the **Australia East** Azure region. No data crosses the border.
- **Privacy Act 1988 (Cth) compliance:** Australian Privacy Principles are the default. Customers know what we collect, why, and can see or delete it on request.
- **Australian jurisdiction:** disputes are heard in Australian courts. No US-style forced arbitration. No offshore parent company.
- **Australian-owned:** no foreign equity holders with a controlling stake.
- **Australian protocol:** built on PACT, an Australian open-source standard, not a US-controlled framework.

---

## Why This Matters

As of **27 August 2025**, both major Australian residential listing portals are owned by US corporations:

- **REA Group** (realestate.com.au) is approximately **61% owned by News Corp** (NYSE-listed, headquartered in New York City). FY2025 trailing-twelve-month revenue: **A$1.28 billion** ([Cos Market Cap, Dec 2025](https://companiesmarketcap.com/rea-group/revenue/); [Yahoo Finance ownership](https://finance.yahoo.com/news/rea-group-limiteds-asx-rea-051815761.html)).
- **Domain Group** (domain.com.au) was acquired in full by **CoStar Group** (NASDAQ-listed, headquartered in Arlington, Virginia) on 27 August 2025 for an enterprise value of **A$3.0 billion**. Nine Entertainment was paid ~A$1.4 billion for its 60% stake. Domain has been delisted from the ASX ([CoStar press release](https://www.costargroup.com/press-room/2025/costar-group-completes-acquisition-domain-ushering-new-era-innovation-australias); [RISMedia](https://www.rismedia.com/2025/08/27/costar-announces-completion-domain-acquisition-steps-into-australian-market/)).

There is no longer an Australian-controlled major residential listing portal.

In FY2024-25 there were **531,457 residential property sales** in Australia ([Cotality July 2025 chart pack](https://pages.corelogic.com/hubfs/CoreLogic%20AU/Article%20Reports/2507-Cotality-HousingChartPack-July-Report.pdf)). Combined REA + Domain revenue is approximately **A$1.7 billion per year** — roughly **A$3,200 per sale** flowing to two US-headquartered companies, just to advertise Australian homes to Australian buyers on Australian soil.

That money used to recirculate in the Australian economy. Now every cent of it leaves the country.

Aigents keeps it home.

---

## Geographic Roadmap

1. **Phase 1 (now):** Brisbane and Gold Coast — hyper-local mastery.
2. **Phase 2:** South-East Queensland (Sunshine Coast, Toowoomba, Ipswich).
3. **Phase 3:** Sydney and Melbourne.
4. **Phase 4:** National.

---

## Product Roadmap Highlights

- [ ] One-click listing migration tool from realestate.com.au and Domain
- [ ] QLD spatial data integration for precise boundary visualisation
- [ ] AI-driven AVM with comparable-sales analysis
- [ ] Per-listing compute-cost dashboard
- [ ] Apache 2.0 open-source release of the platform
- [ ] Mobile app for on-the-go property discovery
- [ ] Direct buyer-to-agent handoff with offer orchestration
- [ ] Privacy Act 1988 compliance audit and public statement
