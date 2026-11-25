# PingWatch

Uptime monitor SaaS. Spec: [docs/SPEC.md](docs/SPEC.md). Conventions: [CLAUDE.md](CLAUDE.md).

## Prerequisites

- .NET SDK 9 or later, plus the .NET 9 runtime
- Node.js 22.13+
- Docker Desktop

## Run locally

Start Postgres and Mailpit (Mailpit UI: http://localhost:8025):

```
docker compose up -d postgres mailpit
```

API (http://localhost:5080, OpenAPI at `/openapi/v1.json`):

```
dotnet run --project backend/src/PingWatch.Api
```

Worker:

```
dotnet run --project backend/src/PingWatch.Worker
```

Web (http://localhost:5173). Copy `web/.env.example` to `web/.env` first.

```
cd web
npm install
npm run dev
```

## Checks

```
dotnet build backend/PingWatch.sln
dotnet test backend/PingWatch.sln
```

In `web/`: `npm run lint`, `npm run typecheck`, `npm run build`.
