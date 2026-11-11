# Agent source ownership and activity publication

**Status**: Accepted direction under [ADR 0094](../../../docs/adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md), merged #3783; source slice #3790 separately authorized. Publication and Studio follow T066/T068. Pre-GA store recreation supersedes additive legacy adoption; no live reset occurs in this leaf.

The [shared revision contract](revision-management.md) owns source/working-copy/snapshot mechanics, the Workflow/Agent mapping, publication recovery and extraction decision. This contract adds Agent authority and execution rules. Logical names are views/responsibilities, not separate Agent-specific repositories or frozen APIs.

## Ownership and identity

| Agent-facing view | Shared backing / Agent responsibility |
|---|---|
| Agent definition | Shared scoped source identity; Agent owns metadata, trusted source authority and permissions |
| Agent draft | Shared working copy/CAS; Agent owns instructions, model reference, typed contract and policy validation |
| Agent source revision | Shared immutable snapshot; exact source content/profile/provenance retained once |
| Publication candidate | Exact revision in a shared publication operation; no independent Agent candidate/versioning aggregate |
| Agent release/version | Public SemVer association to an exact revision and confirmed shared publication receipt; no duplicate publication-success flag |
| Agent activity projection | Read-only generated activity version/template/reference bound to that release |
| Agent publication status/index | Repairable view of authoritative receipt/lifecycle evidence |

One Agent-owned typed contract defines stable member keys, types/collections, null/default/required behavior and compatible JSON refinements. Activity ports and model schemas derive from that source. Agent Core does not depend on Activities Design DTOs; adapters map through owning Core contributions. Runtime uses the compiled contract inside its artifact.

Agent-facing APIs own create/edit/fork/history and permissions. Generic activity APIs refuse independent content/recommendation/lifecycle writes to derived Agent content; authorized Agent commands delegate shared primitives. Catalog visibility grants no authoring or invocation permission. Trusted services determine tenant/partition, principal and source authority; caller-authored owner/source fields cannot claim it.

The Agent collection reads authorized sources/working copies, including unpublished Agents, with server-side filtering/counting and stable paging. The workflow picker reads the one activity catalog. Configuration-owned source is read-only in Studio; authorized fork creates new source identity/provenance. Configuration and Studio use the same snapshot, provider compiler and preflight boundary. Same-release/different-content conflicts within a supported deployment.

## Publication and selection

Use the [shared operation protocol](revision-management.md#publication-and-failed-publication-recovery), not an Agent-specific state machine. Freeze the exact revision once, reference it from review/test/publication, validate schema/model/runtime/storage/dependencies, reserve authority/heads and commit through the existing publisher. No inference is required for publication. Only the shared receipt confirms executable publication; index repair never republishes it.

Agent release SemVer and its primary generated activity version match. Working-copy revisions, source snapshot fingerprints, schema versions and compiled hashes remain distinct. A new compiler cannot silently regenerate a published release. Recommendation/activation selects a confirmed publication; it cannot publish a snapshot, change pinned workflow bindings or override runtime admission. Retirement and operational revocation remain distinct permitted gates.

## Runtime and invocation

Native/BPMN workflow publication pins the exact selected template. Within a supported deployment, changing or losing authoring source does not retarget or disable its interpretation. Runtime requires no authoring database, latest-version lookup or Design/Publishing assembly. Operational model connection authorization, journal, request reservation, uncertain outcomes and accepted-versus-applied settlement remain intact.

Playground testing captures an immutable source revision and uses the same admitted path with a nonpublished execution reference. It cannot manufacture a published release. A future standalone API must resolve deployed immutable material and use the same runtime/journal semantics; it is not delivered here.

Inspection links source revision/release to publication/native identities and leads with public inputs/results. Internal model-turn handles remain diagnostic references, not business outputs. Protected-content access is unchanged by presentation.

## Pre-GA breaking delivery

Use the [reset boundary](revision-management.md#pre-ga-reset-boundary). No legacy ID association, import/backfill, mixed-schema writer transfer, old codec/API aliases or cross-break rollback support is required. The Specialist identifiers were renamed to Agent under [#4027](https://github.com/elsa-workflows/elsa-foundation/issues/4027), with providers and consumers updated together. Keep the pinned `Elsa.Agent` root. Existing demo stores are untouched by this delivery.

A future delivery records reset scope, host stop/drain, incompatible-binary exclusion, coordinated schema/package initialization and explicit source recreation/republish. No automatic reset on publication or startup failure is allowed. Normal immutable-binding, scope and crash/replay guarantees continue for all material admitted by the new deployment.

## Acceptance witnesses

| Witness | Required positive and causal negative | Planned task |
|---|---|---|
| W1 — Source authority | Agent CRUD/CAS works through typed source adapter; direct generic edit of generated activity fails | T065,T069 |
| W2 — Shared mechanics and one contract | Workflow and Agent use one revision implementation; CAS/capture races fail correctly; Agent contract derives both schemas with no second editor | T065,T066,T069 |
| W3 — Shared publication | Both adapters converge after lost replies/independently unavailable stores; changed operation material and stale authority/review are refused | T066,T069 |
| W4 — Frozen runtime | New native/BPMN artifacts retain bindings when authoring source changes/disappears; latest-source fallback is refused | T069,T070 |
| W5 — Revision/release separation | Saving draft does not allocate SemVer; rejected snapshot stays unpublished; activation cannot bypass compile/receipt or regenerate a release | T065,T066,T069 |
| W6 — Conversation recovery | Restart/retry of active/accepted turns in one deployment adds no duplicate model request, revision reset or accepted-versus-applied loss | T069,T070 |
| W7 — Reset and retention | Clean-store recreation/republish and readiness qualify; incompatible profiles fail; pinned snapshot deletion is refused; reset is never a retry | T067,T069,T070 |
| W8 — Product/configuration | Agent create/test/publish/use/inspect and configuration share the pipeline, correct permissions and canonical naming | T068,T070 |

W1–W8 remain open. #3790 qualifies only the source-storage portions of W1/W2/W5/W7; historical M0–M5 receipts do not qualify the amendment. T066–T071 remain pending under their recorded dependencies.
