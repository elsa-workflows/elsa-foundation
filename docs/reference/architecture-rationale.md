# Architecture Rationale

This reference preserves explanatory context that supports the constitutions without
being the enforceable gate surface. The constitutions keep the rules, allowed
exceptions, status, and governance.

## Workflows.Design and Workflows.Runtime split

The Workflows.Design and Workflows.Runtime split enables three deployable
distribution shapes:

| Distribution | Dependencies | Purpose |
|---|---|---|
| WorkflowDesigner | Design only | Build, edit, and persist workflow definitions. |
| WorkflowExecutor | Currently both Design and Runtime | Execute workflows. The long-term goal is Runtime-only through the deferred execution seam. |
| RuntimeMonitorService | Runtime only | Report on execution state, execution log, and runtime persistence. |

The naming convention makes the split visible at the project boundary so the
dependency direction can be audited or enforced in CI.

Rejected names for the design sub-domain:

- `Elsa.Workflows.Management.*` was too broad because management could cover
  runtime concerns.
- `Elsa.Workflows.Definitions.*` was ambiguous because both Design and Runtime
  concern workflow definitions in different forms.
- `Elsa.Workflows.Design.*` names the activity of designing workflows, which
  keeps the asymmetry with Runtime explicit.

`WorkflowDefinitionState`, read models/projections, and `WorkflowExecutable`
complete the Design-side triplet that names how authored content eventually
reaches Runtime.

## Elsa foundation repo composition

The 2026-05-11 snapshot treated the foundation repo as the local-development
baseline: the host, primitives, workflow design/runtime cores and defaults,
default persistence, activity and expression abstractions, and serialization
basics remained in `elsa-foundation`.

Standalone-feature candidates were provider-heavy or optional dependency
surfaces: non-SQLite EF Core providers, optional expression engines, MassTransit,
non-default locking providers, drive/Redis/SaaS integrations, and serialization
variations beyond the default.

EF Core persistence stayed in the foundation repo pragmatically because moving it
out impeded nearby feature development. That stance remains revisable when a
cleaner split no longer slows the work.

## Activity picker catalog rationale

The Elsa 3 picker enumerated loaded `IActivity` implementations at picker time.
That made picker output depend on loaded assemblies: implicit, difficult to
audit, and poorly suited to non-CLR activities.

The catalog-as-source-of-truth rule makes picker output depend on persisted,
queryable, provenance-bearing catalog rows. CLR activities, JSON descriptors,
workflow descriptors, scripts, and future sources can all enter through the same
catalog contract.

Activity availability is a policy layer on top of the catalog, not a replacement
for it. Host configuration may optionally define the maximum baseline through
activity keys and host-defined activity sets; management settings may narrow that
baseline; future user-context policy such as RBAC can narrow it further. This
policy controls picker addability only: existing workflow definitions still
render their authored activity nodes even when those activity types are no longer
available for new selection.

## Framework composition rationale

Replacement-contract examples:

- An `IServiceBus` contract selects one service bus implementation for the host.
  A second registration is a conflict.
- A distributed-lock contract selects one lock implementation for the application
  configuration. Multiple registrations would be ambiguous.

The contributor-interface plus single-handler sub-pattern centralizes fan-in
logic inside the event pipeline. A contributor interface such as
`IDraftValidator` or `IJsonConverterSource` names what a feature contributes;
the owning feature's single handler owns iteration, ordering, and aggregation.

The first sync-contributor exception was EF Core's `OnModelCreating` lifecycle
hook in `Elsa.Persistence.EFCore`: the dispatch site is intrinsically sync, the
contribution mutates a `ModelBuilder`, and the target object does not exist at
startup. Future uses of the exception compare their shape to that case.

## Domain-level shadow properties

Domain-level shadow properties are real CLR properties on entities that are
hidden from read interfaces. They are different from provider-side shadow
properties such as EF Core string-keyed properties.

The real-property rule keeps invariant scanners, cross-cutting attributes, test
code, and non-EF providers aligned around the CLR entity shape. Provider shadow
properties remain appropriate only for provider-internal bookkeeping that does
not belong on the CLR class.

## Pattern catalog rationale

The sanctioned-patterns catalog exists to make modular-design choices
predictable. Reviewers can ask which catalogued pattern applies instead of
evaluating an invented local shape from scratch. If a recurring problem does not
fit the catalog, the new pattern is raised, documented, ratified, and then added.

## Runtime composition strategy

Two Nuplane strategies were considered:

- Strategy A: Nuplane manages everything, including `.Core` libraries, and the
  whole runtime is replaced atomically.
- Strategy B: the host pins `.Core` libraries while Nuplane dynamically loads
  Layer-3 implementations, helper libraries, and optional features.

Strategy B is the framework default because the host contract surface remains
stable and inspectable. Strategy A remains a deliberate deployment choice where
the full runtime replacement boundary is prevalidated.
