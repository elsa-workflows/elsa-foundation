# Embedded starting-profile v1 contract

## Immutable selection

`embedded-runtime@1` selects exactly:

`Primitives`, `Serialization`, `Mediator`, `Events`, `Expressions`, `ActivitiesRuntime`, `ActivitiesPrimitives`, `ActivitiesControlFlow`, `ActivitiesSequence`, `WorkflowsRuntimeEntityFrameworkCore`, `WorkflowsRuntimeResumption`, `WorkflowsRuntimeTriggers`, `ApiCapabilities`, `Tasks`, `WorkflowsRuntimeApi`, `FileSystemDistributedLocking`.

Profile/catalog digests cover selection content and rationale, never provider, connection, lock path, signing or migration values. A composition pins catalog/profile and accepted expansion. Changed same-version content refuses; new version requires explicit review.

## Planner

Plan exposes exact IDs and all selection sources. Required edges come from supplied loaded runtime descriptor first, otherwise manifest evidence, otherwise pinned reviewed rationale. Reviewed required target absent from exact selection is unresolved, even without inventory. A runtime descriptor with empty list does not inherit reviewed fallback edges. Disagreements remain visible. Unknown host/package/persistence state is unverified, never ready.

## Candidate mapping

For one selected shell/environment, unchanged supported object-map source may add absent ID as `true` or disable base-enabled ID as `false` in selected overlay. Unknown base settings and unselected files survive. Review identifies edits/layer without values. Generated bundle is re-read and exact enabled IDs compared with accepted plan.

Refuse without candidate on pin/accepted-expansion drift, missing known edge, array/mixed/duplicate source, base `false` blocking enablement, removal of overlay-only setting object, stale source or readback mismatch. Setting insertion, in-place edits, deployment and live reload are excluded.

## Host boundary

Tested example needs separately configured named SQLite resource, isolated writable `LocksFolderPath`, signing, assemblies and deliberate migration authorization. Generic host creates no HTTP listener, though Runtime API is selected. Other providers, multi-host locking, package loading and web security are unverified.
