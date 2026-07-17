# Quickstart: Verify Successful DispatchWorkflow Wait and Resume

Use the absolute SDK path required by this worktree:

```bash
/usr/local/share/dotnet/dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
```

## Required successful-wait scenarios

1. Pause immediately before and after the parent wait checkpoint. Verify dispatch, bookmark, suspended activity, start outbox, and marker are all-or-nothing and no child is visible before commit.
2. Replay the wait checkpoint and child-start delivery. Verify identical dispatch/child/bookmark/start identities and one logical child.
3. Complete a child with one disclosed JSON output and one redacted output. Verify the child terminal checkpoint contains dispatch Completed and one parent-resume intent; the redacted entry has no value.
4. Replay after uncertain child terminal acknowledgement. Verify the previously committed resume intent is reused exactly even if the active output-capture policy differs.
5. Pause parent-resume delivery after actor acceptance but before bookmark consumption. Verify the outbox claim becomes retryable with positive backoff and does not exhaust.
6. Deliver duplicate resume work before and after consumption. Verify one callback, one bookmark deletion, one parent activity completion, one `Completed` outcome, and one equivalent result.
7. Recreate Groundwork/runtime services at every checkpoint, claim, output, consumption, propagation, and acknowledgement boundary. Drain the global resumption pump and verify convergence.
8. Re-run fire-and-forget and unsupported-kind tests. Verify `Dispatched` behavior and policy-selected failed/final behavior are unchanged.

## Completion evidence

- The result contains the deterministic child execution ID, `Completed`, and ordinally ordered JSON-safe output entries.
- Declared types and redaction flags survive serialization; redacted values occur zero times in outbox, checkpoint, diagnostic, and result JSON.
- Wait bookmarks have no expiry and no activity-owned timeout is introduced.
- Architecture tests show no #680 fault/cancel propagation, #681 dead-letter/redrive, #682 TestRun, #683 distributed transport, broker, Studio, or construct-only workflow-definition activity expansion.
- Any unrelated baseline failure is reported separately; a partially failing suite is never described as green.
