# Polygon AI Builder

Polygon AI Builder is a local-first Windows application for creating a new
competitive-programming problem with OpenAI or Gemini, validating the local
workflow with a pinned GNU C++17 toolchain, and explicitly synchronizing the
finished problem to Codeforces Polygon.

Version 1.0 implements the full five-step workflow:

1. General Info and a read-only Polygon name check.
2. One provider-neutral AI conversation per local problem.
3. A versioned five-field English statement with local LaTeX preview.
4. Editable/versioned `solution.cpp` and `generate.cpp`, compile diagnostics, and
   a real `test_id=1` local sample smoke test.
5. Checker/test configuration, deterministic plus AI Self-Audit, resumable explicit
   Polygon sync, statement render/caution checks, commit, and verified standard
   package polling.

The app never auto-syncs. It does not add validators, brute-force/wrong solutions,
run all 100 tests locally, or download Polygon packages.

## Prerequisites

- Windows 11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for source builds
- OpenAI, Gemini, and Polygon credentials entered only through Settings

Acquire the pinned compiler and testlib/checker sources once:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/acquire-toolchain.ps1
powershell -ExecutionPolicy Bypass -File scripts/verify-toolchain.ps1
```

The compiler archive version and SHA-256, testlib revision, and source checksums are
recorded in `toolchain/manifest.json`. Downloaded compiler binaries are intentionally
excluded from Git.

## Run locally

```powershell
dotnet run --project src/PolygonAiBuilder.Web
```

The server listens only on `http://127.0.0.1:5187`. After `/health` succeeds, the
application asks Windows to open that URL in the default browser. If browser launch
is blocked by Windows policy, open the URL manually while the command remains
running.

Runtime files are stored under `data/`, `projects/`, and `logs/`. Credentials are
outside SQLite in `data/secrets.local.json`, encrypted with Windows DPAPI
`CurrentUser`; plaintext keys are not written to logs or application data.

## Build and test

```powershell
dotnet format PolygonAiBuilder.slnx --no-restore
dotnet build PolygonAiBuilder.slnx -c Release
dotnet test PolygonAiBuilder.slnx -c Release --no-build
```

Automated external-integration tests use mock HTTP handlers. A real Polygon write is
not part of the automated suite because sync requires a deliberate user action.

## Publish Windows x64

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-win-x64.ps1
```

The script verifies the pinned toolchain, runs the Release build and full suite,
publishes a self-contained `win-x64` distribution, copies compiler/testlib/checker
licenses and assets, and verifies the compiler again inside
`artifacts/publish/win-x64`.

See [architecture notes](docs/ARCHITECTURE.md),
[security notes](docs/SECURITY.md), [API research](docs/API_INTEGRATION_NOTES.md),
and [implementation notes](IMPLEMENTATION_NOTES.md).
