# Data Model: Trigger Publication Contract Hardening

## WorkflowTriggerPreflightOutcome

Non-persisted, immutable result for one executable publication attempt.

| Field | Meaning | Validation |
|---|---|---|
| Artifact identity | The executable artifact being assessed | Required; copied from the executable |
| Node outcomes | One outcome for every executable node classified as a trigger | Complete and unique by executable node id |
| Bindings | Flattened binding candidates from registered node outcomes | Every binding belongs to exactly one node outcome; ids are unique |

The outcome exists only for the duration of publication. It is not part of `WorkflowExecutable`, `WorkflowTriggerBinding`, `PublishedWorkflowView`, or a Groundwork document.

## WorkflowTriggerNodePreflightOutcome

| Field | Meaning | Validation |
|---|---|---|
| Executable node id | Trigger node within the artifact | Required and unique in the outcome |
| Activity type | Runtime activity identity | Required |
| Provider id | Stable identity of the single recognizing provider | Required, nonblank |
| Status | `Registered` or `IntentionallyNonStarting` | Exactly one terminal status |
| Bindings | Zero or more normalized binding candidates | Empty only for `IntentionallyNonStarting`; non-empty for `Registered`; unique by deterministic trigger-binding id |

Rejected nodes do not produce a completed outcome; preflight throws a typed failure carrying the available artifact/node/provider context.

## ActivityTriggerStimulusResult

Existing per-provider recognition result. Semantics remain:

- `NotRecognized`: provider does not own the activity type.
- `Recognized(descriptors)`: provider owns the activity type.
- `Recognized([])`: provider owns the type but the authored node is intentionally non-starting.

Provider identity belongs to the provider seam/preflight wrapper rather than persisted descriptor metadata.

`IActivityTriggerStimulusProvider` instances form an exact-one Strategy set. They are not a contribution collection: the consumer selects one owner using the executable node as context, then consumes only that strategy's result.

## WorkflowTriggerBinding

Existing durable registered-state entity. Shape is unchanged. It remains the source for current registered trigger reality and contains artifact/node/stimulus identity plus provider-specific allowlisted routing metadata.

## RecurringTriggerSchedule

Existing durable schedule entity. Shape is unchanged. During preflight, Timer/Cron schedules are constructed as in-memory candidates using one captured `now`; they are persisted only after all schedule candidates and the inner trigger preflight succeed.

## State transitions

```text
Executable trigger node
    -> no provider claim ------------------------> rejected
    -> multiple provider claims -----------------> rejected
    -> exactly one claim + invalid descriptor ---> rejected
    -> exactly one claim + zero descriptors -----> intentionally non-starting
    -> exactly one claim + valid descriptors ----> registered candidates

Timer/Cron registered candidates
    -> invalid/unmaterializable schedule --------> rejected before mutation
    -> complete schedule candidate set ----------> apply existing binding replacement,
                                                    then schedule replacement
```

## Compatibility at spec 090 delivery

- Catalog activity versions: unchanged.
- Executable schema and behavioral hashing inputs: unchanged.
- Trigger-binding and recurring-schedule schemas: unchanged.
- Groundwork document versions and golden fixtures: unchanged by this work unit.

These statements describe the spec 090 diff and its 2026-07-11 baseline. They are not a current promise to
read every persisted executable or source-reference shape from that baseline.

## Current Runtime persistence boundary

- Before GA, every Runtime Groundwork kind uses its current version as minimum-readable, retains only its
  current fixture, and registers no Elsa compatibility upcaster.
- `workflowExecutable`, `workflowExecutableSourceReference`, and `workflowExecutionState` use version 4;
  versions 1 through 3 are rejected before content deserialization.
- Executable v4 persists the reusable-activity input contract and direct dependency snapshot; source-reference
  v4 persists tenant scope; workflow-execution v4 persists dispatch nesting depth.
- An installation carrying earlier persistence must atomically reset the complete Runtime and Publishing
  Groundwork persistence sets while preserving Design and Activities data.
- Workflows must be republished before traffic is served so retained executions, publication authority, and
  serving projections cannot point at removed artifacts.

## As-built confirmation

The spec 090 implementation uses these non-persisted outcome types and left every listed durable shape
unchanged in that work unit. Its golden and Groundwork fixture verification is recorded in
[quickstart.md](quickstart.md); spec 090 added no schema version, upcaster, or migration. The later current-only
pre-GA boundary above supersedes its Runtime persistence compatibility.
