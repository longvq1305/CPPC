# Architecture

## Layers and dependencies

Dependencies point toward the domain and application contracts:

```text
Web ───────────────► Application ◄──────────── Infrastructure
 │                        │                         │
 └────────────────────────┼─────────────────────────┘
                          ▼
                        Domain

Integrations ─────────► Application + Contracts + Domain
```

- **Domain** owns the aggregate and workflow invariants without infrastructure
  dependencies.
- **Application** defines use cases, DTOs, validation, and persistence/secret/client
  interfaces.
- **Infrastructure** implements EF Core SQLite repositories, migrations, runtime
  paths, and DPAPI storage.
- **Integrations** is the boundary for typed OpenAI, Gemini, and Polygon clients.
- **Web** composes the application and renders the loopback-only Blazor Interactive
  Server UI.
- **Contracts** owns cross-boundary contracts independently of UI.

## Data flow

The Blazor component calls an application service. The service enforces use-case
rules and talks only to interfaces. EF repositories create short-lived contexts from
`IDbContextFactory`, while the secret service writes through an independent encrypted
store. UI models expose secret presence, never decrypted values.

Each `ProblemProject` is the root for exactly one general-info record, statement,
conversation, and test configuration. Version/history rows and sync-operation rows
are append-oriented. SQLite foreign keys and unique indexes reinforce these aggregate
rules. `DateTimeOffset` values are stored as Unix epoch milliseconds so SQLite can
order them consistently.

## Long-running flows

Provider streaming normalizes both providers into one persisted conversation;
switching providers does not fork that history. Local execution uses controlled
temporary directories, `ProcessStartInfo.ArgumentList`, output caps, timeouts, and
process-tree cancellation, never a composed shell command. Polygon synchronization
is an explicit, durable state machine whose successful steps resume without repeating
earlier remote mutations. The pre-build Polygon package ID is journaled so resume
cannot report an older READY package as the new build.

SQLite is authoritative for editable source and immutable version history. Atomic
files under `projects/<id>/code` mirror the current source for compilation and
recovery/export. Daily bounded logs live under `logs/`; connection diagnostics store
only non-secret status metadata in application settings.

The exact phases and invalidation rules are tracked in
[`IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md).
