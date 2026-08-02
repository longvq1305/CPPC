# API Integration Notes

Verified against official documentation on 2026-08-02.

## OpenAI

- New conversational work uses the Responses API at `POST /v1/responses`.
- Streaming uses server-sent events; the client accumulates text deltas and handles
  completion, error, function-call arguments, and unknown future event types safely.
- `update_statement` uses function calling with a strict JSON Schema. Structured code
  generation uses strict structured output and rejects malformed payloads.
- Available model IDs come from `GET /v1/models`; relevance/capability filtering is
  configuration-driven and the selected model is never silently replaced.
- Local normalized history remains authoritative so a conversation can continue on
  another provider.

Official sources:

- https://developers.openai.com/api/docs/guides/streaming-responses
- https://developers.openai.com/api/docs/guides/function-calling
- https://developers.openai.com/api/docs/guides/structured-outputs
- https://developers.openai.com/api/reference/resources/models/methods/list

## Gemini

- The Interactions API is generally available as of June 2026 and is the recommended
  API for new projects. The original `generateContent` API is documented as legacy
  but supported.
- REST interactions use `POST /v1beta/interactions` with `x-goog-api-key`. The app
  uses `store=false` because local provider-neutral history is the source of truth.
- Streaming is SSE with interaction and step lifecycle events. Function-call argument
  deltas are accumulated until the step finishes; unknown events are ignored safely.
- Model discovery uses the official models endpoint and preserves manual model IDs.

Official sources:

- https://ai.google.dev/gemini-api/docs/interactions-overview
- https://ai.google.dev/gemini-api/docs/streaming
- https://ai.google.dev/api/interactions-api
- https://ai.google.dev/api/models

## Codeforces Polygon

- Base URL: `https://polygon.codeforces.com/api/{methodName}`.
- Every signed request includes `apiKey`, Unix-seconds `time`, and `apiSig`.
  `apiSig` is a six-character random prefix followed by the lowercase SHA-512 digest
  of `<rand>/<method>?<parameters>#<secret>`. Parameters include `apiKey`/`time`,
  exclude `apiSig`, and sort lexicographically by name then value.
- `problems.list(name=...)` is the read-only duplicate check; `problem.create(name)`
  is called only from the explicit sync workflow.
- Current general-info limits are input/output names 1–64 UTF-8 characters, time
  250–15,000 ms divisible by 50, and memory 4–1,024 MB. Input and output names cannot
  be equal ignoring case.
- Sync uses the documented `problem.updateInfo`, `problem.saveStatement`,
  `problem.saveSolution`, `problem.saveFile`, `problem.setChecker`,
  `problem.saveScript`, `problem.enablePoints`, and `problem.saveTest` fields.
- `problem.saveTest` provides `testPoints`, `testUseInStatements`,
  `testInputForStatements`, `testOutputForStatements`, and
  `verifyInputOutputForStatements`; it is the supported sample/points mapping.
- Before commit, call `problem.renderStatements(includeContent=true)` and
  `problem.cautions`. An empty commit message is omitted from
  `problem.commitChanges`. Package creation uses
  `problem.buildPackage(full=false, verify=true)`, and status is polled through
  `problem.packages`; no package download method is called.

Official sources:

- https://github.com/Codeforces/polygon-misc/blob/main/API.md
- https://polygon.codeforces.com/docs/statements-tex-manual

## Microsoft platform

- Target framework is `net10.0`; the UI is a Blazor Web App with global Interactive
  Server rendering and EF Core 10 SQLite persistence.
- Secrets use Windows `ProtectedData.Protect`/`Unprotect` with
  `DataProtectionScope.CurrentUser` as explicitly required by the product. This is a
  single-user loopback desktop-hosted app, not a multi-node public web deployment.

Official sources:

- https://learn.microsoft.com/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0
- https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew
- https://learn.microsoft.com/dotnet/standard/security/how-to-use-data-protection
