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
- Live OpenAI, Gemini, and Polygon tests remain opt-in and cannot be reported as
  executed until the user supplies credentials through the application Settings UI.
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
