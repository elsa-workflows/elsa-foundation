# Research: Workflow Version Override

## Decision 1: extend normal promotion rather than create another write path

**Decision**: Add the optional `requestedVersion` to `PromoteDraft` and `IPromoteDraftToVersionCommand`; continue to use `POST design/workflows/drafts/{draftId}/promote`.

**Rationale**: The route already owns normal authored promotion, requires `workflow-design.manage`, obtains the promoted immutable version, and is covered by the existing draft validation and locking contract. A distinct endpoint would create two ways to mint the same immutable entity and make authorization, replay, and validation behavior drift.

**Alternatives considered**:

- A separate `/promote-with-version` route was rejected because it duplicates the lifecycle boundary and obscures that automatic and exact assignment have identical promotion semantics.
- Direct version ingestion was rejected because it is a privileged source-ingestion path, not normal authored promotion.

## Decision 2: add a non-mutating authoritative preflight

**Decision**: Add a capability-discovered `POST design/workflows/drafts/{draftId}/promotion-preflight` operation. It accepts the same optional version-selection input, loads the current draft and latest immutable version under the definition consistency boundary, and returns a structured ready/not-ready assessment with the resolved automatic or exact candidate and current comparison baseline. It writes no version, layout, or operation marker.

**Rationale**: Studio needs prompt feedback when a user changes the requested version before invoking an irreversible promotion. The server, rather than the client, is authoritative for shared SemVer parsing, current latest version, draft validation, and identity availability. Because a later request can change the catalog, preflight is advisory; promotion must re-run the same checks inside its atomic mutation boundary.

**Alternatives considered**:

- Client-only validation was rejected because it cannot observe the authoritative latest version, deployed SemVer semantics, draft validation, or a concurrent claim.
- Returning only an error from the mutation was rejected because it makes interactive editing needlessly destructive and slow.
- Reserving the version in preflight was rejected because it would introduce a new lease lifecycle and leave abandoned reservations; the normal promotion lock and unique identity constraint are sufficient.

## Decision 3: automatic assignment remains the default policy

**Decision**: An absent `requestedVersion` invokes `WorkflowVersionNumbering.NextMajor` exactly as today. An explicit value selects exact assignment only after validation.

**Rationale**: Most authors should retain the current friction-free workflow. Exact assignment is an opt-in delivery-process need, not evidence that the server derives semantic change meaning.

**Alternatives considered**:

- Inferring major/minor/patch from the draft diff was rejected: no authoritative semantic-change classifier exists, and incorrect classification would be worse than an explicit author choice.
- Requiring every promotion to specify a version was rejected because it breaks existing clients and does not add safety.

## Decision 4: use the shared SemVer precedence and identity model

**Decision**: Trim an explicit label once, parse it with `SemVer.TryParse`, compare its sort key with the latest version's sort key, and use the sort key for uniqueness. Persist the accepted trimmed label in `WorkflowDefinitionVersion.Version`.

**Rationale**: The shared `SemVer` implementation already defines SemVer 2.0 parsing, precedence, and a persistence sort key. It intentionally ignores build metadata for equality, so `2.1.0` and `2.1.0+build.8` cannot name different immutable versions for one definition.

**Alternatives considered**:

- String comparison was rejected because it misorders numeric and prerelease identifiers.
- Treating build metadata as a distinct published identity was rejected because it conflicts with shared SemVer equality and the existing sort-key unique index.
- Preserving surrounding whitespace was rejected because it makes one semantic label have multiple stored spellings; whitespace normalization is part of this contract.

## Decision 5: preserve the established concurrency and durable replay boundary

**Decision**: Resolve and validate the selected version under the existing draft lock followed by definition lock, inside `GroundworkDesignAtomicCommand`. Include `DraftId`, assignment mode, and the normalized requested label (or an explicit automatic marker) in promotion request material. Promotion repeats all preflight checks under this boundary.

**Rationale**: The definition lock serializes competing promotions for the same definition, while the durable `designOperation` ledger returns an authoritative prior result for an identical retry. The unique `(definitionId, semVerSortKey)` index remains the final persistence defence against a race or an alternative provider implementation.

**Alternatives considered**:

- Checking a version only in preflight was rejected because another promotion can commit after the read.
- Adding a client-supplied expected-latest value was rejected for this operation; the server is authoritative and the existing lock is the correct consistency boundary.
- Treating an operation key as sufficient regardless of version material was rejected because a retry with changed intent could return a version that was not requested.

## Decision 6: capability discovery uses stable additive link relations

**Decision**: Add templated relations `workflow-draft-promote-version-preflight` and `workflow-draft-promote-exact-version` to the existing `elsa.api.workflow-design` capability declaration. The first points to the preflight operation; the second points to the existing promotion operation.

**Rationale**: The global, shell-relative `/capabilities` document is the supported discovery mechanism. Relations, rather than Studio version checks or failed endpoint probes, allow a client to show only the controls that the host can honor. Endpoints still own their authorization.

**Alternatives considered**:

- A boolean in an unrelated response was rejected because capability discovery already has a domain-owned extensibility contract.
- A new capability identifier was rejected because these are additive operations on the existing Workflow Design API, not separately composed domains.
- Always showing the UI and accepting a 400/404 was rejected because an unsupported host is a compatibility state, not invalid user input.

## Decision 7: distinguish invalid input from identity conflict

**Decision**: Preflight represents expected readiness failures in a `200` assessment. Promotion returns an invalid-request outcome for empty-after-trim, malformed, equal, or lower requested versions, and a conflict outcome for an existing or persistence-racing semantic identity. Preserve the existing 409 validation-error outcome for an invalid draft.

**Rationale**: Preflight is an assessment endpoint; invalid values are useful input to render inline rather than transport failure. Mutation needs the status distinction so automation can correct malformed/non-forward intent separately from a concurrently occupied identity.

**Alternatives considered**:

- Collapsing all mutation failures into 409 was rejected because malformed and non-forward requests are not resource conflicts.
- Returning success for a duplicate version without a matching operation replay was rejected because it could falsely claim that different draft content was published.

## Existing seams verified

- `WorkflowVersionNumbering.NextMajor` is the single current auto-assignment policy.
- `GroundworkPromoteDraftToVersionCommand` already takes both draft and definition distributed locks, reruns validation inside that boundary, and uses `GroundworkDesignAtomicCommand`.
- `IWorkflowDefinitionVersionStore` resolves latest versions and checks `SemVerSortKey`; the Groundwork storage manifest retains a unique `(definitionId, semVerSortKey)` identity index.
- `WorkflowDefinitionVersion` already stores the label and derives its normalized sort key through `SemVer.ToSortKey`.
- `WorkflowDesignApiCapabilities.StaticDeclaration` is the existing home for stable Workflow Design capability links, and the promote endpoint already uses `workflow-design.manage`.

## ADR relationship

[ADR 0050](../../docs/adr/0050-author-requested-forward-workflow-versions.md) supersedes ADR 0034 decision D2 only insofar as D2 said versions must be system-assigned. It retains D2's single-writer GitOps v1 topology: author-requested forward labels provide the necessary version admission prerequisite, but do not themselves introduce multi-writer catalog or Git-first authoring.
