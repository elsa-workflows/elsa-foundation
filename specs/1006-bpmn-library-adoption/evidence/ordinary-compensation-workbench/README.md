# Actual ordinary-task compensation and old-state continuation

See [the receipt](../ordinary-compensation-host-recovery.md) and [source/artifact manifest](../ordinary-compensation-host-recovery.json). These complete module captures come from actual public-authored workflows, not installed semantic state. Identity/token stores are excluded. Decompress to a new closed file, verify its adjacent raw hash, require no live SQLite sidecars and use the actual checked reader.

The source-built standard host is supplied and owned by the operator. `Test-BpmnCompensationHost.ps1` never starts/stops it or edits runtime stores. Every phase requires explicit BaseUrl, EvidencePath and SourceRevision. Prepare creates two successful ordinary WriteLine service tasks, their separate compensation handlers and a final hold Event. B alone waits first. Later phases require original PreparedEvidencePath:

| Phase | ExpectedStage | Effect |
|---|---|---|
| Prepare | HandlerB | Author, publish and execute; capture original B handler identity. |
| Observe | HandlerB | Read the unchanged original wait after an actual owned process restart. |
| FinishB | HandlerA | Resume the original correlated B Event once; A alone must wait. |
| FinishA | Settled | Resume A once; both registrations compensated, no replay run, final hold waits. |
| Finish | Finished | Resume the hold once; complete with no waits/incidents/poison/private state. |

Every Event uses CanStartWorkflow=false, a pure WorkflowInstance.instanceId correlation and ResumeOnly delivery. Each stimulus must resume exactly the original workflow and start none. The script asserts original workflow/pins/handler identities, ordinary task results and exact one-at-a-time handler order. Checked provider observations additionally verify semantic registrations, run order, live token/scheduling association, physical wait scope/generation and actual source responsibility.

```text
dotnet <reader.dll> --inspect-compensation <runtime.sqlite> <raw-sha256> default <api.json> <HandlerB|HandlerA|Settled|Finished>
```

`test_compensation_snapshot_reader.py` accepts the same database/hash/API/stage plus reader assembly and output path. It runs six base controls, one additional live semantic-token control and one additional HandlerB replay-order control. Across fourteen actual phases this is 100 controls. Invalid variants never enter a positive host. The compiled observation-only replay-order mutation must fail that independent invalid-copy control; restore exact source and repeat normal verification.

For the legacy witness, start the exact old d81 build with fresh isolated module/identity storage and Prepare. While B waits, freeze coherent whole Runtime and Design/Publishing backups before stopping the exact owned old child/controller. Retain untouched compressed originals. Restart the old build over its original stores as a control. Start the current standard build over byte-identical copies of both original module stores with separate private identity storage. Observe before any stimulus, then finish B/A/hold on each. Both initial complete checked observations must equal the seed. All intermediate old/upgraded semantic states retain alias/schema/version one and activeChildren; final completion clears state. Do not recreate historical catalog paths or rewrite captured bytes.

Current prepared/reopened snapshots and legacy seed/old-reopened/upgraded-reopened snapshots establish equality at the actual restart boundary. Old/current subsequent newly created IDs differ legitimately; compare each against its own original pins and selected handlers. The scopes and exclusions in the receipt apply. Both actual old and new published catalog contract projections equal the unchanged frozen fingerprint, with build metadata treated as provenance rather than authored version.
