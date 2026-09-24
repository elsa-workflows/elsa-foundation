# Profile selection planner v1 contract

Status: specification contract for [#1984](https://github.com/elsa-workflows/elsa-foundation/issues/1984), not a published catalog schema or runtime feature. Read with the [feature specification](../spec.md) and the [source investigation](../../../docs/reports/runtime-composition/profile-catalog-contract.md).

## Documents and results

The implementation plan may choose JSON property names and storage paths, but these semantic fields and distinctions are mandatory. Version numbers below identify immutable selection definitions, not package versions or the live shell's feature revision.

| Record | Required content | Validation and ownership |
|---|---|---|
| Catalog snapshot | Schema version, catalog ID/version/digest, reviewed profile definitions, reviewed flat group definitions, publisher identity | Foundation publishes one immutable snapshot. The same ID/version with different digest refuses. Installed package metadata cannot add a member. |
| Profile/group definition | Kind, stable ID, immutable version, digest, unique stable feature-ID members, reviewed dependency explanations, display metadata | A profile or group has no nested selections or executable behavior. One ID cannot name both kinds; duplicate ID/version or member ID refuses publication. Display/category data cannot override membership. |
| Authored composition | Schema version, pinned catalog ID/version/digest, zero-or-one profile ref, flat group refs, explicit additions and removals, accepted exact expansion, observed per-feature locks, opaque settings/resource payload or reference | The user's intent and accepted lock are retained separately from a candidate re-resolution. A custom profile ref identifies a user-owned workspace snapshot, never a Foundation catalog definition. Unknown imported selections/settings remain visible and are not deleted by planning. |
| Host inventory snapshot | Target identity and observation time, loaded feature IDs and runtime dependency edges, installed package ID/version and manifest digest/read status, manifest dependency edges when runtime evidence is absent, host/package compatibility observations and their source | Supplied by the caller. A package manifest proves only the observed installed package, not feed availability or activation. Missing evidence remains missing. |
| Plan result | Exact sorted selected IDs and selection/removal provenance, catalog/inventory identities, observed feature locks, required unresolved edges, optional companion advice, unavailable/incompatible findings, evidence source, optional supplied effective-persistence context, candidate upgrade diff | Side-effect-free result for command and UI. Selection computation and host assessment are distinguishable; neither constitutes runtime-ready status. |

A definition reference consists of origin (`foundation` or `workspace`), kind, ID, version, and digest. Foundation refs resolve against the pinned catalog; a custom profile ref resolves against a user-owned immutable workspace document with the same digest rules. A feature lock consists of stable feature ID plus either observed package ID/version/manifest digest **or** an explicit host-bundled marker. If neither identity is available, the lock is unresolved. The composition records the exact accepted expansion and catalog digest even if it later imports into a host that cannot resolve all those IDs. The planner may produce a *candidate* expansion but does not replace the accepted expansion until a separate save flow accepts it.

## Digest and publication semantics

Use `sha256-jcs-v1`: lowercase hexadecimal SHA-256 of the UTF-8 bytes emitted by [RFC 8785 JSON Canonicalization Scheme](https://www.rfc-editor.org/rfc/rfc8785.html) for a normalized semantic projection. The digest value itself is excluded from that projection. Reject duplicate JSON keys and non-I-JSON inputs; do not normalize Unicode strings beyond JCS. The projection contains all owned definition fields, including kind, ID, version, members, reviewed dependency explanations, and owned display metadata. It never contains secrets, connection values, mutable shell state, inventory observations, or live feature revision.

Before JCS, treat member IDs and top-level profile/group collections as sets: reject duplicate IDs, sort their elements by stable ASCII ID and version, and sort explanation records by feature ID, dependency ID, and mode. Member or definition declaration order and file formatting therefore do not affect a digest; a change to reviewed content does. Profile/group digests are computed over their own normalized definitions. The catalog digest is computed over its ID/version/schema version, publisher identity, and normalized full definition collections, including each definition digest. A new content digest for the same published ID/version is an error, not a silent catalog upgrade. A changed definition requires a new immutable version. An authored composition pins the definition and catalog digests independently so a mismatched import is diagnosable.

The first implementation plan must provide cross-runtime canonicalization vectors for ASCII IDs and non-ASCII display/rationale text, plus a tampered-digest case. Do not substitute ordinary serializer output for JCS. Digest matching establishes snapshot integrity, not trust or provenance; distribution/signing policy remains outside this v1 planner.

The following definition-projection vectors were independently generated with ECMAScript `JSON.stringify` over recursively UTF-16-sorted object keys (Node.js 25.8.0). They use only strings/arrays/objects, the full v1 definition projection domain. The `digest` field is absent from both inputs. A parser must reject either vector if supplied with a digest differing from the listed SHA-256 hex value.

| Vector | Canonical UTF-8 JSON | SHA-256 hex |
|---|---|---|
| ASCII | `{"dependencyExplanations":[],"description":"Planning fixture only","id":"foundation-core","kind":"group","members":["Events","Primitives"],"rationale":"Common core","title":"Foundation core","version":"1"}` | `a22f7d341423057ec2a3931ab19b38e60d7bc0296ec93618bc391a3d8e799158` |
| Unicode | `{"dependencyExplanations":[{"dependencyId":"B","featureId":"A","mode":"optional","reason":"Éviter les surprises"}],"description":"Δ workflow","id":"unicode-demo","kind":"group","members":["A","B"],"rationale":"Café ☕","title":"Grüße","version":"1"}` | `44de77def0d72ef7c1d8a4b6ba0e7a04724d26c905cca9482df7ec5491b52e52` |

## Expansion and evidence rules

1. Verify schema version and all pinned catalog/profile/group IDs, versions, and digests. A mismatch is unresolved and must not be silently upgraded.
2. Start with profile members if a profile is selected; union all flat group members; union explicit additions; subtract explicit removals. The result is a stable sorted set of exact feature IDs. Keep provenance for every inclusion and exclusion, including overlapping sources.
3. Compare the result with the accepted expansion. A difference is a candidate re-resolution, not a rewrite. Validate dependencies only after this set calculation.
4. For loaded features, use the runtime descriptor's dependency list as authoritative required edges, even when empty. For unloaded features, use manifest edges and their optionality only when available, and mark their source. Show runtime/manifest disagreement without merging the two silently.
5. A missing required target is unresolved and is never added back by the planner. An optional target is advice only. Preserve unknown IDs and missing or incompatible package observations in the plan. The host's later `DependsOn` Fail-Fast or Auto-Resolve mode can change activation behavior; this pure plan does not claim to be the final runtime-installed feature set.
6. Attach supplied effective-persistence evidence separately. If none exists, state that provider, connection, schema, migration, and physical-layout behavior were not checked. Never derive those settings from group membership.

## Contract examples

The following fixtures use the proposed group contents in the [decision record](../../../docs/reports/runtime-composition/profile-catalog-contract.md#evaluation-fixtures-not-released-profiles). They test **exact pre-dependency-validation expansion**, not released profiles, loadability, or useful host behavior.

| Fixture | Selection | Exact expanded feature IDs |
|---|---|---|
| Embedded | `foundation-core` + `runtime-base` | `ActivitiesControlFlow`, `ActivitiesPrimitives`, `ActivitiesRuntime`, `ActivitiesSequence`, `Events`, `Expressions`, `Mediator`, `Primitives`, `Serialization`, `WorkflowsRuntimeEntityFrameworkCore`, `WorkflowsRuntimeResumption`, `WorkflowsRuntimeTriggers` |
| Authoring | `foundation-core` + `authoring-base` | `ActivitiesDesignApi`, `ActivitiesDesignEntityFrameworkCore`, `ActivitiesDesignReconciliation`, `ApiCapabilities`, `ClrActivityReconciliation`, `Events`, `Expressions`, `Mediator`, `Primitives`, `Serialization`, `WorkflowDesignValidations`, `WorkflowsDesignApi`, `WorkflowsDesignEntityFrameworkCore`, `WorkflowsPublishing`, `WorkflowsPublishingApi`, `WorkflowsPublishingEntityFrameworkCore` |
| Worker | `foundation-core` + `runtime-base` + `worker-http` | `ActivitiesControlFlow`, `ActivitiesPrimitives`, `ActivitiesRuntime`, `ActivitiesSequence`, `ApiCapabilities`, `Events`, `Expressions`, `Mediator`, `Primitives`, `Serialization`, `WorkflowsRuntimeApi`, `WorkflowsRuntimeEntityFrameworkCore`, `WorkflowsRuntimeResumption`, `WorkflowsRuntimeTriggers` (14 unique IDs) |
| Custom | User-owned snapshot of Worker's 14 IDs + `diagnostics-ef` - `WorkflowsRuntimeApi` | `ActivitiesControlFlow`, `ActivitiesPrimitives`, `ActivitiesRuntime`, `ActivitiesSequence`, `ApiCapabilities`, `DiagnosticsOpenTelemetry`, `DiagnosticsOpenTelemetryEntityFrameworkCore`, `DiagnosticsStructuredLogs`, `DiagnosticsStructuredLogsEntityFrameworkCore`, `Events`, `Expressions`, `Mediator`, `Primitives`, `Serialization`, `WorkflowsRuntimeEntityFrameworkCore`, `WorkflowsRuntimeResumption`, `WorkflowsRuntimeTriggers` (17 unique IDs) |

Use synthetic IDs in negative vectors so they do not assert nonexistent Elsa dependency edges:

| Vector | Input | Required result |
|---|---|---|
| Overlap and removal | Profile `{A,B}`, group `{B,C}`, add `D`, remove `B` | Exact set `{A,C,D}`; `B` carries both inclusion sources and explicit removal provenance. |
| Required removed | Selected `A`, runtime edge `A -> B` required, remove `B` | `B` remains absent; unresolved required edge identifies runtime source and removal. |
| Optional companion | Selected `A`, manifest edge `A -> C` optional, no runtime descriptor | Exact set remains `{A}`; `C` is advisory with manifest source. |
| Unknown feature | Explicit addition `X` missing from inventory | Exact set retains `X`; availability unresolved. No invented package pin. |
| Package evidence | Selected package feature has unreadable manifest, absent package, or known incompatible version in three separate inventories | Each case is unresolved with its own evidence source. An installed manifest does not imply feed availability or compatibility. |
| Empty descriptor | Loaded `A` has an empty runtime dependency list; manifest lists `B` | No required `B` is inferred; manifest divergence is reported. |
| Catalog upgrade | New `A` profile version adds `C` and changes rationale; old composition remains pinned to prior digest | Candidate diff shows `C`, rationale change, package and settings/resource impact or explicit unverified impact; old accepted expansion is unchanged. |

The planner tests must also prove no file save, package fetch, database call, or host activation is performed. Rebuilt-host dependency closure and exact API surface for the three candidate starting points belong to a later release gate.

## Concrete v1 document fields for #1986

The following field names make the semantic contract implementable. All version and schema values are strings, so no number enters the JCS digest projection. JSON object keys outside the listed catalog/definition schema refuse import rather than being ignored by a hash. An authored composition may carry unknown content only beneath `settings` and `resources`, which the planner neither projects into digests nor echoes in its result.

```json
{
  "schemaVersion": "1",
  "id": "foundation-sample",
  "version": "1",
  "publisher": "elsa-foundation",
  "digest": "<64 lowercase hex characters>",
  "profiles": [],
  "groups": [
    {
      "kind": "group",
      "id": "foundation-core",
      "version": "1",
      "digest": "<64 lowercase hex characters>",
      "members": ["Events", "Primitives"],
      "rationale": "Common explicit choices",
      "title": "Foundation core",
      "description": "Planning fixture only",
      "dependencyExplanations": []
    }
  ]
}
```

Each `dependencyExplanations` row has string fields `featureId`, `dependencyId`, `mode` (`required` or `optional`), and `reason`. It is reviewed rationale, never a substitute for target-host dependency evidence. The definition digest excludes its own `digest`; the catalog digest excludes its own `digest` but includes every normalized definition and its digest. Normalization sorts `members` by ordinal feature ID, `profiles`/`groups` by ordinal ID then version, and explanations by ordinal feature ID, dependency ID, mode, then reason. Duplicate members, explanations with the same feature/dependency/mode tuple, and duplicate definitions of one kind/ID/version refuse publication. A stable ID may have multiple versions of one kind but may not be both a profile and group. Every required string is nonempty after parsing; strings are not trimmed or Unicode-normalized for hashing.

A workspace profile document uses `schemaVersion`, `kind: "profile"`, and the same definition fields plus `digest`. Its reference origin is `workspace`; workspace ownership and path are controlled by the caller, not read by this library.

A standalone document can prove that its content matches its embedded digest, but it cannot discover an earlier publication by itself. Same-ID/version changed-content refusal requires a prior pinned catalog/profile snapshot or authored reference supplied for comparison; Foundation publication review must retain that prior snapshot. A planner presented with only the new self-consistent document reports historical publication identity as unverified rather than claiming it proved immutability.

An authored document has `schemaVersion`, a `catalog` object (`id`, `version`, `digest`), `profile` (null or a definition ref), `groups` (definition refs), `add` and `remove` string arrays, `accepted` (`catalogDigest`, `featureIds`, `locks`), and optional opaque `settings` and `resources` objects. Definition refs use `origin`, `kind`, `id`, `version`, `digest`. A lock uses `featureId`, `kind` (`package` or `hostBundled`), and for `package` the `packageId`, `packageVersion`, and `manifestDigest`. Imported accepted IDs and locks remain a historical baseline even when the target inventory cannot resolve them. Repeated `add`/`remove` IDs and group refs are refused as duplicate authored intent; overlap between an addition and removal is valid and removal wins.

The supplied typed host inventory carries `inventoryId`, `targetId`, `observedAt`, and source; each feature row carries a stable `featureId`, `availability` (`loaded`, `installed`, `absent`, or `unknown`), nullable `runtimeDependencies` (null means no descriptor; empty means an observed descriptor with no edges), nullable `manifestDependencies` with `optional` flags, `manifestReadStatus`, explicit `package` observation or `hostBundled`, and sourced compatibility (`compatible`, `incompatible`, or `unknown`). Duplicate feature rows or dependency IDs refuse inventory input. An installed manifest never proves remote availability or activation.

The typed plan result exposes sorted `selectedFeatureIds`, selection/removal reasons, observed locks, catalog/inventory identities, and findings with stable `code`, `severity` (`unresolved` or `advisory`), affected feature/edge, evidence source and explanation. It has no `runtimeReady` property. Its `persistence` field contains only supplied checked/unchecked status, provenance, unresolved reasons and resource reference IDs, never connection values. Explicit re-resolution returns old and candidate plans plus added/removed IDs and changed reasons, dependency findings, locks, and settings/resource impact (or `unverified`).

Refusal codes for malformed documents include `schema-unsupported`, `duplicate-json-key`, `invalid-unicode`, `unknown-catalog-field`, `duplicate-definition`, `duplicate-member`, `duplicate-explanation`, `duplicate-authored-selection`, `digest-mismatch`, and `invalid-field`. Unresolved plan finding codes include `catalog-pin-unresolved`, `definition-pin-unresolved`, `required-dependency-missing`, `feature-unknown`, `package-absent`, `manifest-unreadable`, `package-identity-missing`, `compatibility-unknown`, `package-incompatible`, and `persistence-unverified`. Advisory codes include `optional-companion` and `descriptor-manifest-divergence`. The implementation may add more specific codes without weakening these distinctions.
