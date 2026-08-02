# Polygon AI Builder

Polygon AI Builder is a local-first Windows application for designing competitive
programming problems and, in later implementation phases, generating content with
AI, compiling GNU C++17 locally, and explicitly synchronizing a finished problem to
Codeforces Polygon.

The current repository contains the completed **Phase 1 foundation**: the layered
.NET 10 solution, Blazor Interactive Server shell, SQLite persistence and migration,
project dashboard, five-step wizard shell, Settings UI, and per-user DPAPI-encrypted
credential storage. Provider calls, compiler execution, and Polygon writes are not
enabled yet; their controls are deliberately unavailable instead of reporting fake
success.

## Prerequisites

- Windows x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

No API credential is needed for the foundation. OpenAI, Gemini, and Polygon secrets
will be entered through Settings when their integration phases are implemented.

## Run locally

```powershell
dotnet tool restore
dotnet restore
dotnet run --project src/PolygonAiBuilder.Web
```

Open `http://127.0.0.1:5187`. The server is intentionally bound to loopback only.
Runtime files are created under `data/`, `projects/`, `logs/`, and `secrets/`; these
directories are excluded from Git.

## Build and test

```powershell
dotnet format PolygonAiBuilder.slnx --no-restore
dotnet build PolygonAiBuilder.slnx -c Release
dotnet test PolygonAiBuilder.slnx -c Release --no-build
```

The tests cover domain rules, settings behavior, SQLite persistence and migrations,
DPAPI encryption, and a hosted application smoke path. Live external integration
tests will remain opt-in and must skip safely when credentials are absent.

## Foundation workflow

1. Create a local project from the dashboard using its future Polygon internal name.
2. Open the project to see the five-step shell; reopening resumes the saved screen.
3. Use Settings to store credentials. Existing secrets are shown only as masked
   state and are preserved unless explicitly replaced or cleared.
4. Delete a local project from the dashboard when it is no longer needed. This has
   no effect on Polygon.

The General Info fields currently show persisted defaults. Validation, autosave, and
the read-only Polygon duplicate-name check belong to Phase 2.

## Data and credentials

- SQLite stores projects, workflow state, versions, messages, test configuration,
  sync journals, and non-secret preferences.
- Credentials are kept outside SQLite in an atomic encrypted file protected with
  Windows DPAPI `CurrentUser` and a current-user-only ACL.
- The UI never reads a secret back into a normal view model; it receives only a
  configured/not-configured flag and a mask.

See [the implementation plan](IMPLEMENTATION_PLAN.md),
[architecture notes](docs/ARCHITECTURE.md), [security notes](docs/SECURITY.md), and
[API research](docs/API_INTEGRATION_NOTES.md) for the remaining roadmap and design.
