# Data Model: Reusable Activity Definitions

The model keeps authoring, reading, and execution separate. Design owns definitions, drafts, immutable versions, provider manifests, validation, and authoritative direct dependency facts. Publishing bridges Design models into Runtime-owned executable templates and workflow artifacts. Runtime owns execution state and inspection projections and never loads Design records.

## 1. Authoring aggregates

### 1.1 `ActivityDefinition`

Stable Activity Catalog identity and lineage.

| Field | Shape | Rules |
|---|---|---|
| `Id` | string | Stable definition identity. |
| `ActivityTypeKey` | string | Stable logical catalog key; immutable and tenant-scoped. |
| `TenantId` | string? | `null` denotes a global definition; otherwise tenant-owned. |
| `ContentAuthority` | `ActivityContentAuthority` | Immutable authority for the lineage. |
| `ForkedFrom` | `ActivityDefinitionForkOrigin?` | Exact source-owned definition/version used to create this independent lineage; audit provenance only. |
| `HeadVersionId` | string? | Latest successfully published version under the definition lock; not a runtime selector. |
| `RecommendedVersionId` | string? | Exact active immutable version offered for new direct selection; never inferred from head or SemVer ordering. |
| `Category` | string | Mutable picker grouping metadata. |
| `DisplayName` | string | Mutable presentation metadata. |
| `Description` | string? | Mutable presentation metadata. |
| `CreatedAt`, `UpdatedAt` | timestamps | Audit facts, not behavior. |

Invariants:

- `(TenantId, ActivityTypeKey)` is unique. Normal authoring generates the key from the display name
  plus the new definition identity unless an advanced author supplies a pre-creation override. The
  server normalizes and validates an override against its advertised prefix, pattern, maximum length,
  and collision scope; collisions fail without suffixing. The persisted key is immutable thereafter.
- Changing display metadata does not create a version and does not affect behavior hashes.
- `HeadVersionId` changes only inside successful publication.
- Exact version resolution never uses `HeadVersionId`; it exists for authoring concurrency and convenience reads.
- The first successful publication establishes `RecommendedVersionId` when no publication existed. Later publication, reconciliation, or restoration never moves or re-establishes it implicitly.
- An authorized recommendation change binds the exact head, current recommendation, target identity, and target lifecycle. Only an active version in the same definition can be recommended.
- Retiring or revoking the recommended version atomically replaces it with an exact active sibling or records an explicit no-recommendation decision.
- A source-owned definition cannot be mutated through general authoring commands.
- Fork provenance never changes content authority and never makes the new definition part of the source definition's version lineage.

### 1.2 `ActivityContentAuthority`

Immutable value object declaring who may produce content for a lineage.

| Field | Shape | Rules |
|---|---|---|
| `Kind` | enum | `Design` or `ProviderSource`. |
| `AuthorityKey` | string | Stable namespaced owner key, such as the graph authoring authority or CLR reconciliation source. |
| `SourceId` | string? | Provider/source-side lineage identity when source-owned. |

The word “custom” is deliberately absent: first- and third-party implementations obey the same framework contracts.

### 1.3 `ActivityDefinitionForkOrigin`

Immutable provenance recorded only when a definition is created by the explicit fork command.

| Field | Shape | Rules |
|---|---|---|
| `DefinitionId` | string | Exact source definition identity visible to the caller when the fork was authorized. |
| `VersionId` | string | Exact source version used for the fork. |
| `Version` | SemVer string | Denormalized audit label; `VersionId` remains authoritative. |

The origin does not create shared mutable lineage, implicit upgrade behavior, or cross-tenant access. Later source-version visibility changes do not erase the retained audit fact.

### 1.4 `ActivityDefinitionDraft`

Mutable authoring aggregate header.

| Field | Shape | Rules |
|---|---|---|
| `Id` | string | Stable draft identity. |
| `DefinitionId` | string | Owning definition. |
| `TenantId` | string? | Must equal the owning definition tenant. |
| `Revision` | long | Monotonic optimistic revision; every successful mutation increments it once. |
| `SourceVersionId` | string? | Immutable lineage to the version cloned or migrated from. |
| `Status` | enum | `Active`, `Published`, or `Discarded`. |
| `PresentationLabel` | string? | Optional non-unique author-facing label; the server-generated draft identity remains authoritative. |
| `State` | `ActivityDefinitionDraftState` | Complete desired authoring document. |
| `CreatedAt`, `UpdatedAt` | timestamps | Audit facts. |

Invariants:

- Multiple active drafts may share a `DefinitionId`.
- Draft labels are optional and need not be unique within a definition; creating or autosaving a
  draft never requires the author to invent a unique name. A label is not identity, lineage,
  provider source, or behavior-hash input.
- `SourceVersionId` never changes after draft creation.
- Full-state update and presentation-label update both require `ExpectedRevision == Revision` and
  participate in the same autosave revision stream. A successful mutation increments `Revision`
  exactly once and updates the sibling layout revision atomically, including a label-only change.
  Stale updates return a conflict and write nothing.
- Only `Active` drafts can be updated or published.
- Successful publication records the published version identity and marks the draft `Published`; rejected publication leaves it active and unchanged.
- Conflict-copy recovery requires the exact current source revision and atomically creates a new
  active draft at revision `1` from the submitted complete contract, provider, layout, and optional
  presentation label. The copy inherits definition, tenant, immutable `SourceVersionId`, and
  provider-neutral internal options; it receives a server-generated draft identity and never
  overwrites or merges the source draft.

### 1.5 `ActivityDefinitionDraftState`

Complete mutable content of a draft.

| Field | Shape | Rules |
|---|---|---|
| `Contract` | `ActivityContract` | Authoritative provider-neutral public contract. |
| `Provider` | `ActivityProviderManifest` | Opaque provider-owned source. |
| `Options` | string map | Provider-neutral authoring options only; provider-specific content stays in the manifest. |

The graph provider stores its graph source in its own manifest schema. `WorkflowDefinitionState` is not reused.

### 1.6 `ActivityDefinitionDraftLayout`

Sibling document keyed by `DraftId`, never nested into draft state or provider manifest behavior.

| Field | Shape | Rules |
|---|---|---|
| `DraftId` | string | One-to-one with the draft. |
| `Revision` | long | Updated atomically with the draft revision. |
| `Records` | layout records | Opaque visual geometry keyed by provider-authored node identity. |

Layout changes are presentation-only unless the provider reports a separate behavioral change.

### 1.7 `ActivityDraftValidation`

Derived sibling record for the latest validated draft revision.

| Field | Shape | Rules |
|---|---|---|
| `DraftId` | string | Owning draft. |
| `Revision` | long | Draft revision from which diagnostics were derived. |
| `ValidatedAt` | timestamp | Outcome time. |
| `Diagnostics` | ordered diagnostics | Structured `ActivityDiagnostic` values. |

Publication always revalidates inside its atomic transition; a stored clean validation from an older revision is never sufficient.

### 1.8 Bounded temporal management projections

Definition, draft, and version management collections return
`ActivityManagementPageView<T>` rather than unbounded relationship arrays.

| Field | Shape | Rules |
|---|---|---|
| `Items` | ordered list | Compact authorization-filtered management views only. |
| `Count` | integer | Number of items in this response. |
| `TotalCount` | long | Exact count within the same authorized query snapshot; never a global count. |
| `HasMore` | bool | Whether the same bound snapshot has another page. |
| `Continuation` | string? | Opaque scope/query/authorization-bound continuation, null on the terminal page. |
| `Snapshot` | `{SnapshotId, AsOf}` | Stable sequence-derived identity and timestamp for the management snapshot. |

`ReusableActivityDefinitionManagementView` contains only `Definition`, a bounded `Lifecycle`
summary, typed `Actions`, and `UpdatedAt`; definition detail never embeds draft or version arrays.
Drafts and versions are paged through their advertised capability relations.

Action availability is represented as ordered `ActivityActionAvailabilityView` entries:
`{Action, Allowed, UnavailableCode}`. `UnavailableCode` is null when allowed and otherwise carries a
stable privacy-safe reason code. The projection is a convenience for rendering; every mutation
rechecks authorization, authority, lifecycle, provider capability, and optimistic preconditions.

Catalog definition, draft, and version lists read provider-queryable safe summaries rather than
scanning or deserializing authoring aggregates. Each summary revision uses a durable monotonic
sequence interval `[ValidFromSequence, ValidToSequenceExclusive)`, with `long.MaxValue` denoting
the current open interval. One mutation batch advances the scope watermark once and atomically
commits the authoritative documents, closed prior summary revisions, new summary revisions, the
snapshot marker, and the watermark compare-and-swap.

The projections contain only list-safe facts: identities, tenant/global visibility, presentation
metadata, authority, status/lifecycle, provider/schema keys, head/recommendation references, counts,
normalized search text, and deterministic sort keys. Provider payloads, graph source, compiled
descriptors, contract defaults, and other protected authoring content are never projected.

List queries bind to one exact sequence and apply tenant visibility, temporal interval, search,
filters, deterministic ordering, offset paging, and exact total count in the selected Groundwork
provider. Hidden rows therefore affect neither items nor totals. A retention operation may remove
closed revisions and advances `RetainedFromSequence` atomically; reads below that floor return a
stable snapshot-expired diagnostic and never restart at the current snapshot.

## 2. Public contract and provider source

### 2.1 `ActivityContract`

| Field | Shape | Rules |
|---|---|---|
| `Inputs` | ordered `ActivityInputContract` list | Unique stable reference keys. |
| `Outputs` | ordered `ActivityOutputContract` list | Unique stable reference keys. |
| `Outcomes` | ordered `ActivityOutcomeContract` list | Unique stable reference keys; includes `Done` for the first graph slice. |
| `ContractSchemaVersion` | string | Platform public-contract schema, independent of provider schema. |

The contract is authoritative. Providers validate or compile against it; they do not own it.
Every mutable contract ingress is admitted through the activated provider-neutral type capability
catalog. A capability records the stable alias, canonical collection kinds, default editor and
presentation facts, null support, durability support, and compatible storage-driver keys.
Compatible drivers are the intersection of descriptor declarations and Runtime's activated durable
driver registry, projected through the Publishing bridge.
Unavailable facts are rejected with structured diagnostics. Immutable historical contracts retain
and return their exact stored facts even if a capability is later unavailable.
The pre-release mutable authoring contract is a clean break: it has no legacy type-alias fallback,
compatibility ingress, or workflow-definition-as-activity representation. Historical immutability
applies only to versions successfully published under this contract.

### 2.2 `ActivityInputContract`

| Field | Shape | Rules |
|---|---|---|
| `ReferenceKey` | string | Stable identity used for binding and diffing. |
| `Name` | string | Current author-facing name. |
| `Type` | `TypeReference` | Alias-based provider-neutral type. |
| `IsRequired` | bool | Absence fails when no default applies. Independent from `IsNullable`. |
| `IsNullable` | bool | Explicitly permits a present null value. Required on every canonical mutable and immutable wire shape and allowed only when the selected type capability has `SupportsNull`. |
| `Default` | `ActivityInputDefault?` | Caller-side binding template. |
| `StorageDriverKey` | string | Stable durability driver key; public boundary defaults to durable-required. |
| `Durability` | enum | `Required` in the first slice; future stronger policies may be additive. |
| presentation fields | strings/ordering/opaque UI metadata | Do not affect behavior unless explicitly promoted by policy. |

### 2.3 `ActivityInputDefault`

| Field | Shape | Rules |
|---|---|---|
| `Syntax` | string | Stable binding syntax/language key such as literal or an expression language. |
| `Value` | JSON | Literal or expression source interpreted by the existing binding compiler. |

The consuming workflow artifact stores the compiled binding. Defaults are applied only to absent caller bindings, never explicit null or present values.

### 2.4 `ActivityOutputContract`

Same stable identity, name, type, explicit nullability, storage-driver, durability, requiredness, and presentation principles as inputs. `IsRequired` means the implementation must produce the output; `IsNullable` independently determines whether the produced value may be null. A required output must be assigned and durably captured before successful boundary completion.

### 2.5 `ActivityOutcomeContract`

| Field | Shape | Rules |
|---|---|---|
| `ReferenceKey` | string | Stable outcome identity. |
| `Name` | string | Author-facing outcome name. |
| `Description` | string? | Presentation only. |
| `IsEmitted` | bool | Whether the implementation can emit it; newly emitted outcomes are breaking under the baseline. |

### 2.6 `ActivityProviderManifest`

| Field | Shape | Rules |
|---|---|---|
| `ProviderKey` | string | Stable namespaced Design provider key; never a CLR type name. |
| `SchemaVersion` | string | Provider-owned manifest schema. |
| `Payload` | opaque JSON | Stored and round-tripped by Design without universal deserialization. |

Old provider schemas remain immutable. A provider migration clones a version into a new draft and deterministically transforms the clone; it never rewrites a version.

### 2.7 Provider authoring capabilities and contract proposals

Each provider declares structured authoring metadata for every supported manifest schema: whether
the schema is authorable, exact migration sources, and required public outcomes. Registry activation
fails when this metadata is missing, duplicated, or inconsistent with the supported schema set.

A contract proposal is a read-only result bound to one exact draft revision, provider key/schema,
and canonical manifest fingerprint. It contains ordered, typed member changes and safe diagnostics,
not a replacement contract or opaque manifest. Applying explicitly selected changes reloads and
recomputes that exact proposal, validates its fingerprint and the resulting capability-catalog
contract, then atomically changes only the contract and draft/layout revision. Any stale binding or
proposal fails without writing.

## 3. Immutable publication models

### 3.1 `ActivityDefinitionVersion`

| Field | Shape | Rules |
|---|---|---|
| `Id` | string | Stable exact version identity used by workflow nodes. |
| `DefinitionId` | string | Owning definition. |
| `TenantId` | string? | Copied from definition. |
| `Version` | SemVer string | Author-selected, validated against required minimum bump. |
| `SemVerSortKey` | string | Persistence-only ordering key. |
| `SourceDraftId` | string? | Publication provenance. |
| `SourceVersionId` | string? | Immutable lineage inherited from source draft. |
| `Contract` | `ActivityContract` | Immutable authoritative contract snapshot. |
| `Provider` | `ActivityProviderManifest` | Immutable provider source retained for inspect/clone/migrate. |
| `TemplateId`, `TemplateHash` | strings | Exact Runtime template identity. |
| `SourceReferenceId` | string | Exact retained publication reference that owns the immutable version layout and artifact lifetime. |
| `ProviderFingerprint` | string | Exact provider/compiler identity used to compile. |
| `DirectDependencyCount`, `ClosedTemplateCount` | non-negative integers | Publication-time summary facts validated against the authoritative direct edges and closed template. |
| `RuntimeRequirements` | exact consumer/schema pairs | Closed Runtime activation requirements copied from the executable template for version reads and deployment preflight. |
| `PublishedAt` | timestamp | Publication fact. |
| `Lifecycle` | enum | `Active`, `Retired`, or `Revoked`; not part of template hash. |

Uniqueness:

- `(DefinitionId, SemVerSortKey)` is unique and build-metadata-insensitive according to the existing SemVer convention.
- A version is inserted only together with its template Source Reference and direct dependency edges.

### 3.2 `ActivityDefinitionVersionLayout`

Immutable sibling layout copied from the publishing draft. Publishing copies it into the activity template's Source Reference; consuming workflow publication composes these segments into its hierarchical Source Reference layout.

### 3.3 `ExecutableActivityTemplate`

Runtime-owned, content-addressed execution material.

| Field | Shape | Rules |
|---|---|---|
| `TemplateId` | string | Derived from full behavior hash. |
| `TemplateHash` | string | SHA-256 of canonical behavioral execution material only. |
| `Root` | `ExecutableNode` | Runtime-owned, purely behavioral compiled root; no publication/version identity. |
| `NodesById` | node map | Immutable flattened lookup of the template's own nodes. |
| `ResumeTargets` | target map | Template-local target identities before placement namespace. |
| `DirectDependencies` | dependency list | Exact child version/template identities with origin. |
| `ClosedTemplates` | exact template identity set | Transitively closed execution requirements. |
| `RuntimeRequirements` | consumer requirements | Stable consumer key/schema pairs required to activate nodes. |
| `ProviderFingerprint` | string | Deterministic compiler/provider fingerprint. |
| `CompatibilityMetadata` | string map | Runtime compatibility facts only. |
| `CreatedAt` | timestamp | Storage fact excluded from behavior hash. |

No source definition, definition version, draft, Design provider type, or layout is embedded in the
template. The immutable version and Source Reference bind publication identity to the behavioral template;
workflow placement stamps exact execution/source identity onto placed executable nodes.

### 3.4 `RuntimeActivityDescriptor`

Compiled node construction payload.

| Field | Shape | Rules |
|---|---|---|
| `ConsumerKey` | string | Stable namespaced Runtime consumer key. |
| `SchemaVersion` | string | Runtime-owned descriptor schema. |
| `Payload` | JSON | Interpreted only by the matching Runtime consumer. |

This replaces `DescriptorType` as durable dispatch identity. Duplicate consumer registrations for one `(ConsumerKey, SchemaVersion)` fail at activation/startup.

### 3.5 `ActivityDependencyEdge`

Authoritative immutable direct dependency fact.

| Field | Shape | Rules |
|---|---|---|
| `OwnerVersionId`, `OwnerTemplateHash` | strings | Exact owning activity version/template. |
| `DependencyVersionId`, `DependencyTemplateHash` | strings | Exact required activity version/template. |
| `OccurrenceId` | string | Deterministic identity for the authored placement occurrence. |
| `NodeOrigin` | origin path | Provider-authored node and nested origin facts safe for diagnostics. |
| `CreatedAt` | timestamp | Publication fact. |

Direct edges are execution truth. Reverse and transitive queries are projections and expose their watermark.

### 3.6 Hierarchical layout Source Reference

Existing Source References remain the owner of provenance, scope, lifetime, retirement, and layout. Their layout evolves from one flat record list to boundary-scoped segments:

```text
ExecutableLayoutSidecar
└── BoundarySegments[]
    ├── BoundaryOrigin                 # invocation origin / placed outer node
    ├── TemplateHash
    ├── Records[]                      # provider-authored geometry mapped to executable ids
    └── NestedBoundaryOrigins[]        # lazy child-boundary navigation
```

The sidecar never contributes to the template or workflow artifact behavior hash.

## 4. Diff, diagnostics, dependencies, and upgrade read models

### 4.1 `ActivityVersionDiff`

| Field | Shape | Rules |
|---|---|---|
| `From`, `To` | version identity summaries | Exact compared versions. |
| `Compatibility` | enum | `Identical`, `NonBehavioral`, `Compatible`, or `Breaking`. |
| `RequiredBump` | enum | `None`, `Patch`, `Minor`, or `Major`. |
| `BehaviorChanged` | bool | Whether template hashes differ. |
| `Changes` | ordered `ActivityVersionChange` list | Stable, machine-readable explanations. |
| `Diagnostics` | diagnostic list | Provider-strengthened or comparison warnings. |

### 4.2 `ActivityVersionChange`

| Field | Shape | Rules |
|---|---|---|
| `ChangeId` | string | Deterministic within the comparison. |
| `Area` | enum | Contract, default, outcome, durability, provider, implementation, dependency, or presentation. |
| `Kind` | stable string | Added, removed, renamed, type-changed, requiredness-changed, nullability-changed, default-changed, and similar stable classifications. |
| `Subject` | contract/dependency subject | Includes member kind and stable reference key when applicable. |
| `Before`, `After` | safe projections | Public contract facts only; no protected provider payloads. |
| `Impact` | enum | Nonbehavioral, additive, or breaking. |
| `RequiredBump` | enum | Minimum bump caused by this change. |
| `Message` | string | Human-readable explanation. |

### 4.3 `ActivityDiagnostic`

| Field | Shape | Rules |
|---|---|---|
| `Code` | stable string | Provider-namespaced when provider-owned. |
| `Severity` | enum | Info, warning, or error. |
| `Message` | string | Human-readable and safe. |
| `Subject` | `DiagnosticSubject` | Definition, draft, version, template, workflow draft/version, or runtime artifact identity. |
| `Location` | `DiagnosticLocation?` | Node origin, reference key, JSON pointer, provider key, and/or dependency path. |
| `Remediation` | string? | Optional safe next action. |
| `Metadata` | string map | Allowlisted safe scalar context only. |

### 4.4 `ActivityDependencyPage`

Read response containing root identity, direction, transitive flag, authoritative/projection source, `AsOf` watermark, ordered edge/usage items, and opaque next cursor. Direct outbound pages are authoritative; incoming and transitive pages identify their derived projection.

### 4.5 `ActivityUpgradePlan`

Immutable planning snapshot, not execution truth.

| Field | Shape | Rules |
|---|---|---|
| `PlanId` | string | Stable plan token used for apply. |
| `CreatedAt`, `ExpiresAt` | timestamps | Prevent indefinitely stale plans. |
| `RequestedReplacements` | exact from/to version pairs | No “latest” selector. |
| `Roots` | selected activity/workflow draft identities | Scope approved by caller. |
| `Steps` | bottom-up `ActivityUpgradeStep` list | Proposed draft clones/updates only. |
| `ExpectedSnapshots` | revision/head list | Every touched draft revision and definition head. |
| `Diagnostics` | diagnostic list | Conflicts, cycles, retired/revoked targets, tenant denials. |

Apply revalidates every expected snapshot and either persists all selected draft edits or none. Plans never mutate published versions or workflow executables.

## 5. Runtime execution and inspection

### 5.1 No graph invocation entity

The outer `ActivityExecutionState` is the graph activity execution scope. Its `ActivityExecutionId` namespaces ordinary Durable Values:

```text
activity-scope:{outerActivityExecutionId}:input:{referenceKey}
activity-scope:{outerActivityExecutionId}:variable:{referenceKey}
activity-scope:{outerActivityExecutionId}:output:{referenceKey}
activity-scope:{outerActivityExecutionId}:boundary
```

The concrete durable-value key encoding is internal, but the ownership invariant is public: values belong to the outer activity execution, not to a separate invocation record.

### 5.2 `ActivityExecutionAttemptLineage`

| Field | Shape | Rules |
|---|---|---|
| `AttemptNumber` | positive integer | `1` for initial attempt. |
| `FirstAttemptActivityExecutionId` | string | Stable across retries. |
| `PreviousAttemptActivityExecutionId` | string? | Immediate failed/cancelled predecessor. |

Retry creates a new `ActivityExecutionId`; it does not reuse descendant state. The pinned template and effective input snapshot remain linked to the new attempt.

### 5.3 `ActivityBoundaryInspection`

Optional extension on the existing `ActivityExecutionInspectionProjection`, present for reusable activity boundaries.

| Field | Shape | Rules |
|---|---|---|
| `DefinitionId`, `DefinitionVersionId`, `Version` | strings | Exact reusable activity identity. |
| `TemplateHash` | string | Exact executed template. |
| `InvocationOrigin` | readable origin | Diagnostic provenance, not durable node identity. |
| `ExecutionScopeId` | string | Equals the outer `ActivityExecutionId`. |
| `Attempt` | attempt lineage | Retry provenance. |
| `HasChildren` | bool | Supports lazy expansion. |
| `DirectChildCount`, `CommittedDescendantCount` | counts | Summary only. |
| `Aggregate` | derived status summary | Separate from the outer lifecycle status. |
| `LayoutAvailable` | bool | Whether a pinned boundary segment can be read. |

### 5.4 Descendant execution relation

Every descendant scheduled inside a graph boundary records the nearest `ExecutionScopeId` in existing scheduling provenance. A nested `GraphActivity` is returned as a child boundary and owns a new scope for its own descendants. This makes click-through recursive without storing or returning an unbounded ancestry path.

### 5.5 `ActivityExecutionHierarchyPage`

| Field | Shape | Rules |
|---|---|---|
| `Root` | boundary summary | Outer activity being expanded. |
| `CommittedThroughSequence` | long | Stable page watermark. |
| `Items` | ordered hierarchy items | Each carries parent execution id, relative depth, lifecycle summary, boundary summary when nested, counts, and safe evidence summaries. |
| `NextCursor` | string? | Opaque, scope-bound replay cursor. |

Cursor bindings include tenant, workflow execution, root activity execution, query shape, authorization/redaction profile, and committed watermark. A mismatched, expired, or trimmed cursor returns a stable cursor diagnostic and never restarts silently from the beginning.

### 5.6 `ActivityExecutionLayout`

Separate lazy read model containing Source Reference identity, layout-selection reason, boundary origin, template hash, and records that map template/authored node identities to placed executable-node identities. It is read from the executed Source Reference only.

## 6. State transitions

### Draft lifecycle

```text
Active --publish succeeds--> Published
Active --discard-----------> Discarded
Active --update-----------> Active (Revision + 1)
Active --publish rejected--> Active (unchanged)
```

### Version lifecycle

```text
Active --retire--> Retired --re-enable--> Active
Active/Retired --revoke--> Revoked
```

- `Retired`: excluded from new direct selection; already closed parent templates remain executable.
- `Revoked`: stronger policy state evaluated at authorized dispatch/activation boundaries; it does not erase the artifact or historical inspection evidence.

### Graph activity lifecycle

```text
Scheduled
  -> entry checkpoint (captured inputs + local state + first child intent)
  -> Deferred/Running descendants
  -> Suspended | Faulted | Cancelling | Completing
  -> exit checkpoint (outputs + outcome + terminal state + parent intent)
  -> Completed | Faulted | Cancelled
```

No descendant work is scheduled before entry commits; no parent continuation observes outputs before exit commits.

## 7. Tenant and authorization invariants

- A tenant definition may reference versions owned by the same tenant or global definitions.
- A global definition may reference only global versions.
- Exact identifiers never bypass tenancy.
- Authoring authority, lifecycle administration, structure inspection, and sensitive-value inspection are distinct authorization decisions.
- Management items, lifecycle counts, `TotalCount`, and action availability are projected only
  after visibility and authorization filtering. They never reveal pre-authorization inventory.
- A `404` reports absence in the caller's authorized scope without confirming hidden existence. A
  `403` reports an operation or tenant-reference denial with a generic privacy-safe body; neither
  response includes hidden names, identifiers, counts, action maps, or provider facts.
- Error and diagnostic projections never include opaque provider payloads, compiled descriptor payloads, captured sensitive values, or unauthorized cross-tenant identities.
