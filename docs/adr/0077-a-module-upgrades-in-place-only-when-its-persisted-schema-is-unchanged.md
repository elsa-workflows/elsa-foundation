---
status: proposed
date: 2026-09-22
amended: 2026-09-23
decision_context: Questions raised at the 2026-09-21 demo and relayed in the Teams thread on this programme; decided by Sipke Schoorstra on issues #1945, #1946 and #1947, which record the options and the evidence behind each.
amendment_context: the fifteenth module's declared schema version was wired up — stamped on write and checked first on read in the publication-policy and projection-intent stores — so every count of checking read paths in this ADR now reads fifteen rather than fourteen.
---

# A module upgrades in place only when its persisted schema is unchanged

## Context

Independent module release (ADR 0067, FR-1 #1144) makes it possible for two versions of the same
module to exist at once: during a rolling deploy, across pods, or because one host took a module
update and another did not. Three questions were asked at the demo — whether hot reload survives,
what happens with multiple pods, and what happens when two versions want different schemas — and they
turned out to be one question asked of three things: the database, the cluster, and a single process
over time.

**There is no compatibility rule today. There is an unstated assumption of a single writer version**,
and two mechanisms that enforce it by accident rather than by design.

**A version a reader does not recognise is reported as corruption.** Fifteen EF modules declare a
schema-version constant, and all fifteen now check it on a read path: twelve runtime modules
(`BookmarkStateEfModule`, `RuntimeActivationSlotEfModule`, `RuntimeActivityExecutionEfModule`,
`RuntimeArtifactEfModule`, `RuntimeOperationalStateEfModule`, `RuntimePostCommitOutboxEfModule`,
`RuntimeSchedulerPoisonEfModule`, `RuntimeTriggerBindingEfModule`, `RuntimeWorkflowAlterationEfModule`,
`RuntimeWorkflowDispatchEfModule`, `RuntimeWorkflowExecutionEfModule`, `RuntimeWorkflowTestScopeEfModule`),
plus `PublishingLedgerEfModule.ContentSchemaVersion`, `Elsa3ImportEfModule` and
`PublishingPolicyProjectionEfModule.SchemaVersion`. All are `1.0.0` except `ContentSchemaVersion`,
which is `1`. Only fourteen checked it when this ADR was written: the fifteenth,
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

**The default migrate policy opens the skew window automatically.** `EfMigrateOptions.DefaultPolicy`
is `EfMigratePolicy.AutoMigrate`, and `EfDatabaseMigrator.ApplyAsync` migrates in-process. The
alternative, `Validate`, fails closed and points at the out-of-process tool.

What *is* already solved: #1845 gave every EF module its own migrations-history table, so modules
migrate independently of each other. And the codebase already knows how to handle an unrecognised
version deliberately — `BpmnGraph` refuses to execute a node whose `StructureSchemaVersion` it does
not recognise, naming the version. The mechanism exists; the rule does not.

## Decision

**Within a major, a module may be upgraded in place while running, provided its persisted schema is
unchanged. A schema change requires coordination. Version skew is detected and refused with a version
diagnostic, never reported as corruption.**

**Skew is not corruption.** Envelope integrity and readable version become separate concerns. A row
whose `SchemaVersion` a reader does not recognise raises a diagnostic naming the module, the row's
version, the reader's version and the remedy — not `InvalidDataException: … envelope is corrupt`.
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

**Additive-only within a major is the stated destination, not this decision.** Reaching it means
changing fifteen modules' read paths and splitting `SchemaVersion` into envelope integrity and a
readable range, so that a reader tolerates unknown columns and refuses only an unrecognised major.

## How this composes with runtime module installation

A schema-changing module arriving through the Nuplane feed is **gated, not half-applied**. ADR 0076 D9
already provides two independent gates: `IFeatureActivationGuard`, which under `Validate` refuses a
feature whose module has pending migrations with an HTTP 409 naming the exact `script` command and
saves nothing; and Nuplane's `IPackageActivationGate`, evaluated in
`PackageLoader.EnsureGraphLoadedAsync` **before the load context is constructed**, reading `[EfModule]`
metadata without loading the package.

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
and multi-pod do not compose under detect-and-refuse.** This is recorded rather than closed, and it is
the strongest argument for additive-only — once the older version can read the newer version's rows,
ragged activation is harmless and runtime installation works on a cluster.

## Considered options

- **Additive-only within a major, now.** The end state, and what independent release means to an
  operator. Rejected as immediate work, not as a destination: it changes fifteen modules' read paths
  and the meaning of a field, which is a larger change than the rule it would support.
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
- Ragged activation remains unsolved on clusters. Until additive-only lands, installing a
  schema-changing module at runtime is a single-host capability.
- Quiescing in-flight work before an assembly swap was considered and not adopted. It may be redundant
  if checkpointing already makes a mid-execution swap safe, which should be verified before any work is
  committed to it.

## Linked decisions

- [ADR 0067](0067-package-versioning-uses-two-lines-with-computed-patch.md) — the versioning this rule constrains
- [ADR 0076](0076-persistence-tooling-runs-inside-the-host-closure.md) — D9's activation guards, which this relies on
- [ADR 0073](0073-ef-core-is-the-only-first-party-persistence-family.md) — the persistence family this applies to
- Issues: #1945 (this rule), #1946 (hot reload), #1947 (multi-pod), #1144 (FR-1)
