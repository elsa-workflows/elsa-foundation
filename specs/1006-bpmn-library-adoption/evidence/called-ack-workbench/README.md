# Actual caller commit before source acknowledgment

See [the receipt](../called-ack-workbench-recovery.md) and [source/artifact manifest](../called-ack-workbench-recovery.json). One actual public-authored noninterrupting caller/child pair is used for two distinct cuts: notification zero and empty terminal success one. The caller's original warning Event keeps it live across both cuts and resumes only after both recoveries.

The standard and opt-in probe builds use source parent `81f6ae7a47497a9f1f5704bd6b5f9b21fa1566a0`, with that full explicit SourceRevisionId. The probe additionally compiles the source-bound test-only recovery feature. Standard build metadata proves the feature is absent. Released Bpmn.Model/Semantics/Interchange 0.5.0 bytes match both builds. Use the [isolated module-store host configuration](../legacy-workbench-called/README.md), Balanced mode, private identity/token storage and an explicitly owned free port. Test-only control directories must be owned private directories.

Prepare with `Test-BpmnCalledAckHost.ps1 -Phase Prepare`; it uses only normal public catalog/authoring/publication/start APIs, returning original workflow IDs, pins and Event names. Its other phases are `Emit`, `FinishChild`, `FinishWarning` and read-only `Observe`. Each accepts `-PreparedEvidencePath`, explicit `-BaseUrl`, `-SourceRevision`, `-EvidencePath`, and the expected stage `Prepared`, `NoticeAccepted`, `TerminalAccepted` or `Finished`. Each Event has CanStartWorkflow=false and canonical pure WorkflowInstance.instanceId correlation; every stimulus must resume exactly its intended workflow and start none.

For the notification cut, atomically write `called-control-plan.json` into the probe's configured ControlDirectory:

```json
{"caseId":"notice-zero","intentKind":"Elsa.Activities.DispatchWorkflow.NotifyControl","childWorkflowExecutionId":"<actual child id>","ordinal":0}
```

Invoke `Emit -ExpectedStage NoticeAccepted`. The normal handler uses the intent-only dispatcher overload. The probe reads the selected provider's real existing Delivering source before forwarding unchanged, then verifies the same owner/fence/deadline/content after the handler returns. It never constructs a claim/context or changes RequiresDurableSource. It atomically writes `<caseId>/reached.json` and waits before the processor records a result. A separate normal API `Observe` plus an immutable checked EF backup must prove committed inbox acceptance, exact branches and physical waits while that source remains Delivering. The marker alone is insufficient.

Stop and await only this controller's exact owned probe child. Remove only the test feature from the host configuration, start the standard no-probe build over the same original Runtime and Design/Publishing stores, and allow natural claim expiry. Neither runtime rows nor clock/claim timestamps are edited. Observe `NoticeAccepted` again after the original source becomes Delivered under a higher fence, then compare the entire checked Facts collection and separately the original immutable source Intent.

For terminal one, stop that standard process, re-enable the test feature and arm `caseId=terminal-one`, `intentKind=Elsa.Activities.DispatchWorkflow.ResumeParent`, `ordinal=1` for the same actual child. Start the probe over the same stores and invoke `FinishChild -ExpectedStage TerminalAccepted`. Repeat the independent committed-inbox/ordinary-completion/live-warning-wait check, owned crash, standard restart and natural expiry. The normal standard process must acknowledge the same source under a higher fence without changing Facts. Finally invoke `FinishWarning -ExpectedStage Finished`; it completes the original warning once.

The five Runtime snapshots are notice held/recovered, terminal held/recovered and final finished. The sixth complete database is coherent Design/Publishing. Identity/token storage is excluded. Original catalog directory strings remain historical provenance; do not recreate paths or rewrite bytes. Each database's adjacent JSON records complete raw/compressed hashes and integrity checks.

Build the checked reader with locked restore, decompress to new private closed files without sidecars, then use:

```text
dotnet <reader.dll> --inspect-called-ack <runtime.sqlite> <raw-sha256> default <api.json> <cut-marker.json> <phase>
```

Use `held` for `notice-held`/`terminal-held`, `recovered` for the corresponding recovered snapshots, and `finished` for the final snapshot. Notice stages use `notice-cut.json`; terminal/final stages use `terminal-cut.json`. The API and checked JSON filenames match each snapshot prefix. `comparisons.json` verifies complete recipient Facts equality across each retry, source Intent equality, higher fencing, actual result-count progression and the original warning's unchanged physical generation across both cuts.

Facts explicitly cover named workflow states, all parent/child activity states, authoritative dispatch, incidents, physical waits and non-target outbox model projections. The target source's delivery projection necessarily changes and its complete immutable Intent is compared separately. This does not compare every SQLite row/module table, raw EF source revision or scheduler/durable-value inventory. It does not count adapter entries. The source's `DeliveryAttemptCount` is zero while the first result is unrecorded and one after standard completion; it is not a count of handler invocations.

The terminal natural-takeover file contains 42 pre-expiry samples of timestamp/status/fence/recorded attempt count, followed by Delivered after the original deadline. Held checked data independently records the original owner. No claim is inferred solely from the sample count. This is a pre-delivery-result cut; loss after an already committed acknowledgment is a separate generic-source qualification.

`test_called_ack_snapshot_reader.py` runs nine controls per phase: actual snapshot, wrong hash, foreign full API pin, foreign marker commit/fence/scope, hidden owed orphan, exact dispatch without declaration, and a caller with its committed inbox removed in private copies. It requires closed coherent variants, integrity/no-sidecars, unchanged hashes and no raw diagnostics. A compiled observation-only omission of the inbox requirement incorrectly accepts the private missing-inbox variant and fails its control; exact restoration passes. Earlier fixtures pass 53 compatible reader controls without restamping their historical host proofs. The positive hosts never receive a synthetic source, claim, transport, state or private invalid variant.
