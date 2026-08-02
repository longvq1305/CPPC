# Implementation Notes

## 2026-08-02

- The latest user prompt is newer than the product specification and requires .NET
  10 explicitly. The installed .NET 10.0.203 SDK is used.
- The starting directory was not a Git repository. Git metadata is initialized so
  the required phase commits can be created; the three supplied requirement files
  are preserved unchanged.
- The official OpenAI documentation MCP connector was unavailable and its mandated
  installation attempt failed with an environment-level `Access is denied` error.
  Official `developers.openai.com` pages are used as the documented fallback.
- Source-of-truth files display mojibake in the current PowerShell output but their
  bytes are left untouched. New source and documentation files use UTF-8.
- Large compiler binaries are intentionally acquired by a pinned release script and
  excluded from Git. The final publish verification must fail clearly if required
  toolchain assets are absent; it must not fabricate success.
- Live external calls are never part of the automated suite. After the user supplied
  credentials through Settings, read-only connection/model checks and provider
  generation smoke tests were run through the application UI; remote Polygon writes
  still require the product's explicit sync confirmation.
- `SQLitePCLRaw.bundle_e_sqlite3` is pinned to 2.1.12 instead of accepting EF
  Core's older transitive version, avoiding the published vulnerability affecting
  2.1.11.
- A browser smoke test exposed submit-time stale input in the create-project form.
  The field now binds on `oninput`; create, navigate, and delete were re-run against
  the real Interactive Server application successfully.
- The original launch profile advertised port 5016 while Kestrel listened on 5187,
  and terminal `dotnet run` could not be trusted to apply IDE browser-launch behavior.
  Startup now checks the configured loopback health endpoint and opens the default
  browser itself; automated hosts explicitly disable this side effect.
- Phase 2 performed one live, read-only Polygon credential test and one exact-name
  `problems.list` availability check through the UI. Both succeeded. No Polygon
  problem was created or modified, and no credential value was printed or logged.
- The Gemini Interactions beta endpoint returned HTTP 404 in the credentialed smoke
  test. Current official documentation marks `/v1/interactions` stable, so the client
  uses that endpoint; `gemini-3.6-flash` then streamed successfully. Older
  `gemini-2.5-flash` attempts returned 404 and are retained as honest failed chat
  records only in the temporary acceptance project.
- OpenAI credential/model discovery succeeded, but the live Responses request was
  rejected for insufficient quota. No successful OpenAI generation is claimed;
  mock SSE coverage verifies request construction and stream parsing.
- Phase 4 vendors pinned Monaco 0.56.0 and MathJax 4.1.3 browser assets so the editor
  and statement preview work without a CDN. User-authored statement text is encoded
  before the small supported LaTeX formatting subset is rendered.
- A live Gemini structured-output request updated only the allowed statement fields
  and its diff/Undo flow was verified in the browser. A preceding streamed request
  ended with a network error; cancellation could briefly reload the still-Streaming
  row before the producer persisted its final status. The reader now awaits producer
  cleanup before returning, with a cancellation regression test.
- The source content stored in SQLite is authoritative; `projects/<id>/code` is an
  atomic disk mirror used by compilation and recovery/export. This intentionally
  chooses one consistent source of truth while retaining both storage forms required
  by the specification.
- The pinned WinLibs GCC 16.1.0 / MinGW-w64 14.0.0 UCRT archive and pinned testlib
  sources were acquired and checksum-verified. A real UI smoke run compiled both
  GNU++17 artifacts, ran `generate.exe 1`, and confirmed sample input `1 2` produces
  output `3`. The temporary acceptance project was deleted afterwards; the user's
  project was not modified.
- The final live code-generation retries received honest provider limits: Gemini
  reported its free-tier request quota and OpenAI returned HTTP 429. No code was
  persisted as AI-generated and no success is claimed for those limited requests;
  structured-output parsing and workflow behavior remain covered by mock HTTP and
  service tests.
- Polygon write methods, resume journaling, rendering/caution gates, commit, and
  verified standard-package polling are covered with mock clients and persistence
  tests. No real Polygon problem was created or changed because the user did not
  perform the explicit sync confirmation. Package resume stores the pre-build package
  ID so an older READY package cannot be mistaken for the new build.
- Daily local logging keeps 14 days, bounds entries, and redacts recognizable bearer,
  OpenAI, Google, Polygon key/secret, and signature shapes. Diagnostics stores only
  bounded non-secret connection results; Polygon server offset remains “unknown”
  because the current official Polygon API does not expose server time on success.
