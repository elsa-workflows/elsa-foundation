# Shared commit-clock probe

Proposed, non-packable, in-memory-only slice of the [reviewed plan](../../../../specs/1210-state-machine/commit-time-plan.md).
It does not implement StateMachine or approve product kernel/provider architecture.

`--list` inventories eight schedules. They use the actual workflow actor, durable worker, in-memory store and
controlled simulation wire: delayed application, before-apply loss, applied/lost acknowledgement, cold takeover,
an actual stale commit executed after takeover, pending-only future-state termination by an older model, and
an unqualified clock capability. Each action checks existing kernel invariants. Unsupported direct-batch refusal
is separately labelled protocol setup; no receipt or provider result is seeded. The ordinary unqualified-store
case uses a different actor ID, avoiding any claim that delete/recreate identity has been fixed.

The actor row optionally carries immutable clock facts. The store stamps requested facts at its synchronous
atomic apply point; the successful worker cache uses that same attested time. Same-turn requests share one fact.
Resolving an absent fact creates nothing; models can release only model-owned facts. Workflow terminality has
one separate owner fact, including an older model terminating opaque future private state with only the earlier
clock opt-in. Repeated terminal controls preserve it. Fenced direct-protocol batches must also refuse forged timestamps, altered retained facts and release of the
workflow-owned terminal fact. Model-facing lookup is asserted after lost acknowledgement and cold takeover.
Scope/version identity is not solved by a clock reference.

Existing EF/Mongo implementations advertise no clock support and explicitly refuse clock-bearing writes before
using codecs that would omit the new field. Their clock/storage/migration qualification remains open. Forwarding
stores preserve capability. No existing ordinary model must opt in; no product default or schema is selected.

Build this focused project through the normal build-slot wrapper, one worker/no shared compiler, bounded600s:

```sh
dotnet build prototypes/kernel-model/models/Clock.Run -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet prototypes/kernel-model/models/Clock.Run/bin/Release/net10.0/Kernel.Clock.Run.dll --list
dotnet prototypes/kernel-model/models/Clock.Run/bin/Release/net10.0/Kernel.Clock.Run.dll
```

Bound each runner to60s; no retry, service, host, provider, Docker, cloud or old Azure job is implied. Isolated
source mutations must demonstrate named failures for pre-commit stamping, dropped persisted facts, restamping
accepting an unsupported write, trusting forged/changed facts and failing model-facing resolution. Preserve actual hashes, inventories, exits and failed/unrun stages.

This first cut does not yet qualify native terminal receipts, the retained T4 failure, metadata lookup against
failed-start JSONnull, actual provider clocks, parent-notice counts, full statecharts, Studio or M0 adoption.
Those require the terminal slice and remaining plan gates, not this shared clock runner. No result is asserted
by this README until an exact-source packet is recorded on its PR.

The latest-main integration also exercises a clock-enabled real worker through the delayed-commit decorator:
the state and immutable timestamp commit before acknowledgement, and the cache keeps that same timestamp when
the delayed acknowledgement arrives. Lease guards and reply-deadline regressions must pass on the combined head.
