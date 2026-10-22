# Native publication and checkpoint components

Implementation checkpoint for #3181, 2026-10-22, based on `230eb5ab536c70b41694d7009910940496df64cc`. This is partial T027–T029 work. It does not complete native integration or any T030–T032 acceptance gate.

## Implemented boundary

- The public compiler contract returns a canonical immutable artifact and validated vocabulary count. Publication accepts a bounded literal finite program and freezes the trusted installed implementation identity once per contribution. It rejects dynamic/secret sources and malformed or unsupported literals without exposing their contents.
- The ordinary typed `RunRules` activity freezes facts, resolved round limit, executable identity and opaque native resume target. Runtime does not recompile or reselect the implementation. Invalid explicit round limits retain a native wait without inventing a Rules invocation.
- The native checkpoint component captures the actual bookmark and registration IDs independently. It stores the destination and Start intent together, preserves unrelated state changes and emits cancellation using retained metadata after trigger payload retirement, including changed nonterminal children of a cancelled parent.
- Resume checks the actual consumed native receipt, provider/type/registration and delivery deduplication identity. Native command IDs and Rules delivery IDs are distinct. It compares all canonical terminal content, including facts, against the retained Rules terminal. Tests exercise the actual type-erased stateful interface and default native JSON serialization.

## Verification

Commands run from the implementation worktree, with the normal build-slot wrapper and root-owned serial builds:

```sh
dotnet restore Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj -p:RestoreLockedMode=false -v minimal
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj --no-restore -v minimal
dotnet restore Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -p:RestoreLockedMode=false -v minimal
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --filter 'FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~PublicApiFilesGuardTests|FullyQualifiedName~Every_admitted_Rules_project_resolves_only_its_reviewed_EF_closure' --no-restore -v minimal
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

The 31 new focused compiler/publication/checkpoint/activity cases passed. The full Rules suite passed 222 tests with zero failures or skips after restoring the control below. The shared checkpoint-fixture cleanup then passed all 9 focused cases. Selected architecture/API/Rules-EF checks passed 86 tests with zero failures or skips. Local map generation and the committed solution-filter freshness check passed. Both findings reports were inspected: no runtime-to-design reference signals or missing catalog links; the four new Design-to-runtime references are the publication compiler and activity/runtime contracts. Generated maps remain ignored under the current generated-map policy. [Log receipt](native-component-receipt.json) records raw log paths and SHA-256 digests.

The architecture dependency graph emits existing analyzer/style, package-pruning and `System.Security.Cryptography.Pkcs` advisory warnings. No warning suppression or dependency change was made to hide these; a passing test result is not a claim that this graph is warning-free.

## Result-comparison control

Temporarily removed only the canonical terminal byte comparison in `RunRules.ResumeAsync`, leaving fingerprint, route and receipt checks intact. `Resume_rejects_altered_result_facts_even_when_their_digest_is_recomputed` failed because no exception was thrown. The altered result had its own valid recomputed digest: shape validation alone did not detect substitution.

The source was restored byte-for-byte in a `finally` block (SHA-256 `f309cec26d4fe33e29501c3b8a5818e54675eac0a5ed0aaba405c3f92c542856`). The subsequent full 222-test run passed. This is a component result-validation control, **not** the later T032 proof-before-acknowledgement control.

## Review and remaining gates

The first review pass corrected derived zero limits for over-vocabulary input, actual-address retention, changed-active-child cancellation and native-vs-model delivery identity assumptions. Root reviewed the final delegated components and ran the checks. A second read-only pass found no further defect in the reviewed compiler/pin/route/result-comparison surface. Shared checkpoint setup was extracted to avoid repeated arrange blocks.

One concrete integration finding remains open: `WorkflowResumeBookmarkSchedulerWorkHandler` records exceptions from typed resume as activity faults. A temporary Rules-reader outage inside the current `RunRules.ResumeAsync` therefore must not be described as retryable recovery. T028/T030 must resolve and prove that boundary before runtime registration. No whole-integration clean review or readiness for merge is claimed.

Intent handling, delivery hosting, durable native proof and acknowledgement, runtime registration, actual CLR catalog publication, persisted Sequence/Flowchart continuation and REST journeys remain pending. The component fixtures construct native state and use store doubles; they do not prove actual scheduler/persistence/parent behavior. Existing four-module package/process/provider evidence applies to its recorded immutable inputs, not automatically to these new modules or changed compiler assembly. Full six-module packaging and retained-run upgrade/rollback remain open. External effects remain at least once.

### Next recovery investigation

Read-only tracing found an existing same-work-item redelivery path: `TryResolveClaimedResume` and `ActivityAttemptActivationClaimer.PrepareTypedResumeRedeliveryClaim` retain the consumed trigger payload and start a fresh attempt. A narrow explicitly retryable resume failure may be able to use the existing scheduler retry policy instead of creating a second retry scheduler. This is a candidate, not an implemented guarantee: the default retry policy is Noop and durable retry also requires the appropriate poison/resumption store composition. The next slice must inspect competing shared-runtime work, establish the actual host policy, and test an outage after trigger consumption followed by recovery of the same work item. Permanent identity/content mismatches must still fail. ETL's similar read during resume is not evidence of safe unavailable recovery.
