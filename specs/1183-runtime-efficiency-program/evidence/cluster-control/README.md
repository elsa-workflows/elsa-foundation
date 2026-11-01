# Cluster cancellation control prototype — qualification incomplete

This operator-only .NET startup hook is unfinished qualification tooling for #2531/#2831. It is not production
code, a public endpoint or timing instrumentation. Prepare its templates with the manual fixture helper, build
`Hook.csproj` explicitly and load only its DLL into an owned Workbench with `DOTNET_STARTUP_HOOKS`. Set
`ELSA_EFFICIENCY_CLUSTER_CONTROL_ROOT` to a fresh, absolute, process-writable directory.

The hook captures the actual generic host's HostBuilt event, waits for application startup, opens the real default
shell and ordinary persistence scopes, and refuses hosts without durable membership and an `eff-i67-` host id.
It waits at most four minutes for an atomically published `cancel.json` with `WorkflowExecutionId`, `ArtifactId`,
`OwnerHostId` and `CommandKey`. Before sending two identical Cancel envelopes, it requires the exact artifact pin,
Running state and placement on the distinct named owner. It records actual membership/provider/placement/dispatch
receipts and waits for Cancelled state with no queue/incidents/undelivered outbox. The operator must stop only the
owned hosts, preserve receipts and independently prove transport/ownership and unchanged final state.

Local compilation passes after correcting asynchronous shell-scope disposal. A real Workbench smoke captures
HostBuilt, correctly refuses control in the ordinary in-process-membership host, and leaves the direct BPMN smoke
passing. This does not prove remote cancellation. The real two-host setup, publication/start driver, runtime/placement/
transport PostgreSQL bindings, durable Data Protection configuration and fault/duplicate qualification are not yet
implemented or executed. Do not count this prototype as M5 evidence. The task handoff records its remaining work.
