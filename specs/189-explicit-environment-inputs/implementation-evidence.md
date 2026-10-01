# Implementation evidence: explicit private environment inputs

Implementation owner: [#2292](https://github.com/elsa-workflows/elsa-foundation/issues/2292), under #1962 / #1959. Authoring #2277 and its causal delivery dependency #2282 are complete. This records incremental local evidence, not a production capability or delivery pass.

## Setup checkpoint

Root reviewed the inherited public-label/private-value boundary and added unknown overlay-only error-identity omission to the contract/matrix and T030/T034. The specification is Approved to implement, not Implemented. The specify-stage non-claim is prose; the 27 actual design checklist items are complete. The optional pre-implement auto-commit hook was skipped because there was no outstanding implementation checkpoint at entry; root commits explicit reviewed paths.

T001 records ownership and compatibility at the worker transport, file-only candidate contract, persistence DTO contract and selected host. No old request/method/response shape was changed. T002 adds independent Version 1 and the metadata-only assembly declaration shape (int/string, assembly target, read-only properties, duplicate detection allowed). T003 enrolls Workbench only; Program.cs source order remains unchanged. T004 adds shared synthetic public/private canaries, correlation IDs and exact-byte document builders to the existing disposable fixture; no extra project or private-value artifact was created.

Actual local checks at this setup checkpoint:

- EF library build: success, zero warnings/errors.
- Real Workbench build: success, 92 existing migration/style/obsolete warnings, zero errors. Build does not execute startup, prove capability binding or establish inspection behavior.
- Existing architecture source inventory test: one passed, zero failed/skipped, with the two new files added to the exact inventory. The future operation file remains to be added under T039.
- Old-reader regression cases: 12 passed, zero failed/skipped. All nine old persistence commands first parse their baseline, then reject an environment-input object and explicit null. The old candidate reader rejects both forms at the outer/candidate/file boundaries. This is compatibility guard evidence before new transport integration; T028 and final compatibility controls remain open.

The original candidate-v1 host operation, validator and persistence preparation policy remain unchanged. No new operation method, public CLI option, private capture/protocol or runtime/DB acceptance is claimed at this checkpoint. Remaining tasks, adverse controls, complete affected suites, real Workbench/public-wrapper journey, maps and gated delivery remain required. No hosted implementation PR is open yet.


## Loader, raw admission and closed outer protocol checkpoint

Root integrated the isolated worker/enrollment and raw-document slices, reviewed their complete source, repaired the shared-parser assembly boundary, simplified duplicate closed-pair/property setup, and kept the old public loader/DTO/readers/response lane unchanged. The pure raw parser now lives in the EF-free WorkerContract assembly; the CLI uses a thin fixed-refusal adapter. No worker-to-CLI or worker-to-EF reference was added.

T005 is complete: the inspection-only loader returns an assembly only when its actual name and location match the selected closure. Controls cover successful loading, a renamed DLL, and an assembly with the same name already loaded from another location. The latter first verifies that the old void loader still admits its matching identity, then verifies additive location refusal. T012 is complete for raw document admission: strict UTF-8, optional one BOM, closed grammar, duplicate/unknown fields, empty/invalid scalar keys, invalid values, omission/blank semantics, case/alias collisions, all eleven prefixes, depth and limits. Exact/one-over controls include multibyte UTF-8 key/value lengths and the raw-key bound before length-reducing normalization.

The separate CandidateEnvironmentWorkerRequestV2 and WorkerEnvironmentInput shapes require the exact new outer/nested fields and correlation while preserving candidate-v1. Shared transport admission checks canonical base64 and decoded raw length before allocation, validates the original document independently and clears its owned decoded byte array. New response entry points parameterize shared safe framing with only the approved source/external-input mode and new fixed codes; old public entry points remain bound to the old lane. Only a closed correlated host-unenrolled response admits exit 3; that same code at exit 2 refuses. Peer worker messages are replaced by fixed local messages. New-lane serialized request 8 MiB and response 4 MiB exact/one-over controls execute alongside existing old-lane controls.

Actual final local command:

```text
dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj --no-restore --filter 'FullyQualifiedName~ToolingEntryPointTests|FullyQualifiedName~WorkerProtocolTests|FullyQualifiedName~CompositionInspectionCaptureTests' --logger 'trx;LogFileName=runtime-2292-environment-foundations-reviewed.trx' --verbosity quiet
```

Result: **295 executed/passed, zero failed/skipped**, from the actual TRX. Log `/tmp/runtime-2292-environment-foundations-reviewed.log`. This is three focused classes in the existing CLI project, not a full affected-suite or runtime/DB acceptance claim.

The duplicate-enrollment mutation changed only `declarations.Length != 1` to `declarations.Length == 0`. The existing seven refusal cases executed: six passed and the duplicate case failed with no exception thrown. The source guard was restored in finally, and the final 295-case run passed. Mutation log `/tmp/runtime-2292-enrollment-mutation.log`, TRX `runtime-2292-enrollment-mutation.trx`. No weakened guard remains.

Earlier attempts remain recorded: the first 203-case run failed its positive metadata control because Reflection.Emit returned an AssemblyBuilder wrapper instead of the emitted assembly; the fixture now returns the actual assembly and the identity guard is preserved. The first combined build exposed the helper's incorrect frontend-only location; it was moved to the worker rather than adding a reference. A later 287-case run failed because its success fixture selected Probe while the candidate accepted ResourceProbe; it now uses the existing candidate-bound fixture, preserving correlation enforcement. Logs `/tmp/runtime-2292-worker-foundations.log`, `/tmp/runtime-2292-environment-foundations.log`, and `/tmp/runtime-2292-environment-foundations-fixed.log` retain those outcomes.

Existing architecture guard `Neutral_activation_and_tooling_projects_have_no_entity_framework_dependency` executed all three cases with three passed, zero failed/skipped; log `/tmp/runtime-2292-worker-ef-boundary.log`. This does not substitute for the final full architecture gate.

Authorized maps `all` and `check` passed; logs `/tmp/runtime-2292-foundations-maps-all.log` and `/tmp/runtime-2292-foundations-maps-check.log`. Root reviewed both generated findings reports: they are byte-identical to the earlier checkpoint, as is manifest.json. Only spec-status-map.md changed, reflecting six completed and forty open tasks.

Two bounded independent read-only reviews found no remaining must-fix in the integrated admission/protocol code. Optional error identity canonicality remains a host-owned proof obligation: frontend syntax validation cannot establish actual host metadata. T007/T010 are not marked complete because the inner host envelope, host response formatting and operation remain absent. T006/T013 remain open until actual worker orchestration proves capability-before-enrollment and the built host controls. T008/T016 capture integration, all production stories, complete suites, Workbench/public-wrapper evidence, maps and hosted delivery remain required.

## Later main incident

The exact 9bba publishing correction evidence remains valid. Newer main 3decc CI 36916843228 failed only Core-only build & test (27 of 28 jobs succeeded): EF 699 passed / 1 failed / 1 existing skip of 701, broader core 3335 passed / 1 failed / 1 skip across 19 assemblies. The disposed SQLitePCL.sqlite3 collation/open signature during BackfillScenario.SaveChangesAsync matches prior #2185 reports; affected test/cleanup sources are unchanged since 9bba, but no cause or repair is established. The separate broad build job passed 13,007 with 32 existing skips. [Main incident #2293 triage](https://github.com/elsa-workflows/elsa-foundation/issues/2293#issuecomment-5939879108) preserves the failure and links unresolved #2185. It does not reopen the causally repaired #2282 or establish a green implementation delivery gate.


## Private capture ownership checkpoint

Root integrated the isolated capture writer (`bb4e748dc`, cherry-picked as `69d1da9c0`) and reviewed the complete delta. The additive capture retains the installed host independently of the source directory, reads the bounded private document once for initial admission, retains the original bytes including a BOM, and constructs canonical base64 only when beginning its one invocation. The request copies package roots and the candidate and correlates the environment input with the same capture. The private input path stays local and is absent from transport metadata; no additional persisted copy is written.

The old `Open` signature and file-only payload remain unchanged. Both captures and input snapshots now provide idempotent disposal. Lifecycle locking admits exactly one begin, including concurrent callers. Observed private/source/intent drift disposes the private capture; restoring the operator file does not re-enable that invocation. Recovery requires a fresh capture and fresh correlation IDs. All owned snapshot and reread buffers are cleared on disposal, initial/late capture failure, and mismatch. The bounded reader preallocates its maximum capacity so stream growth cannot leave abandoned private arrays, and clears both its accumulator and read buffer on success/refusal. Returned request strings and existing JSON configuration strings are managed strings; no physical or managed-string erasure is claimed.

Root corrections include a lambda/discard compile defect, async concurrent test execution, BOM comparison of the actual transported bytes, consolidation of repeated snapshot cleanup into `finally`, and the fresh-capture requirement after observed drift. Independent review identified the growable reader buffer issue; root repaired it with bounded preallocation and the second source review found no remaining must-fix in this helper delta.

The preliminary focused run returned **307 executed/passed, zero failed/skipped**, from the actual TRX (`runtime-2292-private-capture-reviewed.trx`). It ran the same three existing CLI classes, with no new project/provider/cadence. The final run after async/cleanup/preallocation corrections also returned **307 executed/passed, zero failed/skipped**, from actual TRX `runtime-2292-private-capture-final.trx`, log `/tmp/runtime-2292-private-capture-final.log`. The single-use guard is restored in that final run. Both runs are focused helper/contract checks, not full-suite or host acceptance evidence.

A meaningful mutation removed the begin guard's `_environmentInspectionBegun` check. The single-use/disposal case executed and failed with `Assert.Throws() Failure: No exception was thrown`; actual mutation TRX records one failed, zero passed/skipped. The original source was restored in `finally`. Log `/tmp/runtime-2292-single-use-mutation.log`, TRX `runtime-2292-single-use-mutation.trx`. No weakened guard remains.

T008 remains unchecked because command/process disposal, timeout/cancellation/cleanup integration and detached host inspection are not yet proven. T016/T032 and the all-three-story acceptance gates also remain open. Root has delegated two distinct supporting writers for EF-owned inner host inspection and the additive worker exchange; neither is production proof or a second delivery leaf.

Authorized maps `all` and `check` passed for this checkpoint. Logs `/tmp/runtime-2292-capture-maps-all.log` and `/tmp/runtime-2292-capture-maps-check.log`. Root read the manifest and both generated findings reports; all generated maps, findings and manifest remain byte-identical, so no generated file needs staging. Task accounting remains six complete and forty open.

Main advanced to `f02354068b8a9ed2382286685ead9faf018331dc` with an unrelated identity fix. At refresh, CI `36923625909` was in progress. The prior #2293 SQLite report remains open and is not declared repaired or stale.
