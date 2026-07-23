# AGENTS.md

> Onboarding for AI agents picking up work in this repo. Self-contained.
> Companion to `CLAUDE.md` / `SETUP.md` / `docs/VISION.md`.

## Product agent (delivery ownership)

A dedicated **Aigents product agent** owns this repo's delivery loop. North-star
epic: [`#22`](https://github.com/Tailor-AUS/unrealestate.au/issues/22) —
**100,000 active users**.

Local clone (orchestrator home): `C:\tailor_OS\unrealestate.au`.
GitHub: `Tailor-AUS/unrealestate.au` (product brand: **Aigents** / **unrealestate.au**).

| Role | Does | Does not |
|---|---|---|
| **Aigents product agent** | Pull `ready` issues; ship product; file growth/GTM context (metrics, ICP pointers) | Draft outreach / press send; put secrets in issues |
| **Global orchestrator** | Shape + label; stock `ready`; drain holdings#6 | Bypass this agent for Aigents product work |
| **Owner (Knox)** | Rack/DNS, secrets, legal copy, outreach execution | — |

**Labels:** `requirement` · `ready` · `blocked` · `needs-human-review` · `epic`.

**Ready bar:** clear acceptance criteria; no secrets/PII in the body; owner-only
gates marked `blocked` (or [holdings#4](https://github.com/TailorAU/tailor-holdings/issues/4)).
**`file-don't-draft`** for all GTM. **`Refs #N` only**.

## What Aigents / unrealestate.au is

Australian open-source property portal (Apache 2.0). Stack: **.NET 9**, Aspire,
Blazor Server, Postgres, Redis, Azure OpenAI; prod on **AloomU stage-0**
(`https://unrealestate.au`). See [`docs/HANDOFF.md`](docs/HANDOFF.md).

**North star:** 100k active users on the free portal (NFP / cost-recovery AI).
Pricing/MRR packaging is deferred — see [`docs/gtm.md`](docs/gtm.md).

## Truth surfaces

| Surface | Role |
|---|---|
| `docs/HANDOFF.md` | Hosting, secrets keys, deploy, local Aspire |
| `docs/DEV_CHECKLIST.md` | Last machine pass/fail |
| `docs/gtm.md` | Growth model, active-user definition, ICP pointers |
| `docs/metrics.md` | MAU definition, event vocabulary, canonical queries |
| `SETUP.md` | **Partially obsolete** (Azure deploy / Google OAuth) — do not follow those sections |
| Epic [#22](https://github.com/Tailor-AUS/unrealestate.au/issues/22) | North star |

## Run locally (summary)

```powershell
.\scripts\setup-local.ps1          # Postgres/Redis/MinIO/MailDev + build
# set AppHost user-secrets per HANDOFF §10 (never commit values)
dotnet run --project src\Aigents.AppHost
```

## Growth north star

**100k active users** (definition in `docs/gtm.md` — owner ratifies). Agent ships
product + instrumentation and files growth context; owner presses send on outreach.
