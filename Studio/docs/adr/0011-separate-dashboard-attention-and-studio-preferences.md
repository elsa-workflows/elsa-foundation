# Separate Dashboard, Attention, and Studio Preferences

## Context

Dashboard remains an open-ended Studio composition surface, while Attention is an independent cross-cutting capability that may be presented through a Dashboard Widget. The two concepts are complementary but do not share a contribution contract.

This decision extends the Slot and Contribution model from [ADR 0001](0001-use-slots-and-contributions-for-studio-extensibility.md), follows the domain-dependency direction established by [ADR 0002](0002-keep-expression-specific-support-in-expression-modules.md), and preserves the separation between Dashboard and Diagnostics from [ADR 0004](0004-keep-dashboard-and-diagnostics-as-separate-slots.md).

## Decision

`Elsa.Studio.Dashboard` owns the `/dashboard` surface, Dashboard Slot, host-owned widget frame, managed loading and refresh behavior, responsive layout, and Dashboard preference schema. A module may contribute zero, one, or multiple Dashboard Widgets. Dashboard reserves no screen space or cardinality by module identity. Widget contributions provide metadata, an optional cancellable loader, supported semantic sizes, optional versioned settings, and a body component; Dashboard owns framing, accessibility semantics, loading, empty, error, timeout, retry, refresh, and layout controls.

The current self-framed Dashboard Widget contract is replaced in one clean break. No compatibility adapter is provided. `Elsa.Studio.Samples.Dashboard` is removed because its hard-coded module-load, route-ownership, backend-endpoint, and UI-kit cards demonstrate mechanics rather than user value.

Attention is not a Dashboard subdomain. `Elsa.Attention.Core` owns normalized Attention Items, contributor contracts, correlation primitives, and aggregation policy. `Elsa.Attention.Api` owns the shared query surface. The same aggregation model can be hosted independently by Studio and backend hosts; contributors run concurrently and return per-contributor success or failure envelopes so one contributor cannot fail the whole result. Unfiltered discovery omits contributors the caller is not authorized to inspect.

Attention integrations are dedicated satellite modules named `Elsa.<Domain>[.<Subdomain>].Attention`. They depend on both Attention and the domain while keeping domain modules independent of Attention. The initial integrations are Workflow Runtime, Secrets, and Modularity. Contributors evaluate their complete authorized dataset and return bounded normalized items; the browser never infers an all-clear state from a partial page or sample.

`Elsa.Studio.Attention` is the Studio adapter for Attention. It contributes one ordinary Dashboard Widget rather than receiving hard-coded placement or rendering. The widget ranks items by severity and recency, groups items with typed correlation identifiers, supports snoozing, and shows a compact all-clear state. It exposes no domain mutation actions; investigation and mutation remain on owning domain surfaces.

Studio Preferences is a governed shared capability rather than duplicated Dashboard and Attention storage. `Elsa.Studio.Preferences.Core` and `Elsa.Studio.Preferences.Api` store bounded, versioned documents in registered namespaces scoped by user, tenant, and Studio host identity. Owning domains define defaults, validation, migration, and semantics; Preferences owns durable storage, quotas, optimistic concurrency, timestamps, and transport. The backend is the canonical persistence authority, with device-local fallback only when backend preferences are unavailable or authentication is disabled.

This does not supersede [ADR 0008](0008-keep-theme-store-ownership-in-studio-host.md). Theme definitions, publication state, and material assets remain Studio-host presentation data; a user's selected theme is personal preference state and may use Studio Preferences.

Domain-specific Dashboard data remains outside domain core modules. A backend satellite such as `Elsa.Workflows.Dashboard` produces complete bounded snapshots, while `Elsa.Studio.Workflows.Dashboard` contributes their widgets. The first Workflow widgets are Workflow Portfolio and Workflow Run Health.

## Considered Options

- Requiring one widget per module: module identity does not imply dashboard value and creates permanent clutter.
- Replacing widgets entirely with Attention Items: charts, trends, summaries, and other domain-specific visualizations need an open UI contribution surface.
- Making Attention a Dashboard subdomain: Attention correlation, snoozing, aggregation, and future consumers remain useful outside Dashboard.
- Embedding Attention or Dashboard translation in domain modules: this reverses the intended dependency direction and couples domain behavior to presentation concerns.
- Creating one endpoint per Attention contributor: a shared parameterized aggregation surface provides consistent authorization, bounds, timeouts, and partial-failure semantics while still allowing contributors to be queried in parallel.
- Storing arbitrary preference JSON: registered, versioned namespaces prevent the shared capability from becoming an ungoverned data store.
- Aggregating full datasets in the browser: paginated samples cannot support trustworthy all-clear or statistical claims and can become unsafe at scale.

## Consequences

- The architecture gains additional satellite modules, but domain cores remain independent of Dashboard and Attention.
- Dashboard can provide consistent accessibility, loading, error, refresh, and layout behavior while widget bodies retain domain-specific presentation freedom.
- Attention can serve future consumers outside Dashboard and can isolate contributor failures through its normalized contract.
- Complete attention detection and dashboard statistics require efficient domain-owned aggregate queries for every supported persistence provider.
- Preferences can synchronize across authenticated sessions while remaining governed by registered namespaces and bounded documents.
- The public Dashboard Widget contribution contract changes incompatibly; the old sample, tests, and directly affected public contract documentation must be removed or updated in the same delivery.
- Dashboard continues to summarize what users should know or do next, while Diagnostics remains the troubleshooting and observability surface defined by ADR 0004.
