# ETL journey: backend preparation

T020/T022 implement Studio service selection and the native run-reference projection. T023–024 now implement the [native → ETL navigation and durable URL](navigation.md), with component proof. T021's representative actual RunEtl publication/replay verification remains open. T025 now implements [retained incident retry and exact native attempts](retry.md) with68 focused component/client controls. A physical dependency failure after effects, restart and the real Studio journey remain T026. These results do not establish that journey or wider ETL/provider acceptance.

## Studio service selection

The new service editor reuses the existing paged workflow picker and selected workflow's publication slots. It reads the exact requested executable/reference before applying a complete immutable worker pin and explicit fault policy through the ordinary draft edit. It opens the existing create dialog in a separate tab and guides authors through a Sequence workflow → RunEtl child → existing published pipeline selector → normal save/review/publish. It does not generate or publish a hidden worker. Older human-only backends retain their existing experience: service selection additionally requires the advertised `bpmn-authored-service-publish` relation.

Two controls failed before implementation: valid service metadata was unrecognized and a listener-bearing task could be rebound. All104 affected frontend controls now pass (zero failures/skips), including41 new controls. These cover closed/duplicate/future metadata, retained unrelated properties, native child/listener refusal, read-only behavior, absent capabilities, stale/retired/foreign publication facts and responses arriving after backend/account/tenant/selection retirement. The normal editor integration uses the real workflow picker, undo, redo and save with mocked HTTP; the retained source/version/hash and fault policy reach the saved serviceTask. Its initial broad assertion also observed the editor's pre-existing artifact-list reads and was removed; it did not identify a new service-picker scan. Typecheck and targeted lint pass. The initial test command was launched outside the Studio workspace and could not find vitest; the recorded failing/passing controls use the installed runner from the workflow package directory. These are component results, not a live-host/browser acceptance claim.

From `Studio/src/essentials/Elsa.Studio.Workflows/Client`, run the installed Studio Vitest runner against `bpmnServiceBinding.test.ts`, `bpmnServiceSelection.test.tsx`, `bpmnDirectAuthoring.test.tsx`, `bpmnElementInspector.test.tsx`, `directBpmnEditor.test.tsx`, `bpmnAdapter.test.ts` and `RunWorkflowPicker.test.tsx` under `src/__tests__/`.

Those frontend results and the receipt's source blobs belong to `437a8f7af09df99af50e9fd7f9fd5f32e253c548`. The subsequent copy-only correction names the actual composition choices in the existing create dialog: Sequence with a RunEtl child, rather than a RunEtl root. No behavioral suite was repeated for that hint. The actual Studio creation/recovery journey remains open.

## Authored service publication

The authored projection accepts one serviceTask with the complete owned `nativeService` binding described in the [publication contract](../../contracts/authoring-publication.md). It preserves the existing human format and authoring writer, strips owned metadata only from the executable graph, and delegates native material/closed-input/profile/replay validation to the delivered runtime. The publisher checks the exact selected live Published source, tenant, definition/version/artifact/label and `workflows/runtime:read` permission. Unknown, duplicate, partial or incompatible metadata and policies refuse publication.

Backend source `2334ae470` contains the implementation and its controls. The two positive service-publication cases failed before implementation because the human-only projection refused the binding. With the implementation, all194 interchange tests passed (zero failures/skips), including27 new service-publication controls. An additional complete serviceTask XML → JSON → immutable-version projection control was then added; all61 affected publisher/interchange cases passed (zero failures/skips). Its first compile attempt used record mutation on immutable classes; the fixture was corrected to construct the service element directly before the passing run. No product change was needed for that correction. Both feature/capability registration controls also pass after advertising the additional service relation.

Commands, from the repository root:

```sh
dotnet test Essentials/tests/Activities/Bpmn/Interchange/Tests/Elsa.Activities.Bpmn.Interchange.Tests.csproj
dotnet test Essentials/tests/Activities/Bpmn/Interchange/Tests/Elsa.Activities.Bpmn.Interchange.Tests.csproj --filter 'FullyQualifiedName~BpmnDirectBindingInterchangeTests|FullyQualifiedName~BpmnAuthoredDirectPublisherTests'
```

The full194-case result precedes the extra round-trip test; it is not a claim of a195-case full run. Existing source-aware runtime replay controls retain their separate evidence. Actual RunEtl publication/replay and worker pinning/recovery must still be observed. The [qualification receipt](backend-qualification.json) records source blobs and results; raw logs remain local until the completed ETL journey's evidence publication.

## Authorized native run reference

Source `d60e368d7252b9d660bb0e76f0979bca8841180c` registers the ETL-owned `etl.run-reference` version1 activity-state projection. All53 focused ETL native activity/checkpoint tests pass, including11 new controls. The projection returns the existing run identity/result address, excludes captured values even for sensitive-data grants, preserves the address after completion-payload retirement and refuses mismatched workflow/tenant/partition/pin/node/registration context. The state writer is unchanged. This is a pure checkpoint-fixture result; actual authorized API/browser linking remains unproved.

## Integration and cadence

These backend changes include main `94be29ece1b3fd55b534a6a9043d3b2b94f5e33c`. That main's Build & test and Generated maps checks passed; Studio browser shard2 failed. The configured branch rules do not list that browser shard as required, so it does not trigger the required-base-check stop condition. Its failure is retained and is not described as green. No actual browser or broad compatibility matrix was repeated for this backend milestone; the earlier source-bound receipts remain historical evidence. Final candidate/review/CI/resulting-main gates remain open.
