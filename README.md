# QIAD Helper

Discord bot + web panel for QIAD, the Quantum Internal Affairs Department of Quantum Science (QS): appeal intake and
review in Discord forum threads, bot-managed restriction roles with auto-expiry, and a public,
redacted case database that replaces the old Google Sheet.

The full architecture contract lives in `DESIGN-QIAD.md` (in the QDFHelperRewrite repo). Short
version: **one process** — the ASP.NET Core host (`QiadHelper.Web`) runs the Discord bot
in-process as hosted services. No Redis, no bot→web HTTP, no shared write password. PostgreSQL
is the only other container.

## Layout

| Project | Kind | Role |
|---|---|---|
| `QiadHelper.Core` | Class library | Entities, `QiadContext` (+ migrations), shared application services (`CaseService`, `RestrictionService`), in-process notification bus |
| `QiadHelper.Bot` | Class library | Discord interaction modules, `InteractionHandler`, hosted services (`Worker`, notification consumer, restriction expiry, DM evidence listener). Hosted by Web via `AddQiadBot()` |
| `QiadHelper.Web` | ASP.NET Core | The host: public case DB + admin UI + small API, Discord OAuth + rank auth, API keys |

## Run it

```bash
# Everything in Docker (app + PostgreSQL):
cp .env.example .env   # fill in POSTGRES_PASSWORD + Discord credentials
docker compose up -d
# → app on :8080, health at /healthz. A fresh boot migrates + seeds itself.
```

```bash
# Local dev (F5 / dotnet run — the app runs natively, only the database is containerized):
docker compose -f docker-compose.dev.yml up -d   # dev PostgreSQL on localhost:5432 — once;
                                                 # restart:unless-stopped keeps it across reboots
dotnet run --project QiadHelper.Web              # http://localhost:5199 — migrates + seeds itself
# appsettings.Development.json already points at the dev database. The bot stays offline
# until you add Discord secrets (user-secrets on QiadHelper.Web override the dev defaults):
#   dotnet user-secrets set Discord:BotToken "..."
#   dotnet user-secrets set Discord:ClientId "..." ; dotnet user-secrets set Discord:ClientSecret "..."
#   dotnet user-secrets set Bot:MainGuildId "..." ; dotnet user-secrets set Bot:DeveloperUserIds:0 "<your id>"
```

## Database

```bash
dotnet ef migrations add <Name> --project QiadHelper.Core --startup-project QiadHelper.Web
dotnet ef database update --project QiadHelper.Core --startup-project QiadHelper.Web
```

Migrations also run automatically at app startup, before Kestrel serves traffic.

## First-run bootstrap

1. Set `DEVELOPER_USER_ID` (or `Bot:DeveloperUserIds`) to your Discord ID — that account acts
   as Director before any rank roles are mapped.
2. Sign into the panel → **Admin → Settings**: set `MainGuildId`, the default forum channel,
   the `#case-updates` channel, and map Discord roles to QIAD ranks (PA…Director).
3. **Admin → Case categories**: set each category's submission channel, then "Re-sync intake
   panels" — the bot posts the button panels.
4. Whitelist restriction roles under **Admin → Settings** so `/restrict` and case execution work.

## Conventions

Same as QDF: central package management (`Directory.Packages.props`), file-scoped namespaces,
`Async` suffix, `_camelCase` fields, all client JS in `ClientScripts/**/*.ts` (never
logic-bearing inline `<script>` — `npm run check:views` enforces this and runs in the image
build).
