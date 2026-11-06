# Source-bound planning record

**Date**: 2026-11-06. **Current state**: Reviewed requirements, plan/API contracts and 20 unchecked implementation tasks. No production implementation or runtime acceptance. The dated planning observations below preserve their original source and review state; later entries supersede earlier proposals.

## Exact inputs

- Worktree: /Users/sipke/.codex/worktrees/2892-preparation-retention/elsa-foundation.
- Branch: agent/codex/2892-preparation-retention; source HEAD fd0fdb21eacabe690183ff761432746e9c7e680c.
- Requirements spec SHA256 f04e6f8a8fd9b38c2ff8a035d7c21b0793a1e9083a31b114797aa822150a24ae.
- Checklist SHA256 31c36886c1ffe629a2dbf6bcdbb909a5b1fb5efc42efb823b09168594fccc4dd.
- Parent reported clean requirements review after FR-017 precision correction and clean independent acceptance at those exact hashes.
- Requirements [request](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6641754588), [intake](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6641948798), [handoff](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6676494892), [reservation](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6676842316).

## Workflow and governance

Read speckit-plan, both relevant constitutional gates, review checklist, existing spec/template and source seams. setup-plan.sh --json resolved the existing feature.json directory correctly and copied the plan template. Its BRANCH output was empty because common.sh uses explicit feature state rather than Git branch inference; git branch --show-current independently verified the assigned branch. No branch switch/hook rerun or AGENTS change.

before_plan/after_plan git commit hooks are optional and deferred under the authorized no-commit instruction. after_plan agent-context hook is disabled; AGENTS remains untouched. No mandatory planning hook was skipped.

## Fresh ownership/overlap observations

Read-only gh-codex queries confirmed:

| PR | Actual head / state | Relevant overlap |
| --- | --- | --- |
| [#3904](https://github.com/elsa-workflows/elsa-foundation/pull/3904) | 7cf4e425143a2320141ea81c980506d75d515610; OPEN draft; app/elsa-workflows-codex; main base; no labels | Runtime.Core and Runtime PublicAPI; runtime catalog/registration; EF DbContext/provider snapshots. Its delivery-source/completion implementation and schema family remain owned there. |
| [#3912](https://github.com/elsa-workflows/elsa-foundation/pull/3912) | Initial observation f064e47b7aa11f26f70309f4b002c7db5dac4796; refreshed actual head 4fb0654c575907046ac5f00a81cb0c64e6265ead, OPEN draft, same branch/main base | The newer head includes source beyond spec1217 and protected logical publication scope. Supervisor relayed owner confirmation: no preparation/GC changes planned; coordinate before reserved-file edits. |

Observed comments distinguish #3904 component proof from full-feature/full-PR acceptance; neither it nor typed completion delivers preparation retention. This record is a source/ownership observation, not review approval or merge eligibility of either PR. The parent retains our reservation and handles coordination; no other branch/worktree was edited.

## Source anchors checked

- Runtime/Core/Models/WorkflowExecutableSourceReference.cs: Published/TestRun only; generic IsLive depends on retirement/expiry.
- Runtime/Core/Contracts/ReusableActivityRuntimeStores.cs: actual source root write and separate template maintenance; template ID is not workflow artifact ID.
- Runtime/Core/Contracts/IWorkflowExecutableRootWriteLeaseManager.cs and implementation: actual full-root-set grant vector, material closure and provider fencing.
- Memory source root writer: native-only graph path; EF source root writer: ResolveMaterialClosure, physical grant/source/transaction checks.
- EF template .ReferenceFences/.Maintenance: physical hash-claim/incarnation/revision and selected tuple checks.
- GC: source+runtime artifact closure; template retention currently seeded only from executionProtectedArtifactIds; initial/final guarded reads.
- Start resolver: generic requiredScope matching; dispatcher keyed replay precedes ordinary source resolution.
- Export factory/importer: Published-only provenance.
- RuntimeArtifact entity/configuration: existing Scope/ContentJson/IsRetired/Revision/IncarnationId and scoped identity projections.
- Existing canonical Publishing receipt/journal contracts distinguish durable outcome from candidate/activation state.

## Review limits and next gate

The corrected plan proposes one permanent unanchored metadata document in the existing canonical source-reference backend, existing material stores and genuine canonical-owner/provider fence admission. R1's ordinary-reference anchor/member and no-DDL choices are superseded before implementation. These choices require independent plan review. No ABI/schema version is approved and no current provider support is asserted. Generate tasks only after a clean plan review; heavy qualification remains MySQL-first and sequential under the actual grant.

## Refreshed overlap verification

At actual #3912 head 4fb0654c575907046ac5f00a81cb0c64e6265ead, GitHub content bytes matched all four relayed SHA256s:

| Reserved file | Held fd0 bytes SHA256 | Verified newer-head bytes SHA256 |
| --- | --- | --- |
| Source-reference model | 1cff91955c4a9de372745d89b3da42c6bd40a0d3ad1cdb8ffc0f4d88a8e88e0f | 3a0d7e9f0f15d153878dd67e7c10e2c0106e6fcb316a77f2166a6a2e0f1267ee |
| Comparer | 42f8ef39095917907c7a7e8427227cda7c308c74d9a1cb4cd1e310427ebd6708 | dc7d7b49188e08c8d808d98398acaf0301aab74aa6d4edd9e81772979324474a |
| EF source-reference store | d52cfebc2cdcc64e5dca6272ae85b01d9bf228dcdd72ad96c8b7f727c6720bb8 | 50d625e99f68afec051bce4852067c5182baca87e4bfec95baead0f1525c6037 |
| EF source-root writes | 6040c285d2c7a26ffafc4d88c918a62dba3fea6f9d6c37c05a20142241e2be0d | 8df856932500c18dcfa9412d86abb47ecc69acda51d758facdd8270f67956ade |

Read-only diffs show the newer model adds PublicationScope; comparer includes it; generic fenced writing refuses caller-asserted PublicationScope; protected SaveScopedPublication stamps actual physical scope. This is not preparation retention. Implementation must explicitly reconcile these invariants, not replace newer files with this fd0 baseline. No checkout, fetch or branch mutation was performed.

The relayed local #3904 proposal initially named patch SHA dba71b37696f300bcd15918cfbe4eaea2686c8b19088bf65baa7b687a8309582. The file had advanced when inspected: /tmp/elsa-3583-t010-ef-publication-draft/candidate.patch SHA256 c1d58172e9e84ab09a005a690a3229d3ed02de8e07505d162054d0b516146f2c. It now adds the 27-line EfWorkflowExecutableSourceReferenceStore.MessageInterest.cs partial. Its internal StageMessageInterestFenceAsync requires actual scope and an owner transaction, readable RuntimeArtifact schema, expected physical incarnation, no pretracked row, and revision/schema CAS; it performs no independent Save/transaction. This is an unmerged local proposal, not the published #3904 head or accepted behavior.

The helper alone is not interest eligibility: all effective interest/publication selectors must still require genuine Published authority and refuse Preparation. Preserve owner source/family policy and coordinate helper integration with its owner; no changes were applied here.

## R2 actual-owner source and corrections

Read-only git/source checks verified /Users/sipke/.codex/worktrees/agents-publication-integration/elsa-foundation at branch agent/codex/3519-publication-integration, HEAD 3b617af8c86b65d4f21ca37aa2cee35fdf257f78, tree 242f630d48bab6d6b3a90d584f3fc54d975629b4. This unqualified/unpublished integration supersedes the earlier dd8 source observation; it is not merged capability or T066 acceptance.

The exact owner handoff /tmp/3519-publication-owner-fence-handoff.json matched SHA256 72dfd8cab4f293aeff2d3f0bbc71ec1a7145746e5c5f734d0c43a0f45bf11ed7. Recomputed source bytes matched its eight file pins:

| Actual owner/source file (repository-relative) | Verified SHA256 |
| --- | --- |
| Essentials/src/Workflows/Publishing/Core/Contracts/ISourcePublication.cs | 2853e6ab7accf5097a8b178ded9f514d967f419042f271ece18c2f7f19988515 |
| Essentials/src/Workflows/Publishing/Core/Models/SourcePublication.cs | 3ea77e5e3c5c421047a6a315e412317b053b49f7c89688cdc789a2db91a40ae4 |
| Essentials/src/Workflows/Publishing/Persistence/EntityFrameworkCore/Stores/EfSourcePublicationStore.cs | 8b531b0a8356c2e4992b32eee66e921dc3880e5e519a4ca01904473741bed4db |
| Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/SourcePublicationCoordinator.cs | 4a15a27bc11a0fa3b01f5bd995619f72613cc284d8d2391965ace6eb16c9777d |
| Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/WorkflowSourcePublicationAdapter.cs | 2f8a8ab5ec4df56ebc33a82c2b414012165c540576e84c1a3fcd1d222833cf33 |
| Extensions/Agent/src/Specialists/Design/Services/AgentSourcePublicationAdapter.cs | dd9a73e020871377465f591c67c216be1bfaae59f3cad6a12e90a6ecbc9f38d9 |
| Essentials/src/Workflows/Publishing/Persistence/EntityFrameworkCore/Services/EfSourceActivityPublicationCommand.cs | cb91c6391cdfda0211650e511a6c386673320ec1285e9b98ec9f76ea5d6f5dd8 |
| Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfWorkflowExecutableSourceReferenceStore.RootWrites.cs | 6040c285d2c7a26ffafc4d88c918a62dba3fea6f9d6c37c05a20142241e2be0d |

Only production ISourcePublicationStore found is EF. Canonical operation Revision+State CAS exists; true operation insertion incarnation and through-commit issuing guard do not. Review-entity incarnation and Specialist inference turn admission generation cannot supply this authority. Actual progression is Captured0→Reviewed1→Reserved2; actual domain receipt can precede ledger receipt/release repair. Agent publication linearizes in Activities Design, so Runtime-only write cannot close retention.

Existing Essentials/src/Persistence/EntityFramework/EfSharedTransaction.cs matched SHA256 c79a755bf9e02673356baf9aad1e60ab8c9c01be97cb892d5216c7fa237628eb. Its fresh-context, same-provider/exact-string, one-type-once, sole-commit/non-owning-operation, unsaved/rollback-only and unknown-commit constraints were inspected directly. Dedicated staged preparation acceptance is required; existing owner Write and Runtime RootWrites transaction guards stay intact.

Actual-owner technical feedback was relayed through Root and incorporated as proposed semantics: true operation insertion incarnation; no new claim/takeover policy; historical accepted/verification stamps across legal revision progression; stable canonical decision fingerprint; winning prepared-envelope Profile/ContentFingerprint distinct from Runtime physical selection fingerprint; typed adapter roots after Join; same-file shared participation and genuine independent-store guard; post-GC terminal replay. This feedback is not approval or implementation.

The two C# API artifacts are held unchanged for targeted independent review at runtime-preparation-api.cs SHA256 9cc6f6b831065c1233f4456d644aa2b273e98aa4ad70df4ce56b8ad8b7a25676 and ef-participation-api.cs SHA256 6a361822b285e465a9366841deb4f0b4d374f012ab8d4c4e5f1bf5fd12a93b25. Plan R1 findings/dispositions are in plan-review.md. No production implementation has been written against this proposal.

## Latest #3904 overlap handoff

Verified /tmp/elsa-3583-inmemory-active-interest-draft/owner-prerequisite.patch SHA256 37db5b4f32ed27ad073289fa1ceb49d58260c3c3ab9ee2c989f048836ce6c96a. It adds only the internal UsesMessageInterestTuple(selected, clock) accessor to the nonpartial root-write lease manager. Its actual base/new files matched 9f540f6cd9ea2e1207235523d3350fe6ee0ce14860237c465416385b9ced2504 → a1049060ec64d8b809e4b0270e30686110229524f94d0b4bef4539d68cc496bd.

The separate proposed EfWorkflowExecutableSourceReferenceStore.MessageInterest.cs matched SHA256 e2bd4db48df1087b1ed63af435423240204b94491bd0660317c0707d361fd4e5. Thus the newest handoff does not replace the reserved base source-reference file; the small root-manager overlap still requires ordinary exact-source reconciliation. These files are local unmerged proposals, not proof of published #3904 support. No patch was applied to this worktree or another branch.

Root subsequently confirmed Messaging owner authorization is ONLY that frozen internal UsesMessageInterestTuple accessor, actual material-store and TimeProvider reference equality, with no writes/public ABI. Messaging adds it in its own branch. This leaf may adopt the same minimal delta after normal plan/tasks flow; a changed base requires minimal reconciliation and new hash, never overwrite or cross-owner worktree edits. Nothing is applied during this planning stage and no whole-T066 wait is inferred.

## Approved plan and task generation

Parent subsequently reported full R2 standards/acceptance clean, all three held API reviews clean, and the original correctness finder CLEAN on the focused R3 correction. The sole R2 P2 (mutable template CAS revision in stable selection identity) was resolved. The actual canonical owner reviewed the design under the existing assignment. This is delegated #2892 approval to implement the reviewed leaf, not implementation/provider evidence or T066 acceptance.

The approved R3 document pins remain unchanged:

| Artifact | SHA256 |
| --- | --- |
| plan.md | 16bfde2def04660e6caa6021b068c988ab94884c7b820ee02eb993964a588422 |
| contracts/preparation-retention.md | 2fcb1f9046ea96cb273cf3e784f084cf772234bedd86ab2a189d108bf9f419d9 |
| data-model.md | 4c6966332f7170a27ec15e9f48216b30d35c5449ae206b02eac1ad2e5663d1a4 |
| research.md | 6284adb3dce25787f725e8532244f7461e3071670ac72a0e845ce0b48ecdf979 |
| quickstart.md | 154ba3a1f041d813a17e282c335025a06285cb83e861f8ffd3b857e300abebcd |
| plan-review.md | 6b8318934b63036e80c63ec19a73cde25a6b46022bf3cb67345b2d582df473ab |

Their earlier awaiting-review statements describe the frozen submitted packet; this later record governs current approval. Runtime/EF API artifacts remain exactly 9cc6f6b831065c1233f4456d644aa2b273e98aa4ad70df4ce56b8ad8b7a25676 / 6a361822b285e465a9366841deb4f0b4d374f012ab8d4c4e5f1bf5fd12a93b25.

Spec metadata-only transition: spec.md SHA256 f04e6f8a8fd9b38c2ff8a035d7c21b0793a1e9083a31b114797aa822150a24ae → c74086b9b76eff584805fd8cb48c66e89e03232d9c59fe46042516fb88e759f1. Only the Status line changed from Draft to Approved under delegated authority; reconstructing the old status reproduces the old hash exactly. Requirements, journeys, FRs/SCs and architecture-status text are unchanged. Checklist stays 31c36886c1ffe629a2dbf6bcdbb909a5b1fb5efc42efb823b09168594fccc4dd.

Read speckit-tasks skill/template and relevant test/lifecycle rules. setup-tasks.sh --json returned this absolute feature directory, the repository tasks template and research/data-model/contracts/quickstart inputs. Existing feature.json was used; no branch/feature hook rerun or AGENTS update. before_tasks/after_tasks speckit.git.commit hooks are optional and explicitly deferred until parent publishes the reviewed packet; no mandatory task hook is configured. Risk-based paired tests follow the task-specific instruction/ADR 0080, with causal negative proof; no new TDD policy or unresolved cadence question is inferred.

Generated tasks.md has 20 unchecked implementation tasks: shared setup/foundation 5, US1 6, US2 3, US3 2 and final cross-cutting 4. Planning preflight is separately recorded as completed prose, never implementation completion. T001 requires actual newer-main/shared-owner reconciliation before C#; the real Publishing issuer/incarnation/transition/typed-root dependency and both actual durable SQLite compositions are explicit. Execution still requires source/task review and the then-current grant; this planning turn ran no build/test/host/Docker, changed no C# production source and performed no commit/push/GitHub mutation.

## Task review and governance correction

The 20-task packet at SHA25601a93b174e50d7c349db96267d442d9b9a150cddb7ac181958b349a364492c34 received independent correctness and acceptance review. Both found only T020's stale author-never-merge routing; the parent corrected it to current org protocol and applicable standing owner authorization while retaining all required checks, independent review, human holds, branch protection, exact-head and resulting-main gates. This is a documentation-only correction, not merge permission or implementation acceptance. No implementation task is checked; the reviewed API inputs and plan R3 remain unchanged.

## Portable draft publication base

Before publishing the reviewed planning packet, the isolated branch fast-forwarded from fd0fdb21eacabe690183ff761432746e9c7e680c to current main cdfc79a0c3c181b0782719437cab5466d961690f. No foreign branch was changed or assumed merged; T001 still requires actual shared-owner source reconciliation before C# implementation. The corrected task file SHA256 is 4b5a36e26b37770a8930d9693dbccdb133f2ff60094c68606c1fd11b4c7f89bb, with its original-finder focused confirmation clean. The two held API design files remain 9cc6f6b831065c1233f4456d644aa2b273e98aa4ad70df4ce56b8ad8b7a25676 and 6a361822b285e465a9366841deb4f0b4d374f012ab8d4c4e5f1bf5fd12a93b25. No compiled API/provider capability, full implementation review or T066 acceptance is implied.
