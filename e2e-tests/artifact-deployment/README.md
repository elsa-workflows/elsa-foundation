# Executable-artifact deployment (spec 151)

Proves the portability claim end to end: a **workflow-executable closure** exported from a
publish-capable engine through the real endpoint —
`GET publishing/workflows/{versionId}/executable-export` — is **imported and activated at startup**
by an engine whose store never held the source definitions, with zero API calls (issue #1304, spec
`specs/151-executable-artifact-reconciliation`). The gate is `GET /health/ready` (200 only after
shell activation, which includes the artifact reconcile pass); `GET /` is not a gate.

Sibling of [`../file-deployment`](../file-deployment/README.md), which does the same for spec 147's
**design-side** definition files. That suite imports workflow *definitions* for the engine to
compile; this one imports already-compiled *artifacts* for an engine that did not compile them.

| Script | What it exercises |
|---|---|
| `Test-ArtifactBasedDeployment.ps1` | Publishes a child workflow and a parent that waits on a `DispatchWorkflow` of it, exports the parent's closure over HTTP, then restarts the server with `JsonWorkflowArtifactReconciliation` composed (`SourceId` + `FolderPath`) **against a freshly schema-deployed empty database**, and asserts: the importing engine's design catalog holds neither definition, both the parent's and the child's activation slots are claimed, the parent executes to completion including the child; a second restart over the unchanged mount is idempotent (same activation, no duplicate); and a v2 closure dropped into the mount supersedes v1 (T095 latest-wins). |

## Current result: GREEN — 13 assertions, 0 failures (2026-08-18)

The round trip works end to end on a real server: publish → export over HTTP → mount → import at
startup → **parent and pinned child both execute** → idempotent restart → newer `ArtifactVersion`
supersedes.

It was red when first written, and both causes were **real production defects this suite found** —
neither visible to any in-process test:

1. **Converter ordering** (fixed in `01939ca63`). `JsonPayloadConvertersInitializingStartupTask` had no
   `[Order]`, so the reconciler deserialized closures at boot before the JSON converters existed and an
   artifact exported at request time could not be read back. Latent for **any** startup task reading a
   payload; this feature was simply the first to lose the race.
2. **Capability collision** (fixed in `1d8a58e89`). `TryAddEnumerable` de-duplicates by *implementation
   type*, and two features registered the same capability class differing only in data, so one was
   silently discarded. A fully-featured engine reported `activity consumer 'elsa.clr-activity' schema
   '1' is not installed` and refused artifacts it could run.

Both were shadowing each other — fixing the first only revealed the second. **That is the argument for
keeping this suite:** every in-process test resolves a fully-initialised serializer and composes its own
capabilities, so neither defect was reachable from there.

**What this suite does NOT prove.** Both halves run on Workbench, whose single shell composes design,
publishing and the compiler, and env vars can override a config value but cannot *remove* a key. So it
proves *"an engine whose store never held these definitions"*, not *"an engine that cannot compile"*.
The assembly-boundary claim (SC-B-001/005) remains in-process-only, and the lockless-composition
failure is unassertable here for the same reason. See spec 151's T126.

## Why a second database

The design-side suite can import into the developer's own store because nothing there owns the
definitions yet. Here it would prove nothing: the artifact would already be in the content-addressed
store and the activation slot would already be owned by the **publish** source, so the import is an
idempotent no-op and every assertion would pass on state the exporter created.

So the suite exports from the developer's normal database and then restarts the server against a
brand-new SQLite file it schema-deploys itself (same Groundwork manifest as
[`../README.md`](../README.md)). That store has never held these definitions — which is the actual
headline claim — and the suite asserts it directly by querying the design catalog on the importing
engine and finding nothing.

## Composition mechanism

Like the design-side suite, this one composes an opt-in feature **via environment variables** (they
layer above `shells.json`, and setting the section enables the feature) on a server process it
manages itself; the already-built `Elsa.Workbench.dll` is launched directly:

```text
CShells__Shells__default__Features__JsonWorkflowArtifactReconciliation__Options__SourceId
CShells__Shells__default__Features__JsonWorkflowArtifactReconciliation__Options__FolderPath
CShells__Shells__default__Features__GroundworkUnifiedPersistenceSqlite__ConnectionString
```

No repo file is edited; cleanup restarts the server plain, on its own database.

`Elsa.Workbench.csproj` does carry a **project reference** to
`Elsa.Workflows.Runtime.Reconciliation` — added for this suite, for the same reason the design-side
pair is referenced: a feature whose assembly is absent from the CShells runtime feature catalog is
*silently skipped*, so composing it by env var would do nothing. Referencing it does not enable it.

## What this suite does NOT prove

**Runtime-only composition.** Spec 151's stated shape is a runtime with *no design, no publishing,
no compiler*. `Elsa.Workbench` composes all three in its single shell, and this suite restarts that
same host — so what is proven is "an engine whose store never held these definitions", not "an
engine with no design surface". The assembly-boundary claim (SC-B-001/005: no
`Elsa.Workflows.Design.*` / `Elsa.Workflows.Publishing*` / `Elsa.Activities.Design.*` assembly
loaded while executing an imported artifact) remains covered **in-process only**, by
`tests/Elsa/Architecture/RuntimeOnlyArtifactCompositionTests.cs` and
`tests/Elsa/Workflows/Runtime/Reconciliation/Tests/RuntimeOnlyLoadedAssemblyTests.cs`. Proving it
against a real server needs a runtime-only *host* — `Elsa.Foundation.Host` loading a runtime-only
package set — which does not exist as a from-source e2e target today.

**The missing-lock composition failure.** The README and quickstart both warn that composing
`JsonWorkflowArtifactReconciliation` without an `Elsa.Locking.*` provider fails at container
validation, and no in-process test covers it. It is not asserted here either: `shells.json` enables
`FileSystemDistributedLocking` for the default shell, and environment variables can override a
configuration value but cannot remove a key, so the lock provider cannot be taken away from the
shell this suite restarts. Asserting it needs a shell whose whole feature list is under the test's
control.

## Caveats

- Manages the server process on port 5095 (stop/start, like `durability/` and `file-deployment/`) —
  don't run it while another suite is mid-flight.
- Requires the standard from-source setup (build + Groundwork schema deploy) per
  [`../README.md`](../README.md).
- Runs `dotnet tool run groundwork -- apply` once per run against a temp database; that is the
  slowest step, and the whole suite takes several minutes because it restarts the server four times.
- Definition names/ids are timestamped per run. The parent/child definitions published in phase A
  remain in the developer's SQLite catalog afterwards; the importing engine's database, the mount
  and the staging folder are all temp files and are removed.
