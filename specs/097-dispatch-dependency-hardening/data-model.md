# Data Model: Deterministic and Bounded Workflow Dispatch

## WorkflowExecutable

The existing immutable artifact gains two behavioral members:

| Field | Type | Rules |
|---|---|---|
| `InputContract` | `WorkflowExecutableInputContract?` | New compiles always set version 1. `null` remains readable/executable for legacy compatibility but cannot be selected by a newly published strict dispatch parent. |
| `Dependencies` | read-only `WorkflowExecutableDependency` collection | Canonical, one record per distinct direct child artifact; empty when there is no executable dependency. |

`RootActivity`, `ResumeTargets`, `InputContract`, and `Dependencies` form the behavioral hash payload. Creation time, compatibility metadata, source/publication identity, access context, retirement state, and layout remain outside the hash.

### Invariants

- Collections are immutable snapshots.
- Dependency artifact IDs/hashes are nonblank and IDs are ordinally unique.
- Every dependency has at least one unique, sorted dispatch node ID present in `NodesById`.
- Loaded dependency graphs have no missing artifact, hash mismatch, or exact-artifact cycle.
- Equivalent node/input/dependency orderings produce the same hash.

## WorkflowExecutableDependency

An immutable direct edge from the parent artifact to one exact child artifact.

| Field | Type | Rules |
|---|---|---|
| `ArtifactId` | string | Exact child content identity; nonblank. |
| `ArtifactHash` | string | Must equal the stored child's immutable hash. |
| `DispatchNodeIds` | read-only string collection | Sorted unique IDs of DispatchWorkflow nodes targeting this child. |

The edge excludes live source-reference/publication identity and timestamps. The child content identity plus parent node binding is the immutable dependency provenance used at runtime. Multiple parents and multiple nodes may share one child. Transitive closure is derived, never stored as a second source of truth.

## WorkflowExecutableInputContract

| Field | Type | Rules |
|---|---|---|
| `Version` | positive integer | Version 1 here; unknown future versions fail closed. |
| `Inputs` | read-only `WorkflowDeclaredInput` collection | Sorted and unique by ordinal name. Empty explicitly means no inputs. |

## WorkflowDeclaredInput

| Field | Type | Rules |
|---|---|---|
| `Name` | string | Valid nonblank workflow-input name. |
| `Type` | shared `TypeReference` | Exact alias plus `Single`, `Array`, `List`, or `HashSet` shape. Unknown aliases fail validation. |
| `IsRequired` | bool | Missing is invalid unless a supported default exists. |
| `DefaultValue` | optional JSON | Cloned immutable literal default. Unsupported expression defaults fail publication. |

Validation rejects blank/unknown/duplicate names, missing required inputs, and incompatible JSON values. Supported literal defaults are added to the normalized input bag before checkpoint staging. Supplied values remain workflow inputs only—even for a declared input named `tenant` or `authority`—and never mutate variables, stimulus, identity, authority, tenant, partition, lineage, run kind, or depth. Diagnostics never retain raw rejected values.

## ExecutableCompilationContribution

Returned by one `IExecutableCompilationSource` during the named Sequential compile event:

| Field | Type | Rules |
|---|---|---|
| `NodeMetadata` | collection | Deterministic node metadata claims; unequal conflicts fail. |
| `Dependencies` | collection | Child artifact/node claims; unknown nodes or conflicting hashes fail. |

The Publishing-owned handler stamps source identity for diagnostics, normalizes order, and merges claims. Sources do not mutate the shared tree.

## WorkflowExecutableStartAuthority

A typed reason why a start may select an artifact:

- **LiveReference**: existing source selection, required scope, and provenance rules. Public/root starts use this.
- **RetainedDependency**: exact `ParentArtifactId`, `ParentArtifactHash`, and `DispatchNodeId`. The requested child ID/hash is allowed only when the parent dependency binds that node to that child. This provenance is persisted on the child start/state for inspection. Public endpoints cannot supply it.

No generic provenance-bypass flag is introduced.

## WorkflowExecutableStartPolicy

This is a single-implementation replacement contract. Its context contains resolved artifact identity, authority kind, requested-by/system authority, tenant, partition, run kind, and nesting depth—but no raw inputs. Its decision is either allow or deny with a stable machine-classifiable reason and safe message. Denial never mutates artifacts or terminates existing executions.

## Dispatch Nesting Lineage

`DispatchNestingDepth` is a non-negative integer carried on start request, serialized start command, checkpoint payload, execution state, dispatch record, and child-start payload.

- Root and missing legacy fields default to 0.
- Dispatch calculates child depth exactly once as parent + 1.
- Record, outbox, and child start carry that same value.
- Replay/redelivery never increments it.
- Default maximum child depth is 32; hosts configure a positive finite alternative.
- Over-limit dispatch fails before checkpoint staging and is rechecked before child materialization.

## Artifact Retention State

Roots are live source references, retained execution pins, and any existing future authoritative root kinds. The protected set is every root plus its transitive dependency closure.

### Root creation

1. Load root and closure.
2. Fail closed on missing/cyclic/inconsistent edges.
3. Acquire renewable leases for sorted distinct artifact IDs.
4. Write the durable root.
5. Release all leases.

### Collection

1. Delete retired/expired source references.
2. Snapshot artifacts and roots.
3. Compute protected closure.
4. Select unprotected artifacts outside staging grace.
5. Acquire a deletion guard.
6. Re-read roots/artifacts and recompute reachability.
7. Delete only when still unreachable; otherwise cancel.

Query, graph, lease, or guard uncertainty always resolves in favor of retention.

## Cycle Identity

Cycles compare full artifact ID/hash pairs, never definition IDs. `new A → old A` is legal when identities differ and no pair repeats. Normal publication creates a DAG; malformed stored cycles fail closed. Candidate compilation computes its full identity from nodes, inputs, and direct dependencies, then defensively rejects if that pair occurs in its closure. Diagnostics traverse ordinally and render a deterministic artifact path.
