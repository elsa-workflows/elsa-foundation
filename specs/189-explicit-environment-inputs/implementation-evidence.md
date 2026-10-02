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

## Worker dispatch and host/frontend integration checkpoint

Root reviewed and integrated the additive worker dispatch, exact new-contract lookup and common bounded
reflection exchange. Candidate commands share closure observation/loading but retain their own envelopes,
capability binding and response validators. The worker binds the independent environment capability
before metadata-only enrollment; there is no legacy fallback. Owned request/response streams have fixed
capacity and clear their complete backing arrays, including bytes beyond a shortened logical length.
Root review caught and corrected a zero-capacity `MemoryStream` constructor in the first preallocation
patch before integration verification.

Actual worker checkpoint command:

```text
dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj --no-restore --filter 'FullyQualifiedName~ToolingEntryPointTests|FullyQualifiedName~WorkerProtocolTests|FullyQualifiedName~CandidateWorkerOperationTests|FullyQualifiedName~CompositionInspectionCaptureTests' --logger 'trx;LogFileName=runtime-2292-worker-integration.trx' --verbosity quiet
```

At root code head `fd9189a43`, the actual TRX records **349 executed/passed, zero failed/skipped**.
Log: `/tmp/runtime-2292-worker-integration.log`. The only concurrent root edit was catalog documentation;
host/frontend production slices were integrated after this process completed. This is focused
worker/capture/contract evidence, not an actual Workbench operation or full-suite result.

Root then integrated the host operation and frontend option. Host source review repaired request-pool
rounding, partial decoded-buffer ownership and error classification, and extracted one shared
`InspectComposition` core for both lanes. Frontend review restored post-validation cancellation/deadline
checks, retained capture ownership through rendering, and added race-safe response-copy cleanup. A fixed
parse validator refuses repeated `--environment-input` occurrences without echoing either path; omitted
and single occurrences have companion parse controls.

The first combined CLI build at root head `c82200230` failed with **CS0136** in the new host parser: a
local request variable reused the enclosing JSON candidate name. **No tests executed** in that attempt.
Log: `/tmp/runtime-2292-cli-lane-integration.log`. The correction and subsequent executed result must be
recorded separately; this failure is not a test or runtime regression claim.

Independent source review also found that syntax-only successful projection could promote a new
overlay-only resource or connection label. That finding is a live privacy repair obligation: derive
declared public labels from the pre-overlay captured configuration, keep one host composition/preparation,
and refuse unknown target labels before serialization. Companion private-label canary tests and actual
Workbench/public-wrapper proof remain required. No safe-preview, delivery or complete-task claim is made
from the integrated source alone.

## Host parity, privacy fence and actor harness review

Root integrated the pre-overlay public-identity fence and direct-host raw-document controls through
`356546913`. Captured resource/connection names use the configuration system's case-insensitive
membership rules. Unknown overlay-only target labels refuse before serialization; source-declared
aliases remain supported. The captured JSON configuration is built once and chained with the explicit
overlay before the one shared composition/reconciliation/preparation path. A bounded source review of
that exact head found no remaining must-fix in these production boundaries; it is not an external PR
approval or complete acceptance result.

Actual direct-host selection:

```text
dotnet test tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/Elsa.Persistence.EntityFrameworkCore.Migrations.Tests.csproj --no-restore --filter 'FullyQualifiedName~EfCandidateInspectionTests|FullyQualifiedName~EfToolingHostTests' --logger 'trx;LogFileName=runtime-2292-host-restored.trx' --verbosity quiet
```

At root code head `d8f21beaa`, the final actual TRX records **171 executed/passed, zero failed/skipped**.
Log: `/tmp/runtime-2292-host-restored.log`. The preceding run at `356546913` executed 171, passed 170 and
failed one new removal fixture: accepted and removed IDs overlapped, so closed envelope admission
correctly refused before reconciliation. The test-only correction makes the baseline reach the
selection conflict and the intended overlay produce a valid removal.

Root temporarily removed the real `AddInMemoryCollection` overlay application in the captured-root
configuration builder. The connection-affinity parity case then **executed once and failed as intended**
(expected success, actual refusal). Actual TRX: `runtime-2292-overlay-mutation.trx`; log:
`/tmp/runtime-2292-overlay-mutation.log`. A Python `finally` restored the original source bytes, and the
171-case run above passed with the overlay restored. This is a causal check of actual host preparation,
not a second frontend resolver or real-database test.

Frontend attempts preserved as separate evidence:

- `runtime-2292-cli-repaired.log`: build failed with CS0122 from direct access to internal `ElsaCli`;
  no tests executed. The test now uses the existing public assembly to reflect the internal entrypoint,
  without widening production visibility.
- `runtime-2292-cli-lifecycle.log`: build failed with CS1061 in a new repeatability helper;
  no tests executed. The public renderer already excludes fresh correlation IDs, so root removed the
  helper and compares the entire public JSON directly rather than masking arbitrary fields.
- `runtime-2292-cli-compiled.trx`: **445 executed, 444 passed, one failed, zero skipped**. The sole failed
  assertion expected a custom option message; native exact-one arity had already refused repeated
  input without echoing either path. Root removed the unreachable redundant validator.
- `runtime-2292-cli-final-focused.trx`: **445 executed, 444 passed, one failed, zero skipped**. The new
  late-read fixture released its read during disposal, so cleanup could succeed instead of producing
  the asserted cleanup failure. Root removed that release and retains the read until the assertion,
  then releases it in `finally`. No production cleanup defect is inferred from that fixture race.
- `runtime-2292-cli-corrections.trx`: **two executed/passed, zero failed/skipped** for the corrected
  repeated-option and late-read controls. The complete focused rerun is recorded separately when terminal.

Root reviewed the actual Workbench actor tests and corrected three harness assumptions before claiming
an actor result: production shell layering could re-add features; an unenrolled fixture cannot reach
an explicit-lane composer marker; and `composition accept` cannot overwrite an existing output. The
reviewed Workbench fixture now trims both shell layers, asserts its exact four authored requested IDs,
retains the real host closure and all four JSON layers, and creates fresh recovered/required-edge
accepted files while preserving the original accepted file. The required-edge companion overlay
explicitly disables the dependency while retaining its dependent feature. These are authored controls,
not executed Workbench acceptance yet.

Project 51's stale README was refreshed and read back on 2026-10-02: #2292 is the sole active delivery
leaf; #2177/#2277/#2282 are closed; #2064 remains blocked on six real participant sessions. No genuinely
Ready successor was found in the refreshed queue. The program and all-three-story delivery gate remain
open. No new project, suite, provider matrix or workflow cadence was added.

## Focused frontend pass and actual-host prerequisites

At root code head `4e9932e0afdf69efff348dba2e9cc25242cc3cc4`, the final focused frontend
TRX `runtime-2292-cli-restored.trx` records **445 executed/passed, zero failed/skipped**.
Log: `/tmp/runtime-2292-cli-restored.log`. The selection covers `ToolingEntryPointTests`,
`WorkerProtocolTests`, `CandidateWorkerOperationTests`, `CompositionInspectionCaptureTests`,
`CandidateProcessTests`, and `CandidateInspectionOutputTests` in the existing CLI project.
It is focused contract/lifecycle evidence, not the complete CLI suite or actual Workbench actor proof.

The actual Workbench and Foundation Host outputs were then built serially with `dotnet build
<project> --no-restore --verbosity quiet`. Workbench returned exit 0 with 12 warnings and zero errors
(`/tmp/runtime-2292-workbench-build.log`); Foundation Host returned exit 0 with zero warnings/errors
(`/tmp/runtime-2292-foundation-build.log`). These are build prerequisites only. The public actor
selection `FullyQualifiedName~CandidateInspectionTests` is recorded separately after its terminal
result; no runtime startup or database proof follows from these builds.

The actual-host actor selection then finished with **31 executed, 28 passed, three failed, zero
skipped** in `runtime-2292-actual-host-actors.trx` at that same code head. Log:
`/tmp/runtime-2292-actual-host-actors.log`. The actual Foundation Host unenrolled control and public
raw-input refusal controls passed. The actual Workbench success, repeatability and selection/recovery
cases returned exit 3 where success or a selection refusal was expected. Their first exit assertions
did not retain the safe fixed refusal code, so the cause is under investigation and no recovery,
Workbench success or all-three-story acceptance is claimed. The no-option Workbench comparison in
the failed success case was not reached. This failure changes the next action from final acceptance
to exact actual-host diagnosis; it does not invalidate the separately recorded 171/445 focused results.

Root's focused architecture selection returned **11 executed/passed, zero failed/skipped**, including
the exact ADR 0072 pilot inventory and the three neutral activation/tooling dependency cases. Actual
TRX: `runtime-2292-architecture-focused.trx`; log: `/tmp/runtime-2292-architecture-focused.log`.
The full architecture suite remains required. Maps check first found the extension-point map stale
after the owning catalog update; the authorized `-- all` refresh changed only that map. Root reviewed
the unchanged manifest and both generated findings reports; the subsequent `-- check` returned exit 0
and `Generated maps still describe the tree.` (`/tmp/runtime-2292-maps-restored.log`).

Two bounded read-only audits found no additional definite production defect, but identified missing
explicit-transport boundary and post-dispatch private-file drift controls. They also found that the
Workbench success fixture used the same private connection value in source and overlay, so that
assertion alone could not prove overlay consumption. Those controls are being strengthened without
new projects, host enrollment, or test cadence. The closed raw overlay shape cannot admit a depth-64
nested object; existing selected-source JSON depth controls remain the authoritative reachable depth
boundary instead of inventing a valid deep overlay specimen.

## Remaining boundaries and actual Workbench diagnosis

The explicit direct-host request boundary at code head `e9146a163` returned **two executed/passed,
zero failed/skipped**, covering exactly 8 MiB and the first byte over through the public host wrapper.
Actual TRX: `runtime-2292-host-exact-request.trx`; log: `/tmp/runtime-2292-host-exact-request.log`.

The additional frontend boundary selection at `9f6176b32` returned **ten executed, nine passed, one
failed, zero skipped** (`runtime-2292-frontend-boundary-controls.trx`). The exact 4,096 removal fixture
still carried settings for the removed feature in its environment layer; the existing bridge correctly
refused to discard those settings. The test-only repair at `05ddd8907` removes that overlay entry and
reuses the explicit-success response setup. The other nine cases exercised exact/one-over private-file,
request and response byte bounds, admitted selection size, the 300-second timeout option endpoint and
mechanical fake-child post-dispatch file drift. That drift case remains separate from an actual-child
or whole-command drift proof. The corrected selection's terminal result is recorded separately.

The focused actual Workbench diagnosis at `6f4c97cbf` executed once and failed with fixed code
`candidate-host-unavailable` (`runtime-2292-workbench-diagnosis.trx`). Temporary stage-only diagnostics
then showed that the host operation was not reached; no configuration values, paths, fingerprints or
exception text were recorded. All three instrumented source files were restored byte-identically in
`finally`, and both actual Workbench and CLI outputs were rebuilt successfully after restoration. The
diagnostic run itself is not acceptance evidence.

Root found a pre-existing runtime-owned `.nuplane/store-state.json` in the shared Workbench build
output with an empty active package set. The existing package loader observes that file before host
loading and refuses the empty set. A bounded control temporarily renamed that file, ran the same
actual Workbench public-command test, and restored the original file byte-identically in `finally`.
The control returned **one executed/passed, zero failed/skipped**
(`runtime-2292-workbench-empty-state-control.trx`; log:
`/tmp/runtime-2292-workbench-empty-state-control.log`), reaching both explicit-input and old no-option
Workbench invocations. No production package policy or refusal guard was changed. The permanent
harness repair will use a disposable fresh installation of the actual built Workbench closure,
preserving its compiled bytes/layout while omitting unrelated runtime-owned package state; its result
must be verified separately. The actual positive overlay case is also being strengthened to demonstrate
a changed safe resource/reference projection rather than an unchanged connection value.

## Restored frontend boundaries and fresh-installation actor retry

At code head `05ddd8907`, the corrected frontend boundary selection returned **ten executed/passed,
zero failed/skipped**. Actual TRX: `runtime-2292-frontend-boundaries-restored.trx`; log:
`/tmp/runtime-2292-frontend-boundaries-restored.log`. This covers the exact/one-over private file,
request and response byte controls, reachable 4,096 selection admission, the 300-second endpoint,
and mechanical post-dispatch drift. The fake child remains identified as such.

The fresh Workbench installation and stronger positive overlay fixture were committed through
`36264f7eb`. The copied real DLL, deps and runtimeconfig are checked byte-for-byte against the actual
built output; unrelated runtime-owned `.nuplane` state is omitted. The explicit positive input selects
a different source-declared resource/connection reference and fills its initially blank connection,
so a successful changed public projection would demonstrate consumption of the overlay.

That actual actor retry **did not pass**: `runtime-2292-workbench-actors-restored.trx` records
**31 executed, 28 passed, three failed, zero skipped**, all three with fixed
`candidate-host-unavailable`. Log: `/tmp/runtime-2292-workbench-actors-restored.log`. The earlier
empty-state quarantine control remains its bounded causal observation, but does not prove that the
permanent fresh-installation harness is sufficient. Root is diagnosing the copied closure separately;
Workbench success, repeatability, recovery and all-three-story acceptance remain unproven.

Root reviewed and integrated an actual owned-child drift control through `0febe5ecd`. It wraps only
the real child's stdout and changes the original private file after the first real response read,
then requires the final capture recheck to refuse before rendering. Review repaired an assertion
that queried the underlying `Process` after disposal: the wrapper now observes exit before disposal
and guarantees teardown. This test has not yet executed. Its scope is the actual Workbench child plus
the final capture seam, not a complete public-command injection journey.

## Physical-path repair and verified actual operator journey

The isolated diagnostic narrowed the copied installation refusal to **host location mismatch** after
layout/dependency preparation: the actual assembly name matched, but its location did not ordinally
match the supplied temporary path. Only fixed stage labels were recorded. The macOS temporary path
passes through a symlinked ancestor, so the fixture now resolves existing ancestor links before
creating its disposable installation (`059d0f307`). Production name/location validation is unchanged;
the original Workbench output and package state remain untouched. Temporary diagnostic production
source was restored and outputs rebuilt in the isolated checkout before its focused successful run.

Root then ran the full `CandidateInspectionTests` class at `059d0f307`:

```text
dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj --no-restore --filter 'FullyQualifiedName~CandidateInspectionTests' --logger 'trx;LogFileName=runtime-2292-actual-actors-physical-path.trx' --verbosity quiet
```

The actual TRX records **32 executed/passed, zero failed/skipped**; log:
`/tmp/runtime-2292-actual-actors-physical-path.log`. On this macOS run, the Workbench success case reaches
both the explicit overlay and old file-only public invocations. The changed source-declared resource
and connection reference prove overlay consumption; caller ambient values do not enter the result.
Fresh public invocations repeat the same safe projection. External selection divergence refuses, the
existing interactive accept workflow publishes a fresh accepted file, and a fresh capture succeeds;
the required-edge companion still refuses. The actual Foundation Host remains unenrolled.

The actual-child drift control now also requires a successful real Workbench response with the
explicit source and `supplied-intended` state before checking drift (`0ceb0fd86`). It passed in the
same run: the original private file changes after the first real stdout read, the final capture recheck
returns `composition-input-changed`, the private capture is disposed, and the real child exits before
its handle is disposed. The file is restored and source/input preservation is checked. This is the
actual child plus final capture seam; no whole-command injection hook or Windows journey is claimed.

The full affected projects, separate provider regression, complete diff/architecture review, final
maps and hosted PR/resulting-main gates remain open. This actor pass does not mark Spec 189 Implemented.

## Explicit-lane row boundary and producer-size audit

Root integrated the additional direct explicit-lane combined-row controls at `166e12b8a`, reusing
the existing persisted synthetic participant metadata and arrangement. The actual focused run of
`Public_operation_bounds_participant_and_finding_rows_together` returned **six executed/passed,
zero failed/skipped** in `runtime-2292-both-lane-row-bound.trx`; log:
`/tmp/runtime-2292-both-lane-row-bound.log`. Both lanes accept 1,022 participants plus two findings
and refuse 1,023 plus two without partial output; the existing additional old-lane overflows remain.
The explicit success asserts its own source and `supplied-intended` state. This is configuration-only
producer evidence, not a database or Workbench process journey.

Root rejected an audit's character-count argument that the 4 MiB host response limit was unreachable.
The admitted feature identity grammar includes `+`, and the default JSON writer escapes it with a
larger encoded representation. Existing exact/one-over worker transport controls remain valid, but
are not substituted for actual producer-size evidence. A bounded actual-producer control is being
developed using the existing persisted synthetic feature helper and the normal host
composition/reconciliation/preparation path; no production hook, payload padding, new project or
runtime host enrollment is introduced. No result from that pending control is claimed here.


## Actual producer byte boundary and final release preflight

Root integrated the producer fixture and shared synthetic-feature setup through `7d71e2c71`.
The normal explicit host discovery/composition/reconciliation/preparation path produces an actual
4,194,304-byte successful response; appending one ASCII byte to the single observed disabled ID
exceeds the limit. The fixture uses admitted `+` identities and the production JSON encoder's six-byte
escape cost, not payload padding or a production hook. Selected file and serialized request ceilings
are independently asserted, and neither run creates database or temporary artifacts.

The root Release selection at `7d71e2c71` returned **seven executed/passed, zero failed/skipped**:
one producer-boundary case and six both-lane combined participant/finding cases. Actual TRX:
`runtime-2292-producer-row-release.trx`; log: `/tmp/runtime-2292-producer-row-release.log`.
This supersedes the pending producer-proof state above, without changing the separate scope of
transport whitespace controls or the historical failed Workbench attempts.

For causal evidence, root temporarily removed only the new lane's real response-length guard.
`runtime-2292-producer-guard-mutation.trx` records **one executed/failed, zero passed/skipped**:
`Assert.Throws` failed because the oversized response no longer threw. Root restored the production
source byte-identically in `finally` and rebuilt the same selection;
`runtime-2292-producer-guard-restored.trx` records **one executed/passed, zero failed/skipped**.
Logs: `/tmp/runtime-2292-producer-guard-mutation.log` and
`/tmp/runtime-2292-producer-guard-restored.log`. Instrumented source/output is not used for delivery.

Final source candidate `ad84c6840` also clarifies the calibration comment and removes a new nullable
warning by checking the coalesced case-collision identity before invoking its callback. Bounded
independent source review found no remaining must-fix. Admission and selection reconciliation occur
before shared preparation; the successful public-label fence occurs **after** shared
`InspectComposition`/`Prepare` and **before** serialization. Old signatures and closed-lane semantics
are preserved through additive/shared refactoring; their source is not claimed byte-unchanged.

At `ad84c6840`, the actual Foundation Host Release build returned **12 warnings, zero errors**
(`/tmp/runtime-2292-foundation-release.log`), and the actual Workbench Release rebuild returned
**80 warnings, zero errors** (`/tmp/runtime-2292-workbench-final-release.log`). These are build
prerequisites, not suite or acceptance results. Final affected projects, provider regression,
architecture/maps, full matrix/diff review and exact-head/resulting-main delivery gates remain open.


## Full-suite discovery exposed a synthetic fixture collision

On `ad84c6840`, the full Release CLI project returned **1,045 executed/passed, zero failed/skipped**
(`runtime-2292-cli-final-release.trx`; log: `/tmp/runtime-2292-cli-final-release.log`). The
`CandidateInspectionTests` class accounts for 32 passed cases within that total, including actual
Workbench explicit/file-only invocation, repeatability, recovery, successful-child final drift and
actual unenrolled Foundation Host. This is a macOS run; Windows return guards do not establish a
Windows actor journey. The full planning project returned **118 executed/passed, zero failed/skipped**
(`runtime-2292-planning-final-release.trx`; log: `/tmp/runtime-2292-planning-final-release.log`).

The first full Release migrations project did **not** pass: **495 executed, 489 passed, six failed,
zero skipped** (`runtime-2292-migrations-final-release.trx`; log:
`/tmp/runtime-2292-migrations-final-release.log`). All six failures were public `EfToolingHost`
capability calls reaching the shared host-discovery refusal. Focused injected-closure tests had not
exposed the process-wide assembly interaction.

Root and an independent read-only audit found that the two lane variants at module counts 1,022 and
1,023 emitted separate persisted assemblies with identical shell feature IDs. The public host scans
all loaded contexts and correctly rejects duplicate discovered IDs. Root's bounded causal control
removed only the two added explicit-lane InlineData rows: the complete candidate class then returned
**104 executed/passed, zero failed/skipped** (`runtime-2292-row-pollution-control.trx`; log:
`/tmp/runtime-2292-row-pollution-control.log`). The original test source was restored byte-identically
in `finally`. That reduced-case run is causal evidence, not final coverage.

The permanent repair gives each row fixture a lane-specific feature ID and retains all six InlineData
cases, the actual public capability calls and the production duplicate-discovery guard. Its restored
class and full-suite results must be collected separately; no passing migrations gate is claimed yet.


The repaired complete candidate class returned **106 executed/passed, zero failed/skipped** with all
six lane-specific row fixtures restored (`runtime-2292-row-isolation-restored.trx`; log:
`/tmp/runtime-2292-row-isolation-restored.log`). Root committed the two-line fixture repair as
`be33523af`. The full Release migrations project on that head then returned **495 executed/passed,
zero failed/skipped** (`runtime-2292-migrations-restored-release.trx`; log:
`/tmp/runtime-2292-migrations-restored-release.log`). The actual TRX includes all six combined-row
cases and the exact/one-over producer response case, each passed. The earlier six failures remain
recorded above; this restored pass follows the diagnosed identity repair rather than a green retry
without a cause. CLI and planning source/dependency graphs are unchanged by that EF-test-only delta;
their `ad84c6840` full Release results retain their exact tested head.


The full Release architecture project at `be33523af` returned **634 executed/passed, zero failed/skipped**
(`runtime-2292-architecture-final-release.trx`; log: `/tmp/runtime-2292-architecture-final-release.log`).
The existing ordinary Release and isolated Debug evaluated restore graphs were present; no project or
dependency file changed, so the full guard used those graphs without a redundant whole-solution restore.
No architecture gate or source inventory was bypassed. The separate native provider regression is
running with the existing `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1` control; no provider result is
claimed before its actual terminal report. Final maps/diff review and hosted delivery remain open.


The separate existing Release CLI/provider acceptance project at `be33523af` returned **15
executed/passed, zero failed/skipped** with `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1`
(`runtime-2292-provider-final-release.trx`; log: `/tmp/runtime-2292-provider-final-release.log`).
Actual TRX includes all three PostgreSQL, SQL Server and MySQL legs: offline scripting, real raw
application twice, then a real Workbench host starting with `Migrate:Policy=Validate`. This is separate
real database/provider regression evidence; configuration-only candidate inspection did not access a
database. No project, provider, workflow cadence or provider matrix was added.


## Local acceptance and delivery handoff

The final maps check initially reported only the spec task-count snapshot stale
(`/tmp/runtime-2292-maps-final-check.log`). Root used the already-authorized narrow `maps` layer
refresh; only `docs/maps/spec-status-map.md` changed to 45 complete / one open. The manifest and both
findings reports were inspected and remain byte-identical, with no new findings. The restored check
returned **exit 0**, `Generated maps still describe the tree`
(`/tmp/runtime-2292-maps-final-restored.log`). All changed maps are staged by explicit path.

Root reviewed the full leaf diff and its incremental source reviews against the frozen contract,
including the final nullable-safe collision callback and lane-specific synthetic identity repair.
All three P1 outcomes have local evidence: actual Workbench supported-input projection and ambient
exclusion; selection divergence with edit/accept/fresh-capture recovery; and closed old-lane
compatibility with enrollment, privacy, drift and owned-process controls. Admission/reconciliation,
bounded raw parsing and serialization, independently correlated response validation and final input
rechecks are covered by the existing CLI and host projects. The FR/SC assignments in the proof matrix
remain separate from the actual TRX and source-review records above.

T001–T045 are complete locally. T046 remains open: ready PR review, exact-head hosted CI/maps and
resulting-main gates have not yet run for this implementation. Spec189 remains Approved. The
configuration-only inspection evidence is separate from real provider/database regression, actual
Workbench actor evidence is macOS-scoped, and injected process failure controls and the actual-child
final-drift seam are not relabeled as a whole-command cancellation/drift injection or a Windows run.
The full program and six-real-participant builder evaluation remain incomplete.

## PR #2296 review round 1

Copilot reviewed exact head `83f83c3e9fdea90cb561dca3a07349ae24854de6` and identified one
valid finding in [the worker operation](https://github.com/elsa-workflows/elsa-foundation/pull/2296#discussion_r4161750129).
Framework §2.23.3 requires logic-bearing implementations to be public sealed. The new
`CandidateEnvironmentWorkerOperation` is an operation wrapper, not the reflective host API
negotiation covered by the existing Worker InternalsVisibleTo exception. Its request, response,
constructor and operation method were already public. Root changed only the class visibility
from internal sealed to public sealed; no behavior or protocol changed.

The corrected working tree returned **1,045 executed/passed, zero failed/skipped** in the full
existing Release CLI project (`runtime-2292-cli-review-visibility.trx`; log:
`/tmp/runtime-2292-cli-review-visibility.log`) and **634 executed/passed, zero failed/skipped**
in the full existing Release architecture project (`runtime-2292-architecture-review-visibility.trx`;
log: `/tmp/runtime-2292-architecture-review-visibility.log`). These are actual local results for
the visibility correction; older hosted success is not relabeled as proof of a new pushed head.
T046 remains open pending final review, exact-head hosted checks and resulting-main evidence.
