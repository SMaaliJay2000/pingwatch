# PingWatch — Claude Code build prompts

Paste these into Claude Code one at a time, in order. Each ends when the project builds and tests pass.
Put `CLAUDE.md` in the repo root and the exported design doc at `docs/SPEC.md` before starting.

---

## M0 — Scaffold the monorepo

```
Read CLAUDE.md and docs/SPEC.md. Scaffold the monorepo exactly as the layout in CLAUDE.md:
- backend/PingWatch.sln with Domain, Application, Infrastructure, Api, Worker projects (.NET 9) and the two test projects, with correct project references.
- web/ with Vite + React + TypeScript (strict), Tailwind, shadcn/ui initialised (neutral theme, dark mode via class), React Router, TanStack Query, React Hook Form, Zod, Recharts.
- docker-compose.yml with postgres:16 and mailpit for dev.
- .gitignore, .editorconfig, README with run instructions.
- GitHub Actions CI: dotnet build + test, web lint + typecheck + build.
Verify: dotnet build, dotnet test, npm run build all succeed.
```

## M1 — Domain + database

```
Implement the data model from SPEC section 5 in PingWatch.Domain and an EF Core DbContext in Infrastructure:
accounts, users (Identity), monitors, check_results, daily_stats, incidents, alert_channels, monitor_channels, status_pages, status_page_monitors, alert_log.
- snake_case naming, enums stored as strings, indexes on monitors.next_check_at and check_results(monitor_id, checked_at).
- Global query filter on AccountId for tenant-owned entities.
- Plan definitions (Free/Starter/Pro limits from SPEC section 3) as a static class in Domain.
- Initial migration; apply it against the docker postgres.
Add unit tests for plan limit rules.
```

## M2 — Auth + accounts

```
Implement auth per SPEC section 8:
POST /api/v1/auth/register, login, refresh, forgot-password, reset-password, plus email verification.
- ASP.NET Core Identity, JWT access token (15 min), refresh token rotation in HTTP-only secure cookie.
- Register creates Account (Free plan) + owner user.
- Login rate limit: 5/min per IP using built-in rate limiting middleware.
- IEmailSender with Mailpit SMTP implementation for dev.
- GET /api/v1/account and /account/usage.
Integration tests with WebApplicationFactory + Testcontainers for register → verify → login → refresh.
```

## M3 — Monitors API

```
Implement monitor CRUD per SPEC section 8: list, create, get, update, delete, pause, resume, results (paged), stats (24h/7d/30d/90d).
- FluentValidation: URL format, interval >= plan minimum, timeout 1–30 s, type-specific required fields.
- Enforce plan monitor count on create.
- Stats endpoint: uptime % and response-time series; use daily_stats for 30d/90d, raw results for 24h/7d.
- Incidents list endpoint.
Integration tests for plan limits and tenant isolation (account A cannot read account B's monitor).
```

## M4 — Check engine (worker) - done up to this

```
Implement PingWatch.Worker per SPEC section 6:
- SchedulerService: every 5 s, select due monitors with FOR UPDATE SKIP LOCKED (raw SQL via EF), set next_check_at, enqueue into Channel<CheckJob>.
- N consumers (config, default 100) run checks via ICheckRunner implementations: Http, Keyword, Tcp, Ssl.
- SsrfGuard: resolve DNS, block private/loopback/link-local/metadata ranges, re-check on each redirect (handle redirects manually, max 5).
- Failure confirmation: first failure → schedule retry in 20 s; second → down + open incident; pass while down → up + close incident with duration.
- Write check_results. Emit domain events MonitorWentDown / MonitorRecovered / SslExpiring.
- Nightly RollupService: aggregate into daily_stats, drop raw rows past plan retention.
- Health endpoint and a heartbeat log if a scheduler cycle is missed for 60 s.
Unit tests: state transitions table from SPEC, SsrfGuard ranges, keyword match, SSL days calculation.
```

## M5 — Alerting

```
Implement alerting per SPEC section 7:
- Alert channels CRUD + POST /channels/{id}/test. Encrypt config with Data Protection.
- AlertDispatcher in worker consumes domain events: down, recovered, hourly reminder while down, SSL warnings at 30/14/7/1 days (once each).
- Senders: Email, Webhook (JSON payload from SPEC, HMAC-SHA256 signature header X-PingWatch-Signature), Slack, Discord.
- Retries 3x with backoff 30 s / 2 min / 10 min; log every attempt to alert_log.
Unit tests for payload shape, signature, reminder timing and SSL de-duplication.
```

## M6 — Frontend: auth + dashboard + monitors

```
Build the web app per SPEC section 9 using shadcn/ui:
- Layout: sidebar (Dashboard, Incidents, Channels, Status pages, Billing, Settings), top bar with user menu, dark mode toggle.
- Auth pages: signup, login, verify email, forgot/reset password. Typed API client with automatic token refresh.
- Dashboard: monitor table (status badge, name, 24h uptime, last response, sparkline), status filter, 30 s polling.
- Monitor detail: uptime cards (24h/7d/30d/90d), response-time chart (Recharts), incidents, recent checks.
- New/edit monitor form with type-specific fields (RHF + Zod), channel multi-select.
- Onboarding wizard: first monitor from URL only.
- Plan-limit errors show an upgrade prompt.
npm run lint, typecheck and build must pass.
```

## M7 — Status pages + incidents + channels UI

```
- Status pages API (CRUD) and public endpoint GET /api/v1/public/status/{slug} with 30 s output caching.
- Public page /s/:slug: overall banner, per-monitor 90-day uptime bars, recent incidents. No auth, fast, SEO meta tags.
- Status page editor: slug, title, logo, monitor picker with ordering.
- Incidents page and Alert channels page (add, test, delete).
```

## M8 — Billing

```
Implement billing per SPEC section 3 with Lemon Squeezy behind IBillingProvider:
- POST /billing/checkout returns checkout URL for Starter/Pro, monthly/yearly.
- POST /billing/webhook: verify signature, idempotent by event id, handle subscription created/updated/cancelled/expired → update account plan.
- Downgrade pauses monitors over the new limit (oldest kept active).
- Billing page in web: current plan, usage vs limits, upgrade buttons, manage subscription link.
Integration tests for webhook handling and downgrade.
```

## M9 — Production readiness

```
- Production Dockerfiles (multi-stage) for api, worker, web; docker-compose.prod.yml with Caddy (auto HTTPS), postgres with volume.
- Serilog structured logging, /health/live and /health/ready.
- Security headers, CORS to app domain, HSTS.
- Daily pg_dump backup script to S3-compatible storage.
- CI deploy job: build + push images to GHCR, SSH deploy with docker compose pull && up -d.
- Landing page with pricing table; privacy, terms and refund pages (placeholder text marked TODO).
Run the full test suite and a manual smoke test checklist; report results.
```
