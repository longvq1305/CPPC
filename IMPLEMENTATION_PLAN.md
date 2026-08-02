# Polygon AI Builder — Implementation Plan

Last updated: 2026-08-02

## Repository status

The repository initially contains only `AGENTS.md`, `CODEX_BUILD_PROMPT.md`, and
`POLYGON_AI_BUILDER_SPEC.md`. There is no existing solution, source code, database,
test project, generated data, or Git metadata to preserve. The installed SDK is
.NET 10.0.203 on Windows x64.

## Architecture

The application uses a layered .NET 10 architecture with dependencies pointing
inward:

```text
PolygonAiBuilder.Web
  -> PolygonAiBuilder.Application
  -> PolygonAiBuilder.Infrastructure
  -> PolygonAiBuilder.Integrations

PolygonAiBuilder.Infrastructure -> Application, Domain
PolygonAiBuilder.Integrations   -> Application, Domain, Contracts
PolygonAiBuilder.Application    -> Domain, Contracts
PolygonAiBuilder.Contracts      -> Domain-free transport contracts
PolygonAiBuilder.Domain         -> no project dependencies
```

- `Domain` owns entities, enums, value objects, and invariant-preserving methods.
- `Application` owns use cases, provider/Polygon/persistence interfaces, workflow
  orchestration, validation, and DTOs.
- `Infrastructure` owns EF Core SQLite, migrations, atomic project-file storage,
  DPAPI secrets, logging, attachments, and safe local process execution.
- `Integrations` owns typed OpenAI, Gemini, and Polygon HTTP clients.
- `Web` is the loopback-only Blazor Web App using Interactive Server rendering.
- `Contracts` owns validated JSON schemas for AI structured actions and outputs.

External writes are initiated only by explicit application use cases. AI adapters
cannot invoke Polygon. Source code and attachments remain within controlled project
directories and are never exposed as static files.

## Project structure

```text
PolygonAiBuilder.slnx
src/
  PolygonAiBuilder.Domain/
  PolygonAiBuilder.Contracts/
  PolygonAiBuilder.Application/
  PolygonAiBuilder.Infrastructure/
  PolygonAiBuilder.Integrations/
  PolygonAiBuilder.Web/
tests/
  PolygonAiBuilder.UnitTests/
  PolygonAiBuilder.IntegrationTests/
  PolygonAiBuilder.E2ETests/
scripts/
  acquire-toolchain.ps1
  verify-toolchain.ps1
  publish-win-x64.ps1
toolchain/
  mingw64/
  testlib/
  checkers/
data/                 # ignored runtime data except keep files
projects/             # ignored runtime projects except keep files
logs/                 # ignored runtime logs except keep files
docs/
```

## Database entities

- `ProblemProject`: workflow state, selected provider/model, Polygon identity,
  revision, current screen, timestamps, and name-availability timestamp.
- `GeneralInfo`: input/output filenames and time/memory limits.
- `Statement`: current five-field English statement and stale markers.
- `StatementVersion`: immutable snapshots with user/AI attribution and message link.
- `Conversation`: exactly one row per project, with rolling summary.
- `ConversationMessage`: normalized role/content, provider/model, stream state,
  response identity, structured actions, errors, and parent link.
- `Attachment`: safe disk metadata, hash, MIME, extracted text, and provider file ID.
- `CodeArtifact` and `CodeArtifactVersion`: solution/generator source, generation
  provenance, compile result, staleness, and immutable history.
- `TestConfiguration`: checker, script, points, test count, sample settings, commit
  message, and audit state.
- `Sample`: test 1 input/output, source-version provenance, timestamps, and stale flags.
- `SyncOperationLog`: durable Polygon phase journal with fingerprints and safe errors.
- `ApplicationSetting` and `ModelCacheEntry`: non-secret preferences and model cache.

Relationships and uniqueness constraints enforce one general-info row, statement,
conversation, and test configuration per project. SQLite migrations run at startup
through `IDbContextFactory`.

## Implementation phases

1. **Foundation — complete (2026-08-02)**
   - Solution, layering, domain model, SQLite migration, repositories.
   - Dashboard, project creation/deletion, wizard shell, current-screen persistence.
   - Settings, DPAPI CurrentUser encrypted secret file, non-secret settings.
2. **General Info and Polygon read-only — complete (2026-08-02)**
   - Exact five fields, current official validation, autosave, connection test,
     signed `problems.list`, duplicate-name gate without remote creation.
3. **AI workspace — complete (2026-08-02)**
   - Provider abstraction, real OpenAI Responses and Gemini Interactions clients,
     model discovery/cache, SSE streaming, normalized persistent conversation,
     attachment validation/storage, provider switching confirmation.
4. **Statement workflow — complete (2026-08-02)**
   - `update_statement` structured tool, defensive merge, immutable versions,
     diff/undo/restore, five-field editor, Monaco and MathJax preview, LaTeX lint.
5. **Code and local toolchain**
   - Structured code generation/versioning, Monaco tabs, pinned compiler/testlib/
     checker acquisition, verification, safe compile runner, diagnostics, three-pass
     AI repair limit.
6. **Tests and sample**
   - Run only test 1 locally, sample provenance/staleness, checker/script/points UI,
     honest self-audit and readiness checks.
7. **Polygon synchronization**
   - Explicit-only sync state machine, recheck/create/persist ID, idempotent uploads,
     render/cautions/commit, standard verified package build, polling and resume.
8. **Hardening and release**
   - Unit/integration/Playwright coverage, accessibility/security review, rolling
     diagnostics, self-contained win-x64 packaging, documentation, final report.

Phase 1 verification: the application starts on loopback, dashboard project
create/open/delete works in the in-app browser, Settings renders without exposing
stored values, the SQLite migration applies on startup, and the foundation unit,
integration, and host tests pass. Phases 2–8 remain planned and are not represented
as production-complete by the foundation UI.

Phase 2 verification: General Info autosaves valid values, blocks invalid values,
requires an exact signed `problems.list` duplicate-name check before step 2, and
never calls `problem.create`. Polygon signing and response handling have unit/mock
HTTP coverage. The saved real Polygon credential was tested successfully through the
Settings UI, and a temporary local project passed the read-only availability flow;
the temporary project was removed afterward.

Phase 3 verification: Settings discovered 67 OpenAI and 25 Gemini chat-capable
models through the saved credentials. Both provider model-list connection tests
succeeded. Gemini `v1/interactions` produced and persisted a real streamed response
with `gemini-3.6-flash`, including a confirmed provider switch in the same local
conversation. OpenAI Responses reached the authenticated API but the account
returned `insufficient_quota`; the UI persisted the response as `Failed` without
losing the user message. OpenAI streaming behavior remains covered by mock SSE tests
and is not represented as a successful live generation.

Phase 4 verification: the five-field editor autosaved through local Monaco, MathJax
rendered inline formulas and supported text markup, immutable versions were visible
and restorable, and an empty statement was blocked from advancing to Code with an
actionable field list. A credentialed Gemini `gemini-3.6-flash` structured request
updated exactly Legend/Input/Output, displayed a three-field diff, and Undo restored
the prior content as a new version. One separate streamed Gemini request failed with
a network error; the honest failed record was confined to a temporary project that
was deleted. That run exposed and fixed a cancellation/final-status race with a unit
regression test. Both temporary acceptance projects were removed, no Polygon problem
was created, and the user's existing project was returned to its original screen.

Each phase ends with formatting, a Release build, relevant tests, a plan status
update, and a separate commit. Full Release build and the complete test suite are
required before declaring the product complete.

## External integrations

- **OpenAI:** typed REST client using the Responses API, SSE streaming, function
  calling/strict structured output, file/image inputs, and dynamic `GET /v1/models`.
- **Gemini:** typed REST client using the GA Interactions API with `store=false`, SSE
  streaming, function calls/structured output, multimodal inputs, and model listing.
- **Polygon:** typed client for `https://polygon.codeforces.com/api/{methodName}`;
  SHA-512 signatures over lexicographically sorted parameters, multipart uploads,
  structured failure parsing, explicit sync, and durable resume state.
- **GNU C++17:** pinned MinGW-w64 distribution plus pinned testlib/checker sources;
  checksum/license verification and `ProcessStartInfo.ArgumentList` execution.

Verified assumptions and source URLs live in `docs/API_INTEGRATION_NOTES.md`.

## Testing strategy

- **Unit:** domain validation, statement merge/version/undo, LaTeX lint, model filter,
  request normalization, signature canonicalization, workflow transitions, stale
  propagation, filename/archive safety, process limits, redaction, and script output.
- **Integration:** SQLite migration/repositories, DPAPI round trip, atomic storage,
  mock HTTP servers for both AI providers and Polygon, SSE parsing, omitted optional
  commit message, standard build parameters, and failure/resume at every sync phase.
- **E2E:** Playwright covers Settings, restart persistence, project wizard, fake AI
  stream/tool update/undo, code/sample flow, self-audit, fake Polygon sync and resume.
- **Manual/packaging:** start the published app loopback-only, verify database
  recovery, compile C++17 solution and testlib generator, produce Sample 1, and run a
  credentialed Polygon acceptance test without claiming it passed unless evidenced.

## Risks and mitigations

- **External API drift:** keep wire contracts inside typed clients, validate unknown
  events defensively, use dynamic model IDs, mock protocol fixtures, and keep official
  assumptions documented.
- **Polygon is non-transactional:** persist the problem ID immediately, journal every
  successful phase, use idempotent filenames/language/test indices, and invalidate
  downstream phases when local inputs change.
- **Secret exposure:** DPAPI CurrentUser per value, atomic restricted file writes,
  masked UI, centralized redaction, and no secrets in SQLite/browser/logs.
- **Executing generated code:** explicit first-run warning, isolated temporary
  directory, no shell, controlled arguments/environment, time/output limits, tree
  kill, cleanup, and honest non-sandbox warning.
- **Bundled toolchain size/licensing:** pin a reputable distribution and checksums,
  acquire outside Git, include notices in publish output, and smoke-test assets.
- **Long-running server work:** cancellation tokens, bounded background operations,
  progress persisted independently of the Blazor circuit, and retry only transient
  idempotent calls.
- **Editor/runtime asset availability:** pin local Monaco/MathJax assets for the
  distributable and verify offline operation during release packaging.

## Definition of Done tracking

Completion means every acceptance item in the prompt/spec is implemented, the
self-contained app starts, all automated tests pass in Release, the bundled compiler
smoke tests pass, no plaintext secret is tracked, and any credential-dependent live
tests are reported accurately as passed or outstanding with evidence.
