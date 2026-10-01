# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

QIAD Helper is a Discord bot + ASP.NET Core web panel for QIAD, the Quantum Internal Affairs
Department: the appeals and investigations department of Quantum Science (QS, a Roblox group).
It was built from the design contract `DESIGN-QIAD.md` (kept in the sibling
`D:\Projekte\QDFHelperRewrite` repo, which is also the donor codebase much of the
infrastructure was copied from).

**Topology (the one thing to internalize):** a single process. `QiadHelper.Web` is the only
executable; the Discord bot runs inside it as hosted `BackgroundService`s registered by
`AddQiadBot()`. There is no bot→web HTTP, no Redis, no bot API key. Bot modules and web
controllers call the same scoped application services (`ICaseService`, `IRestrictionService`).

| Project | Kind | Role |
|---|---|---|
| `QiadHelper.Core` | classlib | Entities, `QiadContext` + migrations, `CaseService`/`RestrictionService`, `InProcessNotificationBus` |
| `QiadHelper.Bot` | classlib | Interaction modules (`Modules/`), `InteractionHandler`, `QiadBot`, hosted services (`Services/`) |
| `QiadHelper.Web` | ASP.NET Core | Host + public case DB + admin area + API, auth stack (`Security/`) |

## Build & Run

```bash
dotnet build QiadHelper.slnx                       # zero-warning policy: keep it that way
docker compose -f docker-compose.dev.yml up -d     # dev-only postgres on localhost:5432 (once)
dotnet run --project QiadHelper.Web                # native app against the dev db; web-only
                                                   # if no bot token configured. F5 = this.
docker compose up -d                               # full stack (app image + postgres) — deployment

# EF migrations (local dotnet-ef tool, see dotnet-tools.json):
dotnet ef migrations add <Name> --project QiadHelper.Core --startup-project QiadHelper.Web
```

Secrets in dev via `dotnet user-secrets` on **QiadHelper.Web** (the single host):
`ConnectionStrings:Qiad`, `Discord:BotToken`, `Discord:ClientId`, `Discord:ClientSecret`,
`Bot:MainGuildId`, `Bot:DeveloperUserIds:0`. In containers, everything is `Section__Key` env
vars from `.env` (see `.env.example`).

## Architecture Essentials

- **Case state machine** lives in `QiadHelper.Core/Services/Cases/CaseService.cs` (design §5):
  Submitted → (claim mints the case number) UnderReview → AwaitingRatification →
  (upheld) AwaitingExecution → (claimer presses "Execute decision") Closed, plus the void path
  (IA+ ratifies; the APPROVING ratifier writes the appellant-facing DM/public reason) and the
  misc path (no ratification, but closes only via the misc post's Close button, which captures
  the separate public reasoning). Rank gates are enforced INSIDE the service via `StaffActor`.
  Two texts: the reviewer's `DecisionReasoning` (internal) vs `Reasoning` (public, authored by
  the ratifier at uphold / the closer of a misc filing). Executing a REJECTED verdict
  auto-applies the category's `RejectionRestrictionRoleId` for `RejectionRestrictionDays`.
- **Intake is queue-first**: a submission lands as a message in the validation-queue channel
  (`SubmissionQueueChannelId` config key) carrying claim/void/misc buttons. That message is the
  case's queue anchor (`Case.QueueChannelId/QueueMessageId`), edited in place on every
  transition. The forum review thread is created at CLAIM, not at submit; evidence filed while
  queued is backfilled into it. Tracking cases skip the queue (pre-claimed by the filer).
- **Void + misc get their own forums** (like the live system): a void request creates a post in
  the `VoidRequestsForumChannelId` forum where IA+ ratifies (inline queue/thread only as
  fallback); a misc filing creates a documentation post in `MiscFilingsForumChannelId`.
  Ratification outcomes always ping the originator (void requester / case claimer). Staff
  embeds carry required facts only — no process explainers; QIAD staff are trained.
- **Discord side effects** all flow through the in-process notification bus
  (`Core/Notifications`): services publish typed records; the bot's
  `QiadNotificationConsumerService` → `CaseDiscordService` performs threads/DMs/panels/roles.
  Never call Discord from web controllers or from `CaseService`.
- **Staff workflow: buttons OR slash commands**, chosen by `Bot:StaffWorkflowMode`
  (`Buttons` default | `Commands` | `Both`; deploy-time, needs a restart). `WorkflowCommandModule`
  mirrors every button action as a command (`/add-decision`, `/issue-verdict`, `/reject-case`,
  `/claim-case ref:…`, `/execute`, …) by resolving the case from the thread it's run in
  (`ICaseService.GetByThreadAsync` matches review/void/misc thread ids) or the queue `REF-XXXX`
  (`GetByReferenceAsync`), then responding with the SAME `qiad-*-modal` custom IDs the buttons use —
  so `CaseThreadModule`'s modal-submit handlers run unchanged. `InteractionHandler` exempts the
  modal-opening commands from auto-defer (`_modalOpeningSlashCommands`) and registers the workflow
  module only when mode ≠ Buttons. `CaseDiscordService.ShowStaffButtons` gates button rendering.
- **Rank resolution**: staff have no DB table. `DiscordRankResolver` (bot) maps guild roles
  through the `QiadRankRoles` table; the web's `QiadClaimsTransformation` uses the same
  resolver (cached 60 s). `Bot:DeveloperUserIds` act as Director (bootstrap).
- **ulong ↔ bigint**: Npgsql has no native ulong. `QiadContext.ConfigureConventions` bit-casts
  every `ulong` property to `bigint`. Don't add manual conversions.
- **Public data discipline** (design §8.2): anything public goes through `PublicCaseRow` /
  `PublicIncidentRow` projections only — `CaseNumber IS NOT NULL AND Status <> Sealed`,
  reasoning only when terminal. Never hand an entity to a public view or API response.
- **Custom-ID scheme**: `qiad-open:{categoryId}:{kind}`, `qiad-appeal:{categoryId}:{kind}`,
  `qiad-evidence:{caseId}`, per-case actions `qiad-claim/verdict/ratify/void/void-ratify/
  misc/restrict:{caseId}` (multi-step actions add `-pick`/`-modal` variants). Discord hard
  limits: modal titles and input labels max 45 chars — the label is the crash you get wrong.

## Code Conventions

- File-scoped namespaces, `Async` suffix, `_camelCase` private fields, `.editorconfig` enforced.
- Zero-warning build; deliberate suppressions live in `Directory.Build.props` NoWarn with reasons.
- All client logic in `ClientScripts/**/*.ts` compiled to `wwwroot/js` — **never** logic-bearing
  inline `<script>` in `.cshtml` (a data-bootstrap `window.<page>PageData = {...}` is the only
  exception). `npm run check:views` enforces this and runs in the Docker build.
- Central package management: versions ONLY in `Directory.Packages.props`.
- ⚠️ ReSharper hazards inherited from QDF: it strips setters off JSON DTOs and mangles inline
  Razor scripts — don't run automated cleanup over DTOs/views.

## Not yet built (deliberate)

- Evidence intake is DISABLED for now: no attach button in the confirmation DM and
  `EvidenceDmListenerService` is not registered (see `AddQiadBot`); `EvidenceModule` answers
  stale buttons with a disabled notice. All plumbing (entity, `AddEvidenceAsync`, thread
  mirroring, claim-time backfill) remains in place for re-enabling.

- The one-time Google Sheet importer (design §10) — blocked on the CSV export of the three tabs.
  Convention: `#if DEBUG`-gated `DataImport*` controller, never shipped in Release.
- Forum thread state **tags** (the bot renames threads and posts status embeds instead, v1).
- Automated validation assists (duplicate/cooldown checks, design §11.5) — validation is human.
