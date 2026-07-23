# Local/dev runbook checklist

Verified on orchestrator host `C:\tailor_OS\unrealestate.au` — **22 Jul 2026**.
Canonical hosting/dev truth: [`HANDOFF.md`](HANDOFF.md). `SETUP.md` Azure sections are obsolete.

| Check | Result | Notes |
|---|---|---|
| .NET SDK | **PASS** | SDKs `9.0.316` and `10.0.110` present |
| Docker Desktop / engine | **PASS** | Docker `28.3.3`; engine running |
| `docker-compose.yml` present | **PASS** | Postgres 16 + Redis + MinIO (+ MailDev) — matches AloomU substrate |
| `scripts/setup-local.ps1` matches compose | **FIXED this pass** | Was still checking Azure SQL Edge / Google OAuth; aligned to Postgres + HANDOFF secrets |
| `dotnet build Aigents.sln -c Release` | **PASS** | 0 warnings, 0 errors |
| `dotnet test Aigents.sln -c Release` | **PASS** | 17 tests across app + API suites; includes real-Postgres concurrency; CI fails when tests fail |
| NuGet vulnerability audit | **PASS** | No vulnerable direct or transitive packages reported for any solution project |
| Web + API production Docker builds | **PASS** | Both Dockerfiles built with 0 warnings and exported final local images |
| Growth migration on Postgres 16 | **PASS** | Initial + `GrowthFoundation` applied from an empty database; inquiry expansion and `ProductEvents` schema inspected |
| AppHost user-secrets | **PARTIAL** | AI/JWT/SMTP keys are configured locally; a newly restricted Google Maps browser key must replace the removed embedded credential |
| Aspire orchestration startup | **PASS** | Aspire 9.5.2 started the distributed app and API/Web resources; external Azure AI and Maps calls were not exercised |
| Public listing HTTP smoke | **PASS** | health, robots, sitemap, SSR metadata and active-only 404 behavior |
| Public listing browser smoke | **PASS** | Synthetic listing → buyer enquiry → persisted typed row + success toast |
| Ownership/auth smoke | **PASS** | API anonymous/cross-user access rejected, including agreement and publish mutations; private completion/dashboard pages redirect anonymous users without putting email in the URL |
| Inquiry reply-channel validation | **PASS** | Questions require email, inspections require phone, and positive offers require email or phone before persistence |
| Product-event concurrency | **PASS** | Automated 12-way PostgreSQL test and local HTTP smoke each produce one deduplicated durable event with no surfaced failures |
| Full AI listing smoke | **NOT RUN** | Still tracks [#26](https://github.com/Tailor-AUS/unrealestate.au/issues/26); production pass blocked by rack edge |
| Prod `https://unrealestate.au` | **FAIL from this host** | Rack-backed HTTP/HTTPS endpoints time out (22 Jul 2026) |
| Prod `https://api.unrealestate.au/health` (+ `/healthz`) | **FAIL from this host** | Rack/network reachability gate K32 |

## Owner gates (no secrets in-issue)

1. Restore AloomU rack-edge reachability for the site, API, Forgejo and mail host.
2. Rotate the formerly embedded Google Maps browser key, restrict it to the production HTTP referrers, and store it off-GitHub.
3. Deploy the growth migration + web/API images after review/merge.
4. Re-run [#26](https://github.com/Tailor-AUS/unrealestate.au/issues/26) and verify the MAU query in production.
