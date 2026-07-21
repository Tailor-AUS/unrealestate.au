# AGENTS.md

> Onboarding for AI agents picking up work in this repo. Self-contained.
> Companion to `CLAUDE.md` / `SETUP.md` / `docs/VISION.md`.

## Product agent (delivery ownership)

A dedicated **Aigents product agent** owns this repo's delivery loop. North-star
epic: [`#22`](https://github.com/Tailor-AUS/unrealestate.au/issues/22) — first **$10k MRR**.

Local clone (orchestrator home): `C:\tailor_OS\unrealestate.au`.
GitHub: `Tailor-AUS/unrealestate.au` (product brand: **Aigents**).

| Role | Does | Does not |
|---|---|---|
| **Aigents product agent** | Pull `ready` issues; ship product; file GTM context (pricing/ICP pointers) | Draft outreach / press send; put Azure/OAuth secrets in issues |
| **Global orchestrator** | Shape + label; stock `ready`; drain holdings#6 | Bypass this agent for Aigents product work |
| **Owner (Knox)** | Azure AI + OAuth secrets, DNS, legal copy, agency outreach | — |

**Labels:** `requirement` · `ready` · `blocked` · `needs-human-review` · `epic`.

**Ready bar:** clear acceptance criteria; no secrets/PII in the body; owner-only
gates marked `blocked` (or [holdings#4](https://github.com/TailorAU/tailor-holdings/issues/4)).
**`file-don't-draft`** for all GTM. **`Refs #N` only**.

## What Aigents is

Australian-owned, open-source, AI-powered alternative for real-estate agents
done paying the REA/Domain photo tax. Buyer-side AI is distribution; **agent
flat monthly fee** is the beachhead invoice. Stack: .NET 8, Aspire, Blazor,
Azure AI Foundry, Australia East; consensus layer via [PACT](https://github.com/TailorAU/pact).

## Run locally (summary)

See `SETUP.md`. Prerequisites: Docker Desktop, .NET 8 SDK, Azure AI Foundry
access, Google OAuth. Secrets via `dotnet user-secrets` on `src/Aigents.AppHost`
— **never** paste values into GitHub issues.

```powershell
.\scripts\setup-local.ps1
dotnet run --project src\Aigents.AppHost
```

## Commercial north star

First **$10k MRR** = recognised recurring agent subscriptions. Agent files
pricing + agency ICP **pointers** only; owner presses send. Agency contact
lists live off-GitHub.
