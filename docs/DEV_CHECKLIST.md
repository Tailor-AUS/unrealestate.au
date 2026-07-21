# Local/dev runbook checklist

Verified on orchestrator host `C:\tailor_OS\unrealestate.au` — **22 Jul 2026**.
Canonical hosting/dev truth: [`HANDOFF.md`](HANDOFF.md). `SETUP.md` Azure sections are obsolete.

| Check | Result | Notes |
|---|---|---|
| .NET SDK | **PASS** | SDKs `9.0.316` and `10.0.110` present |
| Docker Desktop / engine | **PASS** | Docker `28.3.3`; engine running |
| `docker-compose.yml` present | **PASS** | Postgres 16 + Redis + MinIO (+ MailDev) — matches AloomU substrate |
| `scripts/setup-local.ps1` matches compose | **FIXED this pass** | Was still checking Azure SQL Edge / Google OAuth; aligned to Postgres + HANDOFF secrets |
| `dotnet build Aigents.sln` | **PASS** | 0 errors; warnings include NU1902 (OTel exporter), NU1603 (Aspire.Npgsql resolve), unused fields |
| AppHost user-secrets | **FAIL / owner gate** | `dotnet user-secrets list` → *No secrets configured*. Need Azure AI + JWT (+ SMTP if exercising mail) per HANDOFF §10 — values never in GitHub |
| Full Aspire run | **BLOCKED** | Blocked on owner secrets above |
| Happy-path AI listing smoke | **BLOCKED** | Needs secrets + running AppHost → tracks [#26](https://github.com/Tailor-AUS/unrealestate.au/issues/26) |
| Prod `https://unrealestate.au` | **FAIL from this host** | HTTP timeout 20s (22 Jul 2026) |
| Prod `https://api.unrealestate.au/health` (+ `/healthz`) | **FAIL from this host** | HTTP timeout 20s — owner/AloomU rack gate |

## Owner gates (no secrets in-issue)

1. Mint/set AppHost user-secrets locally (HANDOFF §10 keys only — store values off-GitHub).
2. Confirm AloomU stage-0 stack for `unrealestate-web` / `unrealestate-api` (public + API timed out from orchestrator).
3. Ratify commercial model for first $10k MRR — see [`gtm.md`](gtm.md) (NFP cost-recovery vs flat fee conflict).
