# Worker profile research

Baseline: e7032680dc48e2077631e01e2c961093b0c5ef4d. These are source decisions, not executed actor proof.

## Stable membership and snapshots

**Decision**: Current catalogv2 is latest; publish v3 with Worker exact19 = unchanged Embedded16 plus identity abstractions, OIDC and IAM EF. Preserve old resource bytes and definition digests. Update LoadFor to search v1/v2/v3; current `{1, CurrentVersion}` would otherwise drop v2 pins.
**Rationale**: Existing actor enables static18 and gets Tasks through runtime dependency closure; the profile makes all19 explicit. Settings do not enter immutable membership.
**Alternatives**: Editing catalogv2 or embedding environment settings would break publication semantics; nesting Embedded would add inheritance semantics absent from the contract.

## Candidate is activation authority

**Decision**: Reuse actual CLI/PTY helpers and production CLI build reference in existing Runtime EF tests. Prepopulate local feature settings in source shell JSON, accept/generate into fresh directories, then load those files in the child. Remove its static selector/configuration loop and all parallel feature settings.
**Rationale**: `CompositionCandidateBuilder` starts from source copies and reconciles selection/portable reviewed leaves; local OIDC/IAM values can remain source-owned without broadening portable settings. Accepted intent, generated readback, actual EnabledFeatures and independent consumed hashes prove each boundary.
**Alternatives**: In-process command calls or hardcoded child Features providers cannot establish producer-to-consumer integration.

## Decisive modified-candidate controls

**Decision**: Secondary accepted18 removes ActivitiesControlFlow and changes Audience. Keep Events. Persist the existing capabilities-read permission through the real IAM control channel; call actual `/capabilities` with signed old/new-audience tokens and observe reads/state.
**Rationale**: Serialization's `JsonPayloadConvertersInitializingStartupTask` needs `IInlineEventPublisher` from Events and runs before readiness through Tasks startup. ControlFlow supplies design handlers/validator, with no identified dependency for capabilities startup/read. Actual secondary startup remains mandatory evidence. Primary19 retains event workflow behavior.
**Alternatives**: Removing Events fails activation before the intended selection assertion; keeping identical membership cannot detect a static selector. Audience observation alone is weaker than paired authentication through the real handler.

## Identity and persistence ownership

**Decision**: Retain Spec190 static-provider/tenant normalization, independent audience, host-supplied AddPersistenceCore scope and separate legacy IAM target. Runtime primary never redirects IAM. Local loopback issuer allows HTTP only in tests; document deployment HTTPS requirement.
**Rationale**: Production normalization is delivered, so publishing a starting selection needs configuration/actor proof, not another identity platform.
**Alternatives**: Token-derived tenant, automatic IAM enrollment or root fabricated authentication would violate delivered ownership contracts.

## Deferred choices

Authoring's Publishing→Triggers→RuntimeAPI choice remains with the product owner; its answer is unnecessary for Worker. External IdP interoperability, distributed topology and six real UX participants retain separate owners and revisit gates. The selector bug #2284 stays separate; this task does not delete suites or change cadence.

## Independent and root contract review

2026-10-02: GPT-6 Luna Extra High independently audited membership, old-pin loader branches, current test assumptions, source preservation, child configuration authority and the modified-candidate endpoint. Root checked the actual candidate-builder, ControlFlow registration, Serialization startup dependency and permission-gated capabilities endpoint. No material Worker product question remains. The old child selects18 explicitly and activates Tasks through dependency closure; the new profile intentionally makes all19 explicit. Actual candidate consumption remains an unchecked implementation gate.

Existing `main is red: CI` #2293 is still open for the historical SQLite native-handle failure. The current e703 CI is independently green; no causal resolution or closure of #2293 is inferred.
