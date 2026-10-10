# Research: Receive Event Correlation

## Decision: Scope correlation at the authored Event wait registration

**Rationale**: `Event.CorrelationId` is the existing authored opt-in. A stateful Event suspension
already emits a typed registration, and its metadata is copied unchanged into the durable bookmark.
Adding the optional correlation value at this boundary gives correlated delivery an end-to-end
receive key without changing other wait types.

**Alternatives considered**:

- Stamp every bookmark from workflow identity: rejected because it changes every typed wait and
  requires identity plumbing through both initial invocation and re-suspension paths.
- Change the global lookup fallback: rejected because it would weaken exact correlated delivery
  and alter all bookmark consumers.

## Decision: Normalize authored blank values to unscoped

**Rationale**: Null, empty, and whitespace-only values must retain broadcast behavior. Trim only
the authored Event-wait value before retaining it, preventing an unmatchable blank scope without
changing the existing producer-specific normalization of delivery values.

**Alternatives considered**:

- Retain blanks verbatim: rejected because correlated delivery rejects blank values and would
  create waits that cannot be selected by a valid correlated delivery.
- Reject blank authored values: rejected because the feature explicitly preserves existing
  unscoped behavior for blank input.

## Decision: Reuse the existing metadata propagation and correlated lookup

**Rationale**: The suspension projector copies registration metadata; the invocation and
re-suspension scheduler paths copy it into bookmark-creation payloads; bookmark creation preserves
payload metadata. The global lookup already requires the correlation metadata key for a correlated
delivery and ignores it for an unscoped delivery.

**Alternatives considered**:

- Add a bookmark field or index: rejected because metadata already carries the value and the
  lookup first selects by event identity before applying its current correlation predicate.
- Modify router behavior: rejected because the defect is missing receive metadata, not routing
  selection logic.

## Decision: Keep start fan-out and BPMN correlation authoring outside this work unit

**Rationale**: The feature is limited to existing Event waits. Start trigger binding selection and
BPMN synthesized catches do not need to change for a correctly authored Event wait to participate
in correlated resume selection.

**Alternatives considered**:

- Filter workflow starts by authored correlation scope: rejected because it changes a separate
  established start behavior.
- Add BPMN correlation controls: rejected as a separate authoring and interchange design problem.

## Verification Evidence

- `Event.ExecuteAsync` creates the typed Event wait registration.
- `StatefulActivitySuspensionProjector.Project`, the invoke/resume bookmark-work builders, and
  `WorkflowCreateBookmarkSchedulerWorkHandler.MergeBookmarkMetadata` preserve registration
  metadata to the bookmark.
- `GlobalBookmarkStimulusLookupResult.CorrelationMatches` is the existing exact-match reader;
  `GlobalBookmarkStimulusLookupTests` and `StimulusRouterTests` already cover its lookup and
  routing behavior.
