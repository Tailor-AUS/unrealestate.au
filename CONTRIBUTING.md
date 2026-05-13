# Contributing to Aigents

Thanks for your interest in Aigents — the Australian-owned, open-source, AI-powered alternative to foreign-owned real estate listing portals.

This file describes how to contribute. The full thesis lives in [`docs/VISION.md`](docs/VISION.md). The license is [Apache 2.0](LICENSE).

---

## Ground rules

1. **Australian focus first.** Brisbane and Gold Coast are the launch markets. Features that work everywhere but solve no Australian problem are deprioritised in favour of features that nail QLD specifically.
2. **The agent owns the relationship.** Aigents is the *tooling*, not the brokerage. We do not build features that compete with the agents who pay us. (See Pillar 4.)
3. **Compute cost is visible.** New AI calls must be measurable per request and surfaced in the cost dashboard. (See Pillar 6.)
4. **Data stays in Australia.** No new dependency may route customer data, listings, photos, or AI inference through a non-Australian region without an explicit thesis exception. Cognitive Services lives in `australiaeast`. (See "Australian Sovereignty" section of `docs/VISION.md`.)
5. **PACT is the agent substrate.** New multi-agent flows go through [PACT](https://github.com/TailorAU/pact), not ad-hoc free-text chains. (See Pillar 8.)

---

## Local development

See [`README.md`](README.md) for the full setup. Short version:

```powershell
.\scripts\setup-local.ps1
dotnet user-secrets set "Parameters:azure-ai-endpoint" "https://YOUR-RESOURCE.openai.azure.com/" --project src\Aigents.AppHost
dotnet user-secrets set "Parameters:azure-ai-deployment" "gpt-4o" --project src\Aigents.AppHost
dotnet run --project src\Aigents.AppHost
```

The Aspire dashboard runs at https://localhost:17225.

---

## Branching & commits

- Branch off `main`. Name branches with a short slug describing the change (e.g. `fix-listing-validation`, `feat-onboarding-wizard`).
- Commit messages: imperative mood, lowercase verb, optional scope.
  - `feat(listings): add bulk migration from REA`
  - `fix(infra): bump model to non-deprecated version`
  - `docs: clarify sovereignty pillar`
- Reference the issue number in the commit body if there is one.
- Squash commits into something readable before opening the PR.

## Pull requests

- Open the PR against `main`.
- Title: same conventions as the commit message.
- Description: what changed, why, and how to verify it. If it touches infra, include the expected Azure cost delta.
- CI must be green. The CD pipeline (`.github/workflows/cd.yml`) deploys `main` directly to production, so do not merge a PR that you have not run end-to-end at least once.
- One reviewer approval is required.

## What gets merged

- Bug fixes with a clear reproduction.
- Features that advance one of the eight pillars.
- Documentation that closes a real gap (not "polish" for its own sake).
- Test coverage for code that handles money, listings, or buyer/seller PII.

## What does not get merged

- Code that routes customer data offshore.
- Features that turn Aigents into a brokerage (i.e. compete with the paying agents).
- Hard-coded foreign-portal scraping that violates third-party terms of service. Migration of an *agent's own listings* (which the agent owns the copyright in) is fine; scraping competitors' listings is not.
- Vendor lock-in: SDKs, models, or services that cannot be replaced without rewriting more than a feature module.

---

## Reporting bugs

Open an issue at https://github.com/Tailor-AUS/unrealestate.au/issues with:

- Steps to reproduce (concrete, copy-pasteable where possible).
- What you expected vs. what happened.
- Environment (local dev / production, browser, OS, .NET version).
- Logs or screenshots if relevant.

For security issues, **do not** open a public issue. Email security@aigents.au with the details and we will acknowledge within two business days.

---

## Code of conduct

Be civil. Be specific. Disagree about the work, not the person. Anyone who can't manage that gets removed from the project.

---

## License

By contributing you agree that your contribution is licensed under the [Apache License 2.0](LICENSE), the same license as the rest of the project.
