# Data Model: Consumer Contract Fragments

**Feature**: 149-consumer-contract-fragments | **Date**: 2026-08-08

All types below are tool-side models (`tools/contracts/Elsa.Contracts.Generator/FragmentModels.cs`) serialized deterministically (research R7). They deliberately mirror the served catalog's content fields — the same facts, minus server state.

## ContractFragment (one per contributing assembly)

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | string (semver) | Self-declared fragment schema version, `"1.0.0"` initially. Additive evolution only within a major. |
| `assembly` | string | Assembly simple name; also the fragment file name stem and embedded-resource identity (`elsa.contract.json`). |
| `features` | FeatureContract[] | Feature metadata for every `[ShellFeature]` in the assembly. |
| `activities` | ActivityContract[] | CLR activity contracts minted by the shared scanner. |
| `structures` | StructureContract[] | Structure kinds registered by this assembly's features. |
| `expressions` | ExpressionSurface? | Expression-language contributions; omitted when none. |
| `intrinsics` | IntrinsicContract[] | Only in the engine/design-api fragment (built-in authoring descriptors). |

Ordering: every array ordinal-sorted by its identity key (feature id, activity type key, structure kind, etc.) — determinism requirement.

## FeatureContract

| Field | Type | Notes |
|---|---|---|
| `id` | string | `[ShellFeature]` name — the stable feature identity (framework §2.19); the consumer's filter key. |
| `displayName`, `description` | string? | From the attribute. |
| `dependsOn` | string[] | Dependency closure as structural data (RFC Part 1). |
| `options` | FeatureOptionContract[] | Public settable feature properties + manifest-hint metadata. |

**FeatureOptionContract**: `name`, `jsonType` (string/boolean/integer/number/object/array), `clrType` (alias), `required`, `defaultValue` (JsonElement?), `displayName`, `description`, `category`, `secret`, `restartRequired`, `advanced`, `experimental` — the `ManifestHintReader` projection, except `defaultValue` which is **static-only** (explicit attribute default → compiled initializer constant → synthesizable `default(T)` → null): instance-read defaults embed the generator's environment and broke fragment determinism (discovered via the first CI run — e.g. `LocksFolderPath = Path.Combine(Environment.CurrentDirectory, …)`).

## ActivityContract

Mirrors `ActivityAuthoringDescriptorView` content fields; excludes overlay (`ActivityVersionId`, `Available`, `AvailabilityReason`, `Provenance`) and server-generated template boilerplate.

| Field | Type | Notes |
|---|---|---|
| `featureId` | string? | Owning feature via the shared attribution rule; null if assembly hosts no feature. |
| `activityTypeKey` | string | CLR FullName (scanner-minted, stable identity). |
| `version` | string | SemVer from assembly version / `[Version]` override, **build metadata stripped** (the informational version carries the SourceLink commit sha, which would make committed fragments change every commit; identity is metadata-insensitive per §E2.8). |
| `contentHash` | string | `DefaultActivityDefinitionHasher` canonical hash — equals the persisted row `Hash` for identical content. |
| `displayName`, `category`, `description` | string? | Scanner-minted. |
| `executionType` | string | Always `Action` today (scanner invariant). |
| `inputs` | InputContract[] | See below. |
| `outputs` | OutputContract[] | See below. |
| `ports` | PortContract[] | From the outcomes design facet (`name`, `displayName`, `type`, `isBrowsable`, `referenceKey`). |
| `containerStructure` | JsonElement? | Structure design-facet payload, verbatim. |

**InputContract** (mirrors `ActivityInputDescriptorView` + G1): `referenceKey`, `name`, `type` (alias), `collectionKind`, `displayName`, `description`, `order`, `category`, `isBrowsable`, `isRequired`, `isNullable`, `uiHint`, `defaultValue` (JsonElement?), `hasStaticDefault` (bool — distinguishes "default is null" from "no statically representable default", spec edge case), `defaultSyntax`, `uiSpecifications`.

**OutputContract** (mirrors `ActivityOutputDescriptorView` + G2): `referenceKey`, `name`, `type`, `collectionKind`, `displayName`, `description`, `category`, `isBrowsable`, `isRequired`.

## StructureContract

Mirrors `ActivityStructureView`: `featureId`, `kind`, `schemaVersion`, `supportsScopedVariables`, `payloadSchema` (JsonElement? — explicitly null when the owner publishes no payload type: opaque by choice).

## ExpressionSurface

| Field | Type | Notes |
|---|---|---|
| `descriptors` | ExpressionDescriptorContract[] | `type`, `displayName`, `description`, `editingMode` — same data as `expressions/descriptors`. |
| `javaScriptDeclarations` | JsDeclarationContract[] | Per contributor: `contributor` (type name), declared functions/types/variables captured from a fresh declarations context. |
| `scriptSandbox` | SandboxGlobalContract[] | From the declarative Jint sandbox catalog (research R10): `name`, `kind` (`function` \| `frozenObject` \| `perVariableAccessor`), `signature?`. |

## IntrinsicContract

Mirrors the intrinsic authoring descriptors: `typeKey` (e.g. `Elsa.SetVariable`), stable descriptor id (e.g. `elsa.intrinsic.set@1`), `displayName`, `category`, `intrinsic` block (`kind`, `valueInputKey`, `variableInputKey`, `outputNameInputKey`), inputs/outputs as InputContract/OutputContract.

## ContractsManifest (`docs/contracts/manifest.json`)

| Field | Type | Notes |
|---|---|---|
| `schema_version` | string | Manifest schema, `"1.0"`. |
| `generator` | string | `tools/contracts/Elsa.Contracts.Generator`. |
| `fragments` | { name → `sha256:<hex>` } | Per-fragment file fingerprint — the consumer's "matches my pinned commit" string compare. |
| `submit_schema` | `sha256:<hex>` | Fingerprint of `submit-schema.json`. |
| `hosts` | `sha256:<hex>` | Fingerprint of `hosts.json`. |
| `counts` | object | fragments, features, activities, structures — advisory. |

Unlike `docs/maps/manifest.json`, this manifest is **included** in check-mode comparison: fingerprints are contract.

## HostsIndex (`docs/contracts/hosts.json`)

The third term of consumer availability (`fragments ∩ shells.json ∩ hosts.json[host]`), added after consumer validation of `207326e0c`. One entry per host under `src/Apps`: `host` (assembly name) and `fragments` (ordinal-sorted names of the fragments that host actually contains). Read from the host's `.deps.json` — regenerated per build, so unlike a bin-directory listing it cannot be polluted by assemblies left over from another branch.

A fragment describes what an assembly contributes *if present*; it never asserts that a host ships it. Without this index a consumer reads a fragment correctly and still wrongly concludes the feature is enableable — the shell only reports the mismatch as `requested N feature(s) that are not available in the runtime feature catalog`.

## Validation rules

- A fragment with zero contributions is never written/embedded (absence is meaningful).
- Merge fails (exit ≠ 0) on any unreadable/duplicate fragment — no partial contract set.
- `defaultValue` must be wire-form (camelCase/string-enum) — asserted by the G1 unit tests against the served catalog's serializer options.
- Every array deterministic-ordered; byte-identical double-emit is a test gate.

## State transitions

None — fragments are immutable build outputs; drift handling is regenerate-and-commit enforced by CI `check`.
