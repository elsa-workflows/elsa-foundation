# Spec 151 → Groundwork v2: port scope

**Status:** scoped; open questions resolved except (3), which is with the Groundwork owner. Tasks converged as Phase 10 in [tasks.md](tasks.md). Written 2026-08-24 against `main` at `f2b88192e`, branch `1304-executable-artifact-reconciliation` at `bcfe7e9ef`.

## Why this document exists

`main` replaced the persistence platform this spec is built on. Merging `main` produces 73 conflicts, but the conflict count is misleading: **40 of them are `UD` — "we modified, they deleted".** This is not a merge to resolve, it is a port to plan.

Neither resolution is available:

- **Take theirs** — the activation-slot ledger, the activation authority, the trigger spine and the executable source references cease to exist. The feature is gone.
- **Keep ours** — v1 files referencing APIs `main` removed. Does not compile.

## What changed on `main`

Measured with `git diff --name-status $(git merge-base HEAD origin/main) origin/main`:

| Area | Added | Deleted | Modified |
|---|---|---|---|
| `src/Elsa/Persistence/Groundwork/` | 78 | **175** | 15 |
| `src/Elsa/Workflows/Publishing/Persistence/` | 1 | 3 | 17 |

The v1 abstractions this spec is written against have **zero matches on `main`**: `IGroundworkDocumentStore`, `BoundedDocumentStore`, `GroundworkDocumentStore`.

A new project appears — `Elsa.Persistence.Groundwork.V2` (plus `…V2.Providers`) — introduced by #1420 ("Migrate Publishing and the design-lane composition to Groundwork v2") and extended by #1431, #1433, #1434, #1435.

## The model change

v1 was **document/envelope-based**: document kinds, per-kind document versions, upcasters, canonical JSON, bounded document stores, query routes declared centrally in `ElsaGroundworkQueryRoutes`.

v2 is **row-based**. Each entity is a pair:

```
GroundworkV2<Entity>StorageConventions   (~170 lines)  - row values + projections + deserialize
GroundworkV2<Entity>Store                (~750 lines)  - the store itself
```

`Values(...)` writes an id, a single manifest-level schema version, a JSON content blob, and a dictionary of **projected lookup fields**. Queries are served from those projections rather than from centrally-declared routes.

Two consequences matter more than the rest:

1. **There is one `ElsaRuntimeV2StorageManifest.SchemaVersion` for the whole manifest**, not a version per document kind.
2. **`main` has no upcaster or document-version files at all** (0 matches for `Upcaster|DocumentVersions`).

## Spec 151's persistence surface

32 files changed since the merge base: 3 added, 4 deleted, 24 modified, 1 renamed.

```
A  Stores/GroundworkWorkflowActivationAuthority.cs
A  Stores/GroundworkActivationProjectionState.cs
A  Exceptions/GroundworkWorkflowActivationAuthorityException.cs
R  Stores/GroundworkPublicationProjectionStore.cs → GroundworkActivationProjectionStore.cs
M  ElsaRuntimeStorageManifest.cs, Querying/ElsaGroundworkQueryRoutes.cs,
   Serialization/ElsaRuntimeDocumentVersions.cs, Serialization/GroundworkRuntimeDocumentSerializer.cs,
   Stores/{WorkflowTriggerBinding,RecurringTriggerSchedule,WorkflowExecutableSourceReference}Store.cs,
   8 per-provider shell features, DI registration, physicalizers,
   Publishing/…/{PublishingGroundworkStorageManifest,…ManifestSource,…StoreRegistration}.cs
D  Publishing/…/Stores/GroundworkPublicationSlotStore.cs
D  Publishing/…/Stores/GroundworkPublicationProjectionIntentStore.cs
D  Stores/GroundworkPublicationProjectionState.cs
D  Serialization/WorkflowExecutableSourceReferenceDocumentV4ToV5Upcaster.cs
```

## Scope

### A. Already done for us — verify only

`main` has already ported the entities this spec touches, except one:

| Entity | v2 status |
|---|---|
| `WorkflowTriggerBinding` | `GroundworkV2WorkflowTriggerBindingStore` + conventions ✅ |
| `RecurringTriggerSchedule` | ported ✅ |
| `WorkflowExecutableSourceReference` | ported ✅ |
| `WorkflowExecutable` | ported ✅ |
| `ExecutableActivityTemplate` | ported ✅ |

**Work:** diff each v2 store against our v1 version and establish whether spec-151 field and behaviour deltas survived the port — `Source`, `Revision`, the store-side filtering from T145, and the bounded-read shapes. Do not assume; `main` ported from the pre-151 v1, so our deltas are probably absent.

### B. Must be built new — the core of the port

**v2 has zero mentions of "slot" or "activation" in its runtime manifest.** Spec 151's central concept does not exist there.

| Item | Shape | Rough size |
|---|---|---|
| `GroundworkV2WorkflowActivationSlotStorageConventions` | follow the trigger-binding template | ~170 lines |
| `GroundworkV2WorkflowActivationSlotStore` | ditto | ~750 lines |
| Activation authority (CAS over the slot row) | `IWorkflowActivationAuthority` on v2 semantics | new |
| Activation projection state + store | | new |
| Manifest fields + storage unit in `ElsaRuntimeV2StorageManifest` | | small |
| Registration in `GroundworkV2RuntimeRegistration` | | small |

The compare-and-swap semantics are the part to think hardest about: v1 gave us document-level concurrency, v2 is row-based with a session/transaction model (`GroundworkStorageTransaction`, `GroundworkStorageSessionGate`). The authority's `TakeOver` intent and `Revision` check must be re-expressed in that model, not transliterated.

### C. Must be re-expressed

| Item | Note |
|---|---|
| The 4 activation-slot query routes (T143) | `ElsaGroundworkQueryRoutes` is deleted. In v2, reads come from projected fields. The GW-QUERY-008 guarantee — served natively, not client-materialised — has to be re-established in v2's terms. |
| T117: slot reads are runtime-owned | `main`'s v2 **still has `GroundworkPublicationSlotStore` and `GroundworkPublicationProjectionIntentStore` on the publishing side**. Our move of slot ownership to the runtime authority has to be re-applied against a publishing lane that has just been re-migrated. This is the biggest semantic collision in the port. |
| Publishing manifest changes | rebased onto `PublishingGroundworkStorageManifest` as v2 leaves it |

### D. Obsolete — delete, do not port

Genuinely good news; this is spec-151 work that v2 makes unnecessary:

- **T142's per-document-kind version bumps** (`workflowTriggerBinding` 2→3, `recurringTriggerSchedule` 2→3, `publicationProjectionState` 1→2) — v2 has one manifest-level schema version.
- **`ElsaRuntimeDocumentVersions`, `GroundworkRuntimeDocumentSerializer`, the V4→V5 upcaster** — no equivalent on `main`.
- **The `v3/` and `v5/` document fixtures** and their bridge tests.
- **The `ActivitiesDesignStorageManifest` nullability fix** (`d539b591f`) — v2 deletes the auto-physicalization block it patched (`main`'s file is 275 lines against our ~1,090). **This means the regression reported to the Groundwork owner is very likely moot**; confirm before he spends time on it.
- The v1 manifest goldens (`Goldens/runtime.json`, `Goldens/publishing.json`) as this spec shaped them.

### E. Tests

31 of the 73 conflicts are tests. They divide the same way: those covering ported entities need rebasing onto v2 fixtures, those covering the activation ledger need writing against the new store, and those covering document versioning are obsolete with (D).

The two e2e suites are unaffected by the model change — they drive HTTP — but they are the acceptance gate for the port, and `Test-RuntimeOnlyArtifactDeployment` in particular proves the runtime-only engine still reconciles.

## Open questions — resolved 2026-08-24

**1. Lane ownership of the activation slot — RESOLVED: re-apply T117.** The slot stays runtime-owned and the authority remains the single writer, as in v1. `main`'s v2 still carries `GroundworkPublicationSlotStore` and `GroundworkPublicationProjectionIntentStore` on the publishing side, so the port has to re-apply the move against a lane that was just re-migrated. This is the largest semantic item in the port and is sequenced last on purpose.

**2. The native-plan guarantee under v2 — RESOLVED: provider admission (A) plus a token ratchet (C).** v2 changed the kind of guarantee rather than dropping it. In `main`'s own words, in `DesignPersistenceBoundedQueryTests`: *"certification of newly written scale-bearing paths is owned by the provider admission layer (undeclared shapes fail before I/O) and the provider plan contract suite."* v1 proved after the fact that a read ran natively; v2 makes it structural — an undeclared shape does not execute at all.

- **A. Declare the activation-slot read shapes to the provider admission layer.** This is the real guarantee and the mechanism v2 is designed around: a client-materialising slot read cannot ship, because it fails before I/O.
- **C. Add a token ratchet** mirroring `DesignPersistenceBoundedQueryTests`, failing if a load-all or client-evaluation token appears in the activation-slot sources. Cheap, runs on a laptop, and stops the guarantee being quietly undone.
- **B (rejected for now): enrolling the slot reads in the provider plan contract suite.** Strongest evidence and closest to what T143 did in v1, but it is the container-backed suite that currently hangs locally (#1422). Held in reserve: add it only if the Groundwork owner says admission does not cover the shapes the ledger needs — a natural part of the question 3 conversation, since both concern what v2 guarantees at the storage boundary.

**3. Is the CAS/`Revision` contract expressible on `GroundworkStorageTransaction`? — OPEN, owned by the Groundwork owner.** v1 gave the authority document-level concurrency; v2 is row-based with a session/transaction model. The `TakeOver` intent and revision check must be re-expressed in that model rather than transliterated, and whether v2 offers the primitive is not answerable from outside the package. Tasks depending on this are marked blocked.

**4. Does the frozen-EF-oracle ratchet still apply? — WITHDRAWN; the question conflated two things.** The ratchet covers `Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore` — identity EF Core, not the Groundwork design lane — and Groundwork v2 does not touch it. `main` has modified neither the ratchet, its baseline, nor that csproj, so T153 and its baseline update stand unchanged and merge cleanly.

## Recommended sequencing

1. Answer (1) and (3) — they determine the shape of everything in B.
2. Verify A: diff each already-ported v2 store against our v1 to find missing spec-151 deltas. Cheap, and it sharpens the estimate.
3. Build B against the trigger-binding template.
4. Re-express C, with T117 last — it is the part most likely to need a conversation rather than code.
5. Rebase E, deleting rather than porting anything in D.
6. Acceptance: both e2e suites, then the deployment-mode suites per `docs/agents/main-integration.md`.

## Note on urgency

`main` moved 8 commits in roughly two days, and those commits are what created this port. The scope grows with every day the PR stays open — this is the concrete argument for prioritising its review.
