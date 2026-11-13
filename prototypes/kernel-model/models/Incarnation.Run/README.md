# Shared creation identity probe

Non-packable M0 research under [the proposed work unit](../../../../specs/1210-state-machine/incarnation-plan.md).
This is an in-memory prototype, not product architecture agreement or the StateMachine implementation.

```sh
dotnet build prototypes/kernel-model/models/Incarnation.Run -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet prototypes/kernel-model/models/Incarnation.Run/bin/Release/net10.0/Kernel.Incarnation.Run.dll --list
dotnet prototypes/kernel-model/models/Incarnation.Run/bin/Release/net10.0/Kernel.Incarnation.Run.dll
```

Use the normal build-slot wrapper. Bound each focused build to 600 seconds, runner to 60 seconds and simulation
sweep to 180 seconds, with one processor. Record commands, source and assembly hashes, inventories, exits and
failures. Mutation copies must be isolated and fail their named assertion, not merely time out or fail to build.
No service, provider host, Docker, cloud resource or previous Azure grant is used by this runner.

The 33 cases comprise twelve ordinary start/finish/receipt-expiry/Delete/recreate schedules across both modes,
root/nested placement and first-creation uncertainty; six parent-cancel-before-child-commit races; four actual
old event/timer carrier replays; one separately scheduled, non-cooperative old job completion/ticket replay; four
store/reader boundaries; two visit-isolation cases; and four job acknowledgement races. Select subsets with
`--parent-race`, `--transport-races` (also includes the job race), `--boundaries`, `--visits` or `--job-ack`.
The default run includes every listed case. An assertion failure stops it; subsequent cases are not run.

Normal workflows create the positive lifecycle witnesses. Their delete clears workflow content and ordinary
positive history, but retains an actor-ID/generation high-water mark for as long as that ID is reusable. This
is deliberate lifecycle retention, not complete physical erasure. Missing or stale targeting cannot become a
command for the replacement. Takeover preserves generation; explicit recreation advances it. Existing qualified
starts compare an immutable fingerprint of all start contents, rather than treating the intent string alone
as equivalence. Native receipt registration requires this shared identity; existing legacy receipts remain
readable/protected without fabricated generation or retention metadata.

The boundary cases explicitly distinguish setup from writer evidence: legacy v2 JSON and an exhausted
high-water mark are reader fixtures; malformed fenced commits are protocol-negative inputs. Neither claims a
historical binary rollout or billions of normal creations. Unsupported capability checks use the in-memory
implementation with that capability disabled. EF/Mongo refuse qualified inputs before lossy codecs; compiling
those adapters does not qualify live provider/schema support. Provider and mixed-kernel rollout still require
an enforced minimum-protocol or exclusive upgrade boundary before this protocol can be enabled there.

The simulation retains I10/I22 checks. It permits a new revision sequence only after observed retirement of
the preceding generation and a next-generation revision-one birth. Legacy resurrection remains forbidden.
Each ordinary simulation action checks the oracle. The async job witness awaits the actual old driver after
replacement and checks that stale or unqualified tickets never execute another body. Work already executing
outside the owner can finish after cancellation; this protocol promises neither undo nor exactly-once effects.

Event proof concerns pinned carriers emitted for actual subscriptions, not business-key re-resolution or
broadcast recipient snapshots. Trigger cohorts, complete guard/phase recovery, values/scopes, bounded child
address naming, namespace ownership/start authorization, production API, real process/provider restart,
StateMachine/Studio and compensation composition remain separate gates. The prototype uses a full-parent
length-prefixed child namespace to prevent cross-partition punctuation aliasing; it does not settle the
shared-kernel owner's outstanding child-address design or create/delete product policy.

The isolated source-control packet and broad regression results belong on the exact-head PR. A passing local
inventory is not a merged product result; earlier failures remain in their original raw packets.
