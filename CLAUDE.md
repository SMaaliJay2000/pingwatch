# PingWatch — Uptime Monitor SaaS

Micro-SaaS that checks websites/APIs/ports on an interval, alerts on downtime, and hosts public status pages.
Full spec: see `docs/SPEC.md` (export of the PingWatch design doc). Follow it; ask before deviating.

## Tech stack (fixed — do not substitute)
- **Backend API:** ASP.NET Core 9 Web API, Clean Architecture
- **Check worker:** .NET 9 Worker Service (`BackgroundService` + `System.Threading.Channels`)
- **ORM / DB:** EF Core 9 + Npgsql, PostgreSQL 16
- **Auth:** ASP.NET Core Identity + JWT access token (15 min) + HTTP-only refresh cookie
- **Frontend:** React + Vite + TypeScript, Tailwind CSS, shadcn/ui, TanStack Query, React Router, React Hook Form + Zod, Recharts
- **Infra:** Docker Compose (postgres, api, worker, caddy), GitHub Actions CI
- **Email:** abstraction `IEmailSender`; dev = Mailpit container, prod = Resend/Postmark
- **Billing:** abstraction `IBillingProvider`; Lemon Squeezy first

## Monorepo layout
```
/pingwatch
├── CLAUDE.md
├── docker-compose.yml          # postgres, mailpit, api, worker (dev)
├── .github/workflows/ci.yml
├── docs/SPEC.md
├── backend/
│   ├── PingWatch.sln
│   ├── src/
│   │   ├── PingWatch.Domain/          # entities, enums, domain rules — no dependencies
│   │   ├── PingWatch.Application/     # use cases, DTOs, interfaces, validators (FluentValidation)
│   │   ├── PingWatch.Infrastructure/  # EF Core DbContext, migrations, email, billing, check runners
│   │   ├── PingWatch.Api/             # controllers, auth, DI, OpenAPI
│   │   └── PingWatch.Worker/          # scheduler, check consumers, alert dispatcher, rollup job
│   └── tests/
│       ├── PingWatch.UnitTests/       # xUnit + FluentAssertions + NSubstitute
│       └── PingWatch.IntegrationTests/# WebApplicationFactory + Testcontainers (PostgreSQL)
└── web/                               # Vite React app
    └── src/
        ├── components/ui/             # shadcn/ui generated components
        ├── features/<feature>/        # auth, monitors, incidents, channels, status-pages, billing
        ├── lib/                       # api client, auth, utils
        ├── routes/
        └── main.tsx
```

## Commands
- Start infra: `docker compose up -d postgres mailpit`
- Backend: `cd backend && dotnet build && dotnet test`
- Run API: `dotnet run --project backend/src/PingWatch.Api`
- Run worker: `dotnet run --project backend/src/PingWatch.Worker`
- Add migration: `dotnet ef migrations add <Name> -p backend/src/PingWatch.Infrastructure -s backend/src/PingWatch.Api`
- Frontend: `cd web && npm install && npm run dev` · lint `npm run lint` · typecheck `npm run typecheck` · build `npm run build`
- Add shadcn component: `cd web && npx shadcn@latest add <component>`

## Conventions
### Backend
- Dependency direction: Api/Worker → Infrastructure → Application → Domain. Domain references nothing.
- Controllers stay thin; logic lives in Application services.
- All routes under `/api/v1`. Errors = RFC 7807 ProblemDetails.
- Every tenant-owned entity has `AccountId`; enforce with an EF Core global query filter. Never query across accounts.
- Use `IHttpClientFactory` for all outbound HTTP; never `new HttpClient()`.
- Async all the way; pass `CancellationToken` everywhere.
- Config via `appsettings.json` + environment variables. No secrets in git.
- Encrypt alert channel secrets (`config_json`) with ASP.NET Core Data Protection.
- Table names snake_case (use `EFCore.NamingConventions`).

### Check engine rules (from spec — must hold)
- Scheduler polls every 5 s: `next_check_at <= now AND status <> 'paused'`, `FOR UPDATE SKIP LOCKED`, sets next run immediately.
- First failure → retry after 20 s; second consecutive failure → status `down`, open incident, alert.
- Recovery → close incident, record duration, recovery alert. Reminders hourly while down.
- **SSRF guard:** resolve DNS before each check and after redirects; block 10/8, 172.16/12, 192.168/16, 127/8, 169.254/16, ::1, fc00::/7. Unit-test this.
- Max 5 redirects, read at most 1 MB of body for keyword checks.

### Frontend
- TypeScript strict mode. No `any`.
- Server state via TanStack Query only; no Redux.
- Forms: React Hook Form + Zod schemas colocated in the feature folder.
- UI from shadcn/ui components; Tailwind for layout. Support dark mode.
- API base URL from `VITE_API_URL`.

## Workflow for Claude Code
- Work one milestone at a time (see `docs/PROMPTS.md`). Don't start the next until the current one builds and tests pass.
- After each change: build, run tests, fix failures before reporting done.
- Write tests for domain rules, the check engine, SSRF guard and plan limits.
- Keep commits small with conventional messages (`feat:`, `fix:`, `chore:`).
- Ask before adding a dependency not listed above.
