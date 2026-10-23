# Native Rules hosting checkpoint

This checkpoint extends the [delivery proof checkpoint](native-delivery-checkpoint.md) with normal feature registration and bounded background delivery. T027–T032 remain open until complete workflow publication, persisted Sequence/Flowchart, native REST and package-host acceptance pass.

## Runtime integration

`RulesWorkflowsRuntimeFeature` registers the checkpoint enricher, Start/Cancel intent handlers with one-second retry-until-acknowledged policies, the delivery executor/inspector and the default independent proof validator. One singleton delivery worker serves both `IBackgroundTask` and `IHostedService`. Its concurrency and idle/error polling settings are bounded and captured at registration. The host must still compose the native workflow runtime, persistence and bookmark dispatcher alongside the Rules store/reader.

The computation and delivery workers share an internal Rules lifecycle implementation, exposed to the sibling runtime assembly through same-domain internal access. They retain separate concurrency limits. The extraction preserves the existing start gate, host readiness versus shell activation distinction, one scope per attempt, missing-store preservation, cancellation and stop behavior. It introduces no public worker framework, queue-pause API, additional durable queue or cross-domain transaction. The future task runner remains separate work.

## Executable evidence and limits

- The delivery-host theory uses real GenericHost DI, both Rules features, the normal Rules EF feature and migrated SQLite persistence. It seeds a retained refusal terminal, starts delivery through either the host or directly through `IBackgroundTask`, cancels the activation token, and observes the real persisted acknowledgement and absence of further eligible delivery. It checks shared singleton identity, retained retry registrations and graceful loop completion. Native state and bookmark dispatch are controlled doubles. The direct interface branch does not claim to exercise TaskManager's complete orchestration.
- The catalog test activates an actual CShells shell with the normal Tasks, Events, Serialization, Activities and Rules features. Its startup scanner resolves the ordinary CLR alias for `RunRules`. Teardown drains the shell through its normal terminator. The fixture supplies the unused distributed-lock dependency and root host lifetime; it does not supply a Rules store or claim persisted workflow execution.
- The publication test resolves the real registered node-metadata enricher and runs its event collection path through the Design feature's registered compilation source. It checks canonical artifact bytes, literal-source hash, vocabulary and pinned implementation. The implementation identity is synthetic and explicitly not package provenance. This is the registered enrichment bridge, not the complete workflow compiler/publication endpoint.
- Existing computation-worker composition tests cover real migration, pooling, admission, worker results, accepted-receipt recovery and direct Tasks-interface startup. They must remain green after the lifecycle extraction.

## Review and correction

The first host run retained the delivery and timed out because the controlled dispatcher omitted the required reason on a duplicate dispatch result. The worker logged the resulting `ArgumentNullException` and preserved the lease as designed. The fixture was corrected; no production exception handling or acknowledgement rule was weakened. The first catalog run could not find its no-op lock provider because the test feature was private and undiscoverable; making that test feature public fixed actual shell discovery. An initial build also caught the publication test's missing codec namespace import.

A read-only delegated production review found no lifecycle or registration defect. It noted that the direct `IBackgroundTask` test is not TaskManager orchestration evidence; the descriptions above retain that limit. Root reviewed all delegated files and executed the checks.

The removal control changes only the explicit `IBackgroundTask` startup path to wait for root-host readiness. The direct Tasks-first delivery case fails while the plain-host case passes. The original source is restored byte-for-byte before intact validation. This checks the shell-readiness distinction and does not complete T032's REST/package proof-before-acknowledgement control.

## Verification

The restored complete Rules suite passed **262 tests** and architecture passed **585 tests**, both with zero failures/skips. The normal solution restore, generated-map refresh and committed solution-filter check passed. Both generated findings reports were inspected: zero runtime-to-design signals and zero missing catalog links. Generated documentation remains ignored and unstaged. Three bounded root review rounds, including final independent read-only review, ended clean. See the [machine-readable receipt](native-hosting-receipt.json) for exact commands and log hashes. Package/analyzer warnings and generator-hint name conflicts caused by same-domain internal access remain visible; no check or warning policy is relaxed. Whole-leaf package, persisted native continuation, REST and upgrade gates remain open. External effects remain at least once.

## Next persisted case

The next case should place `RunRules` on one branch of a Flowchart fork/join nested in a Sequence, with a controllable sibling. Prove the Rules activity and invocation finish while the sibling/join remain pending, then finish the sibling and prove the join and following Sequence step run once. The existing `NativeBackgroundActivityFlowchartEfTests` provides the runtime/SQLite pattern, but its harness saves a prebuilt executable rather than compiling an authored definition. Pair that runtime proof with real workflow compilation/publication; do not call the harness save operation full publication evidence. The actual authoring composition host and branching REST suites provide source patterns for that remaining path.
