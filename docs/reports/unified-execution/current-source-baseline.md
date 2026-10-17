# Unified execution current-source baseline

Program #2892; task #2895. Read-only investigation, 2026-10-17.
Source inspected: `15feed9e0be709348602e054cca85a33f0f76873` (freshly fetched Foundation main).
Historical prototype: `4cc63596d`, based on `e4a699879791fb7eb2bc1c6874f772f4a7fe5355`.
No runtime tests were executed in this audit. Source mechanisms are not execution proof.

## What changed the next action

1. Main now has Essentials/, Extensions/ and Studio/. Earlier research paths under src/essentials are historical. Follow current files below; #2501 retains consolidation ownership.
2. Main CI is red at the inspected SHA: [run 39020537150](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39020537150), Build & test, `DurableDrainCycleTests.An_HTTP_triggered_Durable_sequence_of_200_WriteLines_completes_under_the_default_drain_cycle_bound`. Generated maps passed on that run. #2293 remains open. Do not claim a green main, begin production edits, or open a merge-ready implementation against this baseline. The audit and immutable evidence preservation do not assume main is healthy.
3. Bpmn.Semantics v0.3.1 is latest as queried. Its release explicitly excludes breaking #42, the fix for #32; #33 is included via #41. Both issue closures therefore do not satisfy #2735's requirement for a release containing both fixes. The release notes place the omitted changes in 0.4.0; availability must be freshly checked. [Release](https://github.com/valence-works/bpmn/releases/tag/v0.3.1).
4. Existing agent tooling is substantial prior art. `Extensions/Agent/src/Core/Services/AgentTurnServices.cs` contains a turn orchestrator, tool policy/approval integration and an `InMemoryAgentTurnStateStore`; `Contracts/AgentTurnContracts.cs` defines paused turns and an eight-step default budget. Reuse candidates exist, but this does not establish durable distributed collaboration. The Workflows extension offers explanation/troubleshooting/change-proposal capabilities; it is not automatically the new agent-collaboration composition.
5. No Elsa 4 equivalent of the Elsa 3 BackgroundActivityInvoker worker protocol was found in the inspected Essentials/Extensions C# source. RunAsynchronously hits are import models/conversion. This is bounded discovery, not a proof that no alternative implementation exists anywhere.

## Mechanism ledger

All paths below are relative to the inspected commit and can be resolved through its GitHub tree.

| Concern | Current source | Candidate reuse and limit |
|---|---|---|
| Actor hosting | `Essentials/src/Workflows/Runtime/Core/Contracts/IWorkflowExecutionActorProvider.cs` | Provider activation/passivation and per-execution serialization; vocabulary is workflow-specific |
| Local mailbox | `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executions/InProcessWorkflowExecutionActorProvider.cs` | Key is persistence partition plus execution ID; bounded in-memory dedup cache is not durable receipt authority |
| Distributed route | `Essentials/src/Workflows/Runtime/Distributed/Elsa.Workflows.Runtime.Distributed/Services/DistributedWorkflowExecutionActorProvider.cs` | Placement is best-effort routing; commit fencing remains independently authoritative |
| Durable cross-node inbox | `Essentials/src/Workflows/Runtime/Distributed/Elsa.Workflows.Runtime.Distributed/Contracts/IExecutionCommandTransport.cs` | Leased/acknowledged workflow commands; not a generic partition-work contract |
| Scheduler queue | `Essentials/src/Workflows/Runtime/Core/Contracts/IWorkflowSchedulerWorkQueue.cs` | Per-workflow FIFO and claimed work, with checkpoint consumption; not an ETL graph scheduler |
| Ownership | `Essentials/src/Workflows/Runtime/Core/Contracts/IRuntimeExecutionOwnershipService.cs` | Monotonic per-execution fence and heartbeat; placement token is a separate concept |
| Durable commit | `Essentials/src/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs`; `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/RuntimeCheckpointCommitter.cs`; `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs` | State, outbox, receipt and fence pattern is reusable evidence. State-change set explicitly contains workflow/activity/bookmark/incident objects; do not declare it model-neutral |
| Persistence isolation | `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executions/ScopedWorkflowExecutionCommandExecutor.cs`; `PersistenceWorkflowExecutionPartitionAccessor.cs` in the same directory | Scope is bound to envelope partition. This is not an ETL data partition; capture tenant/authority separately and verify worker propagation |
| Durable child work | `Essentials/src/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs`; `Services/WorkflowDispatchCompletionEnricher.cs` | Pinned child, input/context snapshot and durable parent resume precedent. Not already independent worker partition aggregation |
| Activity invocation | `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs` | Native scheduler invocation. Offload requires an explicit durable invocation/result boundary |
| BPMN | `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Activities/BpmnProcess.cs`; `Directory.Packages.props` | Current in-repo engine; Model/Interchange remain 0.1.1-preview.19, no Semantics reference. Existing owner controls switch |
| Agentic work | `Extensions/Agent/src/Core/Services/AgentTurnServices.cs`; `Contracts/AgentTurnContracts.cs` | Turn/tool/approval policies are prior art; in-memory paused state cannot prove restart recovery |

## Ownership and external gates

- #2735: open, assigned to sfmskywalker; body parks the switch until 21 November, later parity comments report upstream work. Read-only analysis may proceed. Do not infer migration authority from upstream issue closure or reactivate the switch. Refresh owner direction before the hosting witness.
- valence-works/bpmn #32 and #33: closed; release inclusion differs as above. #42 adds a FaultedIterationId host requirement. Pin a compatible source/library contract deliberately.
- #2501: source consolidation remains its own program; no whole-peer-program freeze is declared. Publishing/cutover is not this program's work.
- #2771: current parity work does not implicitly reactivate unrelated parked goals.
- #2747: operator retry/recovery overlap; link rather than create duplicate retry semantics.
- #1307: sequential timer/resumption drain and concurrency concerns; relevant to host scheduling, not a prerequisite for BPMN library migration and not a measured claim from this audit.

## Candidate contract questions and witness assignments

1. #2896 must test explicit invocation ID, attempt/ownership epoch, pinned implementation and input snapshot, authority/persistence context, result/effect envelope, duplicate/conflicting receipt handling and durable continuation. Compare against the current commit/outbox and DispatchWorkflow patterns; do not extract the toy Store API unchanged.
2. #2896 must distinguish root execution identity from independent ETL partition-work identity. The coordinator remains an execution-instance actor; workers need not be actors. Test bounded batches, checkpoints, idempotent destination effects, late completion after cancellation and preserved Sequence/Flowchart behavior.
3. #2898 uses actual Bpmn.Semantics only after a pinned compatible library/parity baseline is available. The native/embedded equivalence and operational task/timer/incident/alteration witnesses remain unproved. Production migration stays with #2735.
4. #2900 needs real provider substitution with one fleet view, capability-aware placement, durable lost-route recovery and commit-time stale-owner rejection. Framework activation guarantees do not replace fences. Record exact network-failure coverage; local SQLite workers are insufficient evidence.
5. #2913 must inspect existing Agent provider/tool/policy code before introducing alternatives. Define message rounds, private/shared memory, knowledge snapshot/reference versions, effect authority and durable budget/recovery. Use typed resource bindings distinct from Flowchart connections. A successful native composite is not a fourth execution model.
6. #2901 remains a genuinely independent semantic module test; #2904 cannot freeze contracts until all relevant witnesses, including #2913, are evaluated.

Cross-cutting unanswered questions: receipt replay after owner changes; durable approval-to-effect correlation; cancellation versus business error; worker context isolation; schema/implementation version compatibility; retention of large outputs and conversations; notification loss versus authoritative owed-work state. Each belongs in a concrete witness, not a universal graph abstraction.

## Durable historical archive

The nine original prototype files are preserved in [issue archive](https://github.com/elsa-workflows/elsa-foundation/issues/2895#issuecomment-6233432122) as a deterministic ZIP encoded in the comment, with per-file SHA-256 manifest and reconstruction instructions. ZIP SHA-256: `dc9f6e278f3241278f6962845526a41dcec222f31077b942b4e6f56eaa3f1677` (16,747 bytes).

Verification performed: fetched posted comment, compared body, decoded the ZIP, checked archive digest and each of nine file digests. No credentials, generated binaries or execution databases included. This preserves source, not ephemeral historical databases. Historical 115 assertions/68 workers/negative controls remain reported prior results, not rerun results. The original report is preserved on [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6232845636).

## Disposition

Baseline findings and source preservation are complete; independent final report review and task checkpoint remain required before closing #2895. Current main is not a green production base. Keep the goal active: none of the new witnesses, reviewed architecture recommendation or HTML artifact is complete. Recheck main CI and the BPMN release gate at the next transition rather than interpreting this timestamp as continuing truth.
