# Research: Dashboard Widgets and Attention

## Dashboard ownership and extraction

**Decision**: Extract Dashboard from the Studio Web shell into a mandatory
`Elsa.Studio.Dashboard` module. Register `/dashboard`, `/`, and legacy
`/overview` routes while the SDK continues to own the Dashboard Slot.

**Rationale**: Dashboard has its own contract, preferences, layout, and lifecycle.
Keeping a special `App.tsx` render branch obscures ownership.

**Alternatives considered**: Keep Dashboard embedded in Web; rejected because
the host remains coupled to one feature. Redirect aliases only; rejected because
bookmarked entry points should continue to render predictably.

## Dashboard Widget contract

**Decision**: Replace `{id,title,order,component}` with a host-framed contribution
containing module identity, permissions, semantic sizes, refresh/cache/timeout
metadata, optional versioned settings, optional cancellable loader, and body
component. No compatibility adapter ships.

**Rationale**: Host-owned loading, errors, accessibility, refresh, and layout are
required for consistent charts and data widgets. Optional loaders preserve
local/static content.

**Alternatives considered**: Self-framed components; rejected for inconsistent
UX and hidden polling. Remove Widgets for Attention; rejected because charts and
summaries need arbitrary presentation.

## Accessible layout

**Decision**: Use small/medium/wide/full host sizes and keyboard move, resize,
hide, and restore controls. Pointer drag is optional.

**Rationale**: No accessible drag dependency exists; keyboard controls meet the
behavior without making drag the only path.

## Shared settings editors

**Decision**: Move generic setting-editor selection and built-in editors from
optional Feature Management into mandatory shared Studio infrastructure.
Dashboard reuses them but persists values as preferences.

**Rationale**: Dashboard cannot depend on Feature Management load order or
enablement, and personal widget preferences are not administrative settings.

## Auth-disabled and scope behavior

**Decision**: Always provide a safe session context, including explicit
anonymous state when auth is disabled. Scope cache/fallback keys by Studio host,
backend, subject, and tenant; cancel/clear immediately on scope change.

**Rationale**: Current auth-disabled rendering has no provider, and URL-only
cache keys can leak state across scopes.

## Stable Studio host identity

**Decision**: Add a non-secret configured Studio host ID to runtime config and
send it to Preferences.

**Rationale**: Multiple Studio deployments may share a backend or change origin;
origin/backend shell identity is not a stable isolation key.

## Studio Preferences

**Decision**: Provide registered, versioned, quota-bound namespaces with
optimistic revisions. Canonical storage lives on Elsa Server through Groundwork;
anonymous/unavailable mode uses explicitly device-local storage.

**Alternatives considered**: Browser-only storage; rejected as not cross-device.
Separate Dashboard/Attention stores; rejected as duplicate infrastructure.
Arbitrary JSON API; rejected for quota/security/schema risks.

## Attention aggregation

**Decision**: Use `Elsa.Attention.Core` and `Elsa.Attention.Api`. A parameterized
endpoint runs contributors concurrently with permission checks, cancellation,
five-second timeout, two-call budgets, item bounds, and per-contributor envelopes.
Studio and backend hosts expose the abstraction independently.

**Rationale**: Shared structure removes repetitive endpoint plumbing without
moving domain detection rules into Studio. Filters still permit parallel loads.

## Attention dependency direction

**Decision**: Satellite names follow `Elsa.<Domain>[.<Subdomain>].Attention`.
Domain cores never depend on Attention.

## Secrets tenant prerequisite

**Decision**: Add authoritative tenant identity and tenant-filtered repository
operations to Secrets before Secrets Attention. Existing rows require an explicit
migration/backfill policy; missing subject/tenant fails closed.

**Rationale**: Secrets are currently global, so permission filtering alone cannot
meet tenant isolation.

## Workflow aggregate strategy

**Decision**: Use persistence-neutral snapshot query ports with provider-specific
exact adapters rather than persisted Dashboard projections. Groundwork pages and
folds indexed results in bounded memory; EF Core aggregates in the database;
in-memory adapters enumerate exact test/development state.

**Rationale**: Projections introduce drift/backfill/atomicity. Draft validation
is derived and can change with validators/configuration. Existing list APIs are
unbounded and perform N+1 reads.

**Alternatives considered**: Client aggregation; rejected as sampled and unsafe.
Persisted projections; rejected for drift. Existing store ports; rejected because
they materialize complete collections.

## Official persistence matrix

**Decision**: Validate against in-memory runtime, EF Core SQLite workflow design,
Groundwork SQLite unified, and Groundwork PostgreSQL unified. Unknown third-party
providers return unavailable.

## Invalid draft semantics

**Decision**: Page current drafts and derive validation through the existing gate
with bounded concurrency. Cache completed snapshots only; do not persist validity.

## Published and run semantics

**Decision**: Published definitions require a live Published executable source
reference. Runs use inclusive `from`, exclusive `to`, supplied IANA time zone,
hour/day buckets, independent outcome/incident dimensions, and exclude test runs
by default.

## Verification strategy

**Decision**: Each slice receives focused unit/contract tests, full Studio and
Foundation suites, and a real `/dashboard` browser proof. Provider fixtures
exceed normal page sizes and cover DST, auth, partial failure, concurrency, and
scope switching.
