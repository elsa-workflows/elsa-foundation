# Proposed release bundle contract

**Status:** Proposed; no production wire format or artifact profile is approved by this document.
The owner requires native, BPMN and ETL workflows plus their supported required dependencies in the first complete delivery; model-specific codecs remain design work in [the spec](../spec.md).
This contract applies FR-001–006, FR-024 and FR-027–029; it does not change publication/start authority.

## Existing boundaries

- [WorkflowArtifactClosure](../../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowArtifactClosure.cs) is a single-root, native-workflow envelope. Its source references/trigger rows are provenance, never target rows to copy.
- [ClosureFactory](../../../Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/WorkflowArtifactClosureFactory.cs) walks exact workflow edges; it does not export separately stored activity templates/catalogs or typed model material.
- [Reusable templates](../../../Essentials/src/Workflows/Runtime/Core/Models/ReusableActivityRuntimeModels.cs) have their own exact direct/closed dependency identities. Inlining their nodes into a workflow does not supply their portable catalog records.
- Git distributes authored state, but its reconciliation handler derives version IDs. Promotion imports exact retained identities; it does not use Git recompile/reconciliation as the executable import operation.

## Immutable manifest

The portable unit is one `manifest.json` and content-addressed payload blobs; display metadata and evidence are separate attachments.
The version-1 manifest has these required fields; no field is inferred from a filename, target catalog or mutable label.

| Field | Meaning |
|---|---|
| `format` / `formatVersion` | `elsa.workflow-release` / integer `1`; part of the digest preimage |
| `profile` | Explicit supported artifact profile and version; initial complete profile covers native, BPMN and ETL with supported reusable dependencies |
| `roots` | Set of selected exact typed workflow/model/pipeline version/artifact references, with explicit activate-serving or make-available intent owned by that kind |
| `members` | Complete set of typed immutable records, each with a unique member key, exact identities, codec/version, payload digest and byte length |
| `edges` | Complete typed direct dependency edges between member keys, retaining occurrence/dispatch-node references |
| `prerequisites` | Explicit package/provider/runtime/schema/storage/resource requirements and affected member/occurrence keys |
| `resourceRequirements` | Stable logical requirement keys, resource kind/schema and originating member/input locations; no target values |

Member keys are structured `(kind, identity)` pairs, compared ordinally without case folding, Unicode normalization or concatenation ambiguity.
Workflow roots retain `definitionId`, `definitionVersionId`, owner-version value, `artifactId` and full `artifactHash`.
Version encoding is owner-specific and tagged: workflow SemVer is not substituted for ETL's positive integer version.
Selected-version association is separate from the compiled artifact's producer metadata: content-addressed storage can
retain the first producer's executable row for several selected source versions. Verify each selected-version-to-artifact
association from source publication evidence without requiring those source IDs to equal the artifact producer's IDs.
Preserve the artifact's full runtime pin identity independently; do not reconstruct it from a selected root's labels.
Members retain original immutable authored-state/version identity and content, compiled workflow bodies, reusable contracts/provider manifests, catalog version/publication projections and exact template IDs/hashes as applicable.
Reusable edges retain definition/version/SemVer, template ID/hash and occurrence origin; workflow dispatch edges retain exact child ID/hash and dispatch node IDs.
Catalog records and compiled material are linked explicitly; equal labels or an available newer dependency never satisfy an exact edge.
Original content-authority/provider/source identity and read-only provenance travel with their owner-defined immutable record; importing does not manufacture an editable draft.
Target placement and separately generated control/projection database keys, activation/slot IDs, mutable head/recommendation/lifecycle state and target publication timestamps are outside these immutable records.
An existing immutable owner payload's retained creation data is preserved; release creation time/display labels are separate metadata.
Separately exported source-control/reference records are non-authoritative provenance, never IDs the target must reuse.
Source-local reference IDs and physical incarnations already embedded in tested executable/model payloads remain byte-identical
hashed content. They are inert lineage resolved by the captured owner-validated target locator context; do not strip or rewrite them.
Catalog/version/publication blobs use a versioned portable owner projection, not a database entity copy with local reference
IDs and lifecycle timestamps. Each codec defines exact immutable equality. Existing equal hashes with conflicting runtime
pin/producer identities are named identity conflicts, never silently overwritten or accepted as equivalent by label/hash alone.
Transport bytes remain retained even when an owner materializes a target-local projection; no target row mutation changes the release.

Only startable roots explicitly marked for activation contribute target serving changes. ETL catalog roots become available
for normal exact `RunEtl` invocation; they do not gain an invented direct-start selector. Direct-BPMN roots require an
owner-managed Runtime serving-selection bridge consistent with ADR 0043 before that profile can be declared complete.
Other workflow/model members are dependency
material and acquire no independent start endpoints, schedules or slot ownership by import. Retained dependency invocation
uses its existing model/runtime authority. A closure cannot silently promote every workflow dependency to an activation root.

Resource requirement keys are structured exact member identity plus authored origin/placement path, input/reference identity
and resource kind. Preserve reusable/model scope through placement and cross-workflow dependency edges. Equal names are not
implicit aliases; deliberate compatible sharing is explicit. Unresolvable dynamic/ambiguous requirements are refused unless
their owner supplies an explicit bounded requirement/resolution contract. Module-owned opaque code/package prerequisites do
not imply that arbitrary external I/O was discovered or integration-tested.

## Digest and encoding

The transport foundation privately pins the exact recursive recipe used by the existing [canonical value hasher](../../../Essentials/src/Workflows/Publishing/Core/Services/ExecutableActivityTemplateBehaviorHasher.cs), avoiding that project's Design-bearing dependencies. It defines no new JSON-equivalence algorithm.
`releaseDigest` is the same canonical-value SHA-256 recipe over the normalized complete manifest only; the digest is carried outside that preimage.
The manifest is closed-schema JSON containing explicit string/integer/boolean/null/object/array values and no opaque embedded payloads, floats or inferred defaults.
The existing writer recursively orders object properties with `StringComparer.Ordinal`, preserves array order, emits compact UTF-8 through `Utf8JsonWriter`, and returns lowercase `sha256:<64 hex>`.
Producer normalization sorts roots/members by structured member key; edges by `(from, relation, occurrence, to)`; prerequisites/resource requirements by their explicit keys. Duplicates are rejected before hashing.
Semantically ordered data, such as child occurrence order, stays inside owner payloads or explicit edge fields; set sorting must not reorder executable behavior.
Golden vectors must pin Unicode escaping, null/absence, integer rendering and ordering on supported producer/consumer runtimes before implementation freezes version 1.

Each blob is the exact UTF-8 output of its declared, versioned owner export codec, with no BOM or appended newline; `payloadDigest` is SHA-256 of those bytes using the same lowercase prefixed representation.
Import verifies bytes before decoding and preserves them; it never generic-sorts opaque JSON, upcasts, recompiles or reserializes a tested blob into different release content.
The [payload serializer](../../../Essentials/src/Serialization/SystemText/Services/JsonPayloadSerializer.cs) intentionally preserves opaque JSON ordering; the Git whitespace transform is not a substitute for an owner codec.
Owner behavioral hashes are independently verified using their owning workflow/template algorithms; a blob digest does not replace artifact/template identity or repair historical hash coverage.
Transport compression, archive entry order, manifest indentation, external display metadata, evidence, target bindings and attempt history do not change the release digest.

## Closure, prerequisites and import

1. Resolve selected exact published roots on the source; reject draft/test-only provenance. Walk every supported workflow, reusable-template and owner-declared required edge.
2. Verify every exact identity/hash, authored/catalog/compiled association, edge and owner codec. Shared members deduplicate only when their complete immutable records agree.
3. Require all reachable content in the bundle; reject unrelated extra members, missing edges/members, hash conflicts and cycles with a located dependency path. Never complete a broken bundle from target state.
4. Validate package IDs/version constraints, provider identity/fingerprint, consumer/schema versions, CLR aliases, storage drivers and resource kinds against discovered target capabilities. Constraints are explicit structured fields; an unknown comparison scheme is unsupported.
5. Native, BPMN and ETL adapters must each provide complete export, identity verification, exact import and retention before the first delivery is complete. Fail closed for other unsupported module/agent material. A generic native wrapper is not evidence of model support; temporary refusal during staged development is not final acceptance.
6. Report independently managed trigger/resource ownership separately. A requirement does not grant this release lifecycle authority over that resource or permission to deploy #3582.
7. Compare every immutable target identity against supplied content before writes. Any conflicting definition/version, activity type, template or artifact identity blocks; there is no automatic renumbering, rewrite or substitution.
8. Stage through the owning persistence bridges, retaining original source/version/template identities. Runtime stores consume execution material; Runtime must not depend on authoring Design to execute it.
9. Record per-owner durable staging outcomes and retention roots before activation. Bundle validity is environment-independent; staging/activation/readiness remain distinct target operations with recoverable partial outcomes.

Release creation holds required source retention while reading and durably captures the complete validated manifest/blob
set before exposing the release. A later download reads that retained bundle, not a lazy reconstruction from catalogs/GC.
Partial capture is not a created release. Source roots may be released after durable capture; release-store retention then
protects the downloadable content independently of runtime artifact GC. Target rollback and active-execution roots remain separate.

No import installs packages, upgrades providers/servers, migrates schemas, transfers secret values or rewrites logical resource requirements with target names.
Binding captures select authorized target resource incarnations separately; different bindings can consume the same immutable manifest/blobs.

## Portable input validation

Version 1 supports a directory or ZIP transport with exactly `manifest.json` and `blobs/<lowercase digest hex>.json`; optional attachments use a separate enumerated attachment directory.
Read without extracting untrusted paths: reject rooted paths, drive prefixes, backslashes, empty/dot/dot-dot segments, symlinks/hardlinks/devices, encrypted entries and duplicate/case-colliding normalized entry names.
Reject unknown manifest/profile versions, unknown manifest fields, duplicate JSON property names at every depth (including decoded owner payloads), invalid UTF-8, comments/trailing commas, duplicate identity keys and unlisted executable blobs. Owner semantic validation separately rejects unknown owner codec names/versions; structural transport parsing never claims those adapters exist.
Advertised target limits cover manifest/blob/total expanded bytes, entry/member/edge counts, nesting and dependency depth; preflight and apply use the same limits and report a named refusal before target mutation.
Proposed defaults: 1 MiB manifest, 32 MiB per blob, 256 MiB total expanded bytes, 10,000 entries/members, 100,000 edges, JSON depth 128 and dependency depth 128. Apply enforces streaming byte/count bounds against dishonest ZIP lengths; larger releases require explicitly configured target limits.
Validation inspects all required content before staging; hostile/malformed transport never creates an activatable prefix. Original inputs remain unchanged after refusal.

## Separate evidence association

An append-only association names the release digest, evidence/report identity and digest, actual frozen candidate/closure identities, and the exact workflow roots or configured activity occurrences covered.
Where applicable it retains suite revision/manifest digest and frozen case revision/content digests; latest saved cases are resolved once by the runner, never re-resolved when evidence is attached.
It records controlled/integration mode, execution status separately from assertion verdict, timestamps, target authority/tenant, non-secret binding snapshot and prerequisite observations, cleanup and completeness.
Subset evidence cannot imply whole-release coverage; a captured-draft result cannot be relabeled an exact published release merely because one behavioral hash matches.
Association verifies the tested immutable subject/closure against covered release members without assuming equal definition labels or substituted dependencies. Unsupported/unverifiable linkage remains explicit.
Failed, skipped, cancelled, unavailable, incomplete, stale or mismatching evidence remains visible. Controlled substitutes prove their configured checks, never real target integration.
Integration evidence belongs to its observed target/bindings/runtime context; another environment requires its own evidence where an optional policy demands it. No universal approval/publication gate is added.
Exact-candidate reports may be associated before the complete #3588/#3892 saved-suite runner ships; their origin/trust is reported rather than implied by an upload.

## Common structural envelope recipe (T006/T007)

This implemented foundation captures transport and checks the **declared** graph only. It does not create a durable release,
verify owner content identities or selected-version associations, discover omitted dependencies, check installed owner codecs,
prove source retention, stage catalogs or authorize activation. All native/BPMN/ETL owner gates remain required.

The current envelope profile tag is exactly `native-bpmn-etl/1`. Every object below is closed-schema: all listed properties
are required, including explicit `null`, and extra properties refuse. Integers use canonical signed-64-bit decimal spelling
without exponent, fractional part, redundant zero or negative zero; owner semantic validation imposes narrower owner ranges.
String comparisons are ordinal; identities receive no Unicode/case normalization. Unpaired Unicode surrogate strings refuse.

| Object | Required fields and structure |
|---|---|
| Manifest | `format: "elsa.workflow-release"`, `formatVersion: 1`, `profile`, `roots`, `members`, `edges`, `prerequisites`, `resourceRequirements` |
| Member key | `{kind: nonempty string, identity: nonempty string}`; owner record identity is distinct from its codec/payload digest |
| Root | `{member: key, content: key, action: "activate-serving" | "make-available"}`; selected source record and actual content can be different members |
| Member | `{key, codec: nonempty string, codecVersion: positive integer, ownerVersion, payloadDigest, byteLength: positive integer}` |
| Owner version | Explicit `null`, or `{scheme: "semver" | "integer" | "opaque", value}`; `integer` has a positive JSON integer, others a nonempty string |
| Edge | `{from: key, to: key, relation: "dependency" | "association", occurrence: string}`; empty occurrence is allowed for a non-occurrence association |
| Prerequisite | `{key: nonempty string, member: key, kind: nonempty string, identity: nonempty string, scheme: "exact" | "semver-range" | "integer-range" | "opaque", constraint: nonempty string}` |
| Resource requirement | `{key: requirement key, schemaVersion: nonempty string}` |
| Requirement key | `{member: key, origin: ordered string array, reference: nonempty string, kind: nonempty string}`; origins are nonempty path components; an empty array names the member's root scope |

`roots` and `members` are nonempty. A selected member can occur once in `roots`; different selected versions may share a
content member. Member keys, prerequisite keys, requirement keys and full edge tuples must be unique. Member and root
content keys must exist. Both edge relations participate in root reachability; every member must be reachable from the
root selected/content keys. Only `dependency` edges participate in executable-cycle and dependency-depth checks; metadata
association cycles do not assert executable recursion. Owners must verify correct relation classification and actual edges.

Normalize manifest sets before digesting, on both producer and reader: roots by selected member `(kind, identity)`;
members by that tuple; edges by `(from.kind, from.identity, relation, occurrence, to.kind, to.identity)`; prerequisites by
key. Resource keys sort by member tuple, lexicographic ordered origin components (shorter prefix first), reference, kind.
All string comparisons use `StringComparer.Ordinal`. Preserve origin array order and every owner payload byte.
Object properties use the original recursive ordinal-property writer, arrays preserve this normalized order and scalars
use `JsonElement.WriteTo` through the default compact UTF-8 writer. Digest spelling is `sha256:` plus 64 lowercase hex digits.

One pinned recipe vector has canonical text `{"items":[1,true,"\u003C"],"null":null,"\u03B2":"\u00E9"}` and digest
`sha256:ea655576dff3d172bd93d6950f51d723ffd5a4bdebb8d008824d5a1e7e018db4`.
This pins escaping/null/array behavior, not an owner artifact hash. Original manifest bytes are retained separately from
the normalized digest subject; formatted or set-reordered manifests can therefore have the same content digest.

Blob inputs and all result byte accessors are copied. JSON owner blobs require strict UTF-8, no BOM or appended newline,
and no duplicate properties/comments/trailing commas; their original ordering and bytes remain unchanged. Blob-map keys
are prefixed digests; transport filenames use the 64 hex digits without the prefix. Several members may share one blob.
Opaque attachments under `attachments/` are bounded, preserved and excluded from the release digest; no evidence verdict
is inferred from their presence. Attachment interpretation/authorization is a later owning feature.

ZIP32 input validates central/local names, encryption flags, Unix file kinds and extra metadata before entry reads.
Multi-volume/ZIP64 and link-bearing Unix/ASi extras refuse explicitly. Streaming bounds apply despite dishonest length
metadata; independent manifest/blob hash and byte-length checks still apply when the platform decoder truncates an entry.
Raw archive input has a separate 256 MiB default bound. Directory reads use no-follow relative opens and single-link regular
file checks on macOS/Linux x64/arm64; other platforms refuse `directory_platform_unsupported` until their equivalent reader
is qualified. ZIP transport is independent of that directory capability. No untrusted archive is extracted.
JSON/dependency depth is configurable from 1 through 256; defaults remain 128. All entries, including directory markers,
count against archive/directory entry bounds; total expanded bytes include manifest, distinct blobs and attachments.
