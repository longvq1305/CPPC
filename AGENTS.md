# AGENTS.md — Polygon AI Problem Builder

## Source of truth

- Read `POLYGON_AI_BUILDER_SPEC.md` before making changes.
- The specification controls scope and behavior.
- Record unavoidable deviations in `IMPLEMENTATION_NOTES.md`.

## Working agreements

- Target .NET 10 and Windows x64.
- Use ASP.NET Core Blazor Web App with Interactive Server rendering, EF Core 10, and SQLite.
- Preserve a layered architecture: Domain, Application, Infrastructure, Integrations, UI.
- Keep OpenAI, Gemini, and Polygon behind interfaces and typed clients.
- Never log or commit API keys, API secrets, signatures, or decrypted secret values.
- Use async APIs and cancellation tokens for I/O and long-running work.
- Do not use shell-concatenated process commands.
- Run relevant tests after every meaningful change.
- Before declaring completion, run Release build and the full test suite.

## Product guardrails

- Do not add a validator.
- Do not add brute-force or wrong solutions.
- Do not run the full 100 tests locally.
- Do not support editing arbitrary existing Polygon problems.
- Do not download Polygon packages.
- Do not auto-sync to Polygon; sync only after the explicit user action.
- Preserve one conversation per local problem, even when switching AI providers.
- Statement fields are only title, legend, input, output, and note.
- Samples belong to test configuration, not the statement editor.
- GNU C++17 is fixed for local compile and Polygon sourceType.
- The generator must follow the product-required `test_id` workflow and use `mt19937_64` plus testlib registration.

## External APIs

- Verify current official OpenAI, Gemini, Polygon, and Microsoft documentation before implementing or changing integrations.
- Do not silently substitute obsolete APIs.
- Keep provider model names dynamic; do not scatter model constants across UI code.
- Use mock HTTP servers/handlers for automated integration tests.
- Never report a real external integration as tested without actual evidence.

## Code quality

- Prefer clear, maintainable code over clever abstractions.
- Validate all external input and API responses.
- Use structured errors and actionable UI messages.
- No empty catches, swallowed exceptions, fake success states, or TODO-only production paths.
- Add tests for bugs and non-trivial workflow rules.
