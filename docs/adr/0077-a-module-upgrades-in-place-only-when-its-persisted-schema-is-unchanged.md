---
status: accepted
date: 2026-09-22
amended: 2026-09-28
decision_context: Questions raised at the 2026-09-21 demo and relayed in the Teams thread on this programme; decided by Sipke Schoorstra on issues #1945, #1946 and #1947, which record the options and the evidence behind each.
amendment_context: 2026-09-23, the fifteenth module's declared schema version was wired up — stamped on write and checked first on read in the publication-policy and projection-intent stores — so every count of checking read paths in this ADR now reads fifteen rather than fourteen. 2026-09-24, the destination changed from additive-only to the cluster version gate of ADR 0078, decided by Sipke Schoorstra, after additive-only was found to burden versions that have already shipped and to lose data when an older writer rewrites a newer row. 2026-09-27, the unit this ADR checks was corrected from "EF module" to schema family, and the description of a pre-load `IPackageActivationGate` gate that was never built was corrected to the enable-time guard that exists, both per the drift noted on #2096; accepted by Sipke Schoorstra on 2026-09-27 on issue #2096, matching the rollout decisions recorded on #2093. 2026-09-28, #2119 stamped every EF content table's schema version, so every count of schema families in this ADR now reads twenty-six rather than fifteen; approved by Sipke Schoorstra.
---

# A module upgrades in place only when its persisted schema is unchanged

Status: accepted (2026-09-27). Sipke Schoorstra accepted this ADR after it was amended to match the
rollout decisions recorded on [#2093](https://github.com/elsa-workflows/elsa-foundation/issues/2093)
(2026-09-27): the schema-version unit is the schema family, not the EF module, and the drift the
acceptance review on [#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096) found
between this text and the code — a pre-load Nuplane activation gate that was designed but never built
— is corrected to describe what exists.

## Context

Independent module release (ADR 0067, FR-1 #1144) makes it possible for two versions of the same
module to exist at once: during a rolling deploy, across pods, or because one host took a module
update and another did not. Three questions were asked at the demo — whether hot reload survives,
what happens with multiple pods, and what happens when two versions want different schemas — and they
turned out to be one question asked of three things: the database, the cluster, and a single process
over time.

**There is no compatibility rule today. There is an unstated assumption of a single writer version**,
and two mechanisms that enforce it by accident rather than by design.

**A version a reader does not recognise is reported as corruption.** Twenty-six schema families declare a
schema-version constant, and all twenty-six now check it on a read path: twelve runtime families, inside
the `Workflows.Runtime` EF module (`BookmarkStateEfModule`, `RuntimeActivationSlotEfModule`,
`RuntimeActivityExecutionEfModule`, `RuntimeArtifactEfModule`, `RuntimeOperationalStateEfModule`,
`RuntimePostCommitOutboxEfModule`, `RuntimeSchedulerPoisonEfModule`, `RuntimeTriggerBindingEfModule`,
`RuntimeWorkflowAlterationEfModule`, `RuntimeWorkflowDispatchEfModule`, `RuntimeWorkflowExecutionEfModule`,
`RuntimeWorkflowTestScopeEfModule`); three publishing families, inside `Workflows.Publishing`
(`PublishingLedgerEfModule.ContentSchemaVersion`, `PublishingPolicyProjectionEfModule.SchemaVersion` and
`PublishingSnapshotReviewEfModule.SchemaVersion`); one import family, inside
`Elsa3.Activities.Design.Import` (`Elsa3ImportEfModule`); and one family in each of the remaining ten EF
modules — `Secrets` (`SecretsEfModule`), `Workflows.Design` (`WorkflowsDesignEfModule`),
`Workflows.Runtime.Distributed.Placement` (`ExecutionPlacementEfModule`),
`Workflows.Runtime.Distributed.CommandTransport` (`ExecutionCommandTransportEfModule`),
`Activities.Design` (`ActivitiesDesignEfModule`), `Identity.Iam` (`IdentityIamEfModule`),
`Identity.ProviderConfiguration` (`IdentityProviderConfigurationEfModule`), `Diagnostics.OpenTelemetry`
(`EfOpenTelemetryModule`), `Diagnostics.StructuredLogs` (`StructuredLogsEfModule`) and
`Studio.Preferences` (`StudioPreferencesEfModule`) — module names
from [ADR 0076](0076-persistence-tooling-runs-inside-the-host-closure.md) D3's thirteen-name vocabulary,
every one of which now stamps at least one family.
All are `1.0.0` except `ContentSchemaVersion`, which is `1`. Only fourteen checked it when this ADR was
written: the fifteenth,
`PublishingPolicyProjectionEfModule.SchemaVersion`, was declared but neither written nor read, so
`elsa_publication_policies` and `elsa_publication_projection_intents` carried no version column at all
and a newer version's rows were read as if this build had written them. That gap was closed by wiring
the constant up — the column is now stamped on every write and checked first on every read — rather
than by deleting the constant, which would have left the two tables permanently unversioned. The field
is checked for equality **alongside hash and order-key integrity checks**:

```csharp
if (row.Revision <= 0 || row.SchemaVersion != RuntimeOperationalStateEfModule.SchemaVersion)
    throw new InvalidDataException("The workflow run-health row envelope is corrupt.");
```

It is an envelope-integrity field, so a version it does not know is corruption by construction. An
operator meeting this during a deploy sees a data-incident message for what is a sequencing mistake.

*Amended 2026-09-27.* This ADR first called these fifteen constants "EF modules". They are schema
families instead: what a row's `SchemaVersion` carries, and what the mismatch check above compares.
Twenty-six families live inside all thirteen EF modules ADR 0076 D3 names — most modules host exactly
one, but `Workflows.Runtime` hosts twelve and `Workflows.Publishing` hosts three — so it is still not
one family per module. Decided by Sipke Schoorstra on [#2093](https://github.com/elsa-workflows/elsa-foundation/issues/2093)
(2026-09-27), settling drift the acceptance review on
[#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096) found between this text and the
code. Enable-time refusals stay per EF module ([ADR 0076](0076-persistence-tooling-runs-inside-the-host-closure.md)
D9): a guard maps a feature to the module it depends on, not to one family inside it.

*Amended 2026-09-28.* [#2119](https://github.com/elsa-workflows/elsa-foundation/issues/2119) (PR
[#2131](https://github.com/elsa-workflows/elsa-foundation/pull/2131)) stamped every EF content table's
schema version, growing the checked count from fifteen to twenty-six and taking the count of EF modules
with a checked family from three of thirteen to all thirteen. Approved by Sipke Schoorstra.

**The default migrate policy opens the skew window automatically.** `EfMigrateOptions.DefaultPolicy`
is `EfMigratePolicy.AutoMigrate`, and `EfDatabaseMigrator.ApplyAsync` migrates in-process. The
alternative, `Validate`, fails closed and points at the out-of-process tool.

What *is* already solved: #1845 gave every EF module its own migrations-history table, so modules
migrate independently of each other. And the codebase already knows how to handle an unrecognised
version deliberately — `BpmnGraph` refuses to execute a node whose `StructureSchemaVersion` it does
not recognise, naming the version. The mechanism exists; the rule does not.

## Decision

**Within a major, a module may be upgraded in place while running, provided the schema families it
persists are unchanged. A schema-family change requires coordination. Version skew is detected and
refused with a version diagnostic, never reported as corruption.**

**Skew is not corruption.** Envelope integrity and readable version become separate concerns. A row
whose `SchemaVersion` a reader does not recognise raises a diagnostic naming the schema family, the EF
module it lives in, the row's version, the reader's version and the remedy — not
`InvalidDataException: … envelope is corrupt`.
These are different operator actions: one is a data incident, the other a deploy-sequencing mistake.
This part is required regardless of anything else here and is the first thing to build.

**Hot reload stays production-supported, and gains the floor check it lacks.** The reload bridge
(`ShellReloadOnPackagesChanged`, hooking Nuplane's reconciliation-completion phase) performs **no
version or compatibility checking at all** today: it reloads whatever the feed reconciled, so an
incompatible module loads and fails later at a missing member. Reconciliation must refuse a module
whose declared floor the running host cannot satisfy, naming the package, the required range and the
host version. `Elsa.Workbench` registers `NullShellReloader`, a literal no-op, so hot reload is the
Foundation.Host feed model rather than a whole-product capability; that asymmetry is documented rather
than left implicit.

**Mixed-version pods are supported when the schema is unchanged.** A code-only module update rolls
normally, and resumption across pods works, because persisted runtime state is governed by the same
`SchemaVersion` check. This is the common case and carries most of the operational value of
independent release.

**The destination is the cluster version gate of
[ADR 0078](0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md),
not this decision.** A new schema-family version reads its predecessor's rows but keeps writing the old
format until every live host can read the new one; only then is the new version finalized and written.

*Amended 2026-09-24.* This ADR first named **additive-only within a major** as the destination: readers
tolerating unknown columns and refusing only an unrecognised major. It was replaced for two reasons. It put
the compatibility burden on versions that have already shipped, which cannot learn anything new. And it could
lose data without any error: eighteen entities store their content as a JSON document, and an older version
resuming a row that a newer one wrote would re-serialise it without the fields it does not know. Resumption
across pods makes that routine rather than rare. The gate puts the burden on the new version instead, which is
being written now and can carry it.

## How this composes with runtime module installation

A schema-changing module arriving through the Nuplane feed is **gated at enable time, not
half-applied**. [ADR 0076](0076-persistence-tooling-runs-inside-the-host-closure.md) D9 provides
`IFeatureActivationGuard`, implemented as `EfPendingMigrationActivationGuard` and registered in
`Elsa.Workbench`; under `Validate` it refuses a feature whose module has pending migrations with an
HTTP 409 naming the exact `script` command, and saves nothing.

*Amended 2026-09-27.* D13's second, pre-load gate — an Elsa implementation of Nuplane's
`IPackageActivationGate`, reading `[EfModule]` metadata before a package's load context is constructed
— was never built: no type implements that interface anywhere in `src/` (checked by `git grep`). The
restart-and-reconcile case it was designed for has no Elsa-owned activation gate today; the nearest
mechanism is a different one, not a later-built version of the same one —
[#1951](https://github.com/elsa-workflows/elsa-foundation/issues/1951), delivered by
[PR #1979](https://github.com/elsa-workflows/elsa-foundation/pull/1979), refuses a package whose host
is too old for it, at Nuplane's own resolution stage, not at activation and not through
`IPackageActivationGate`.

**On a single host this composes fully, and needs no drain and no roll.** The package arrives, the
gate refuses it, the operator applies the migration out of process, and the next reconciliation
activates the module. It waits rather than half-applying.

**On a cluster the constraint is real, and the reason is not the migration.** An additive migration
leaves the older version working. What breaks it is the newer version **beginning to write rows**
carrying a new `SchemaVersion`. The ordering that matters is therefore not "migrate before rolling"
but "do not let the new version write while the old one is still reading".

## Known gap

`Validate` prevents a premature migration. It does **not** prevent **ragged activation**: once the
schema is applied, pods reconcile at their own pace, so the first pod to activate the new module
begins writing rows the not-yet-reloaded pods refuse. **Runtime module installation through the feed
and multi-pod do not compose under detect-and-refuse.** ADR 0078 closes this by design, though it is not
yet built: no host writes a new version until every live host can read it, so the order in which pods
activate stops mattering and runtime installation works on a cluster.

## Considered options

- **Additive-only within a major.** This ADR's original destination, replaced on 2026-09-24 by the
  cluster version gate of ADR 0078. It burdens versions that have already shipped, and an older writer
  rewriting a newer row's JSON content drops the fields it does not know without reporting anything.
- **Detect and refuse, permanently.** Every schema change coordinated, forever. Rejected because it
  permanently caps what independent release can promise, and the ragged-activation gap above shows the
  cap falls exactly where runtime installation is most valuable.
- **A schema per module version, never shared.** Total isolation, no skew possible. Rejected: the
  migration and data-continuity story is worse than the problem.

## Consequences

- FR-1 (#1144) can promise independent **versioning** for every module, but independent **deployment
  alongside its predecessor** only for modules whose schema does not move. This is a limit of the
  capability and belongs in FR-1's body, not a footnote.
- The first implementation step is small and independent of the rest: separate "corrupt" from "written
  by a version I do not know".
- Hot reload gains a refusal path it does not have, which converts a late failure at a missing member
  into an early one naming the package and range.
- Ragged activation remains unsolved on clusters until the gate in ADR 0078 is built. Until then,
  installing a schema-changing module at runtime is a single-host capability.
- Quiescing in-flight work before an assembly swap was considered and not adopted. It may be redundant
  if checkpointing already makes a mid-execution swap safe, which should be verified before any work is
  committed to it.

## Linked decisions

- [ADR 0078](0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md) — cluster membership, and the cluster version gate that is this ADR's destination
- [ADR 0067](0067-package-versioning-uses-two-lines-with-computed-patch.md) — the versioning this rule constrains
- [ADR 0076](0076-persistence-tooling-runs-inside-the-host-closure.md) — D9's activation guards, which this relies on
- [ADR 0073](0073-ef-core-is-the-only-first-party-persistence-family.md) — the persistence family this applies to
- Issues: #1945 (this rule), #1946 (hot reload), #1947 (multi-pod), #1144 (FR-1), #2093 (cluster rollout program), #2096 (this acceptance and the family/module and gate corrections), #2119 (schema family count correction, fifteen to twenty-six)
