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
- **Contracts** will own AI structured-action wire contracts independently of UI.

## Foundation data flow

The Blazor component calls an application service. The service enforces use-case
rules and talks only to interfaces. EF repositories create short-lived contexts from
`IDbContextFactory`, while the secret service writes through an independent encrypted
store. UI models expose secret presence, never decrypted values.

Each `ProblemProject` is the root for exactly one general-info record, statement,
conversation, and test configuration. Version/history rows and sync-operation rows
are append-oriented. SQLite foreign keys and unique indexes reinforce these aggregate
rules. `DateTimeOffset` values are stored as Unix epoch milliseconds so SQLite can
order them consistently.

## Planned long-running flows

Provider streaming will normalize both providers into one persisted conversation;
switching providers will not fork that history. Local execution will use controlled
temporary directories and `ProcessStartInfo.ArgumentList`, never a composed shell
command. Polygon synchronization will be an explicit, durable state machine whose
successful steps can resume without repeating earlier remote mutations.

The exact phases and invalidation rules are tracked in
[`IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md).
