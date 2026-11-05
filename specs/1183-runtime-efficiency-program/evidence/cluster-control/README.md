# Cluster cancellation control hook

This operator-only .NET startup hook is qualification tooling for #2531/#2831. It is not production code, a public
endpoint or timing instrumentation. Prepare its templates with the manual fixture helper, build `Hook.csproj`
explicitly and load only its DLL into an owned Workbench with `DOTNET_STARTUP_HOOKS`. Set
`ELSA_EFFICIENCY_CLUSTER_CONTROL_ROOT` to a fresh, absolute, process-writable directory.

The hook captures the actual generic host's HostBuilt event, waits for application startup, opens the real default
shell and ordinary persistence scopes, and refuses hosts without durable membership and an `eff-i68-` host id.
It waits at most four minutes for an atomically published `cancel.json` with `WorkflowExecutionId`, `ArtifactId`,
`OwnerHostId` and `CommandKey`. Before sending two identical Cancel envelopes, it requires the exact artifact pin,
Running state and placement on the distinct named owner. A forwarded command reports `Deferred`. It records actual
membership/provider/placement/dispatch receipts and waits for Cancelled state with no queue, incidents or undelivered
outbox; a failure records its exception type, message and actual status, artifact and owner.

[`two-host-qualification/cluster-controller.py`](../two-host-qualification/cluster-controller.py) drives it on two
real Workbench nodes with shared PostgreSQL. Results: cancelling a **parked** execution owned by another node passed;
cancelling a **progressing** drain on another node did **not** qualify (placement sometimes moved to the sender, and
otherwise the sender stalled before dispatch). See the program report.
