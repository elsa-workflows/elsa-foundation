# Orleans hosting composition

Status: report-boundary experiment verified locally for [#3098](https://github.com/elsa-workflows/elsa-foundation/issues/3098), under [#2905](https://github.com/elsa-workflows/elsa-foundation/issues/2905) and [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892). Production membership mapping remains proposed. This is not a shipping provider or an amendment to ADR 0078.

Foundation source: `33c33214f16ca8c157d2af99ab31faecb90cb9a9`. Orleans source: `d114d7522a53b8cce4c6b97d44293e5c7273d766`, release 10.4.0. The [published server package](https://www.nuget.org/packages/Microsoft.Orleans.Server/10.4.0) has .NET 10 dependencies. Its use here selects an experiment version; production support follows adapter qualification.

## Decision being tested

Use Orleans' selected clustering provider as the liveness authority, with a report facet owned by the same Elsa/Orleans membership adapter. Resolve that provider's public `IMembershipTable` from the silo's host container. Read its authoritative table directly for Elsa Fresh reads; combine its exact member identities with committed reports from the facet. Keep EF checkpoint fences and durable scheduler/outbox/transport records authoritative for native execution.

The experiment uses the official Orleans ADO.NET clustering provider and an owned PostgreSQL database. A report table in that database carries no independent membership heartbeat or liveness decision. Sharing a database alone proves neither visibility nor membership correctness: the executable probe must test the report publication/read boundary. No existing `EfClusterMembership` lifecycle runs alongside Orleans in this candidate.

The governing meanings remain in [ADR 0078](../adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md#invariants-every-membership-or-actor-provider-must-preserve) and the [membership contract](../../Essentials/src/Cluster/Core/Contracts/IClusterMembership.cs). This report records compatibility findings rather than redefining them.

## Alternatives and source evidence

| Alternative | Finding | Disposition |
|---|---|---|
| Read the selected Orleans membership table and a provider-owned report facet | Public `IMembershipTable.ReadAllAsync` returns complete membership and table version; it has no dynamic report field. Supported shared DI makes the selected table reachable without the cached membership service. | Probe this boundary first. Prove complete-read failure behavior and report visibility separately. |
| Use Orleans silo metadata for Elsa reports | The inspected API configures metadata at host startup; the exposed cache is read-only. It does not establish dynamic publication visible to every later Fresh read. | Insufficient for `PublishReportAsync`. |
| Adapt the current Elsa EF provider directly into Orleans' table contract | Current EF rows have per-member revisions. Orleans requires table-wide version/etag CAS for membership mutations, and different heartbeat/version behavior. The existing EF lifecycle would compete with Orleans if both drove joining and heartbeats. | Not a direct reuse. A new store mapping and an explicit ADR decision would be needed before reversing the current membership direction. |
| Use Orleans cached/gossip membership as Elsa Fresh | A locally refreshed snapshot does not establish the authoritative read and post-publication visibility contracts. | Do not substitute it for Fresh. A bounded cached path may be considered later. |

Pinned upstream sources: [membership table contract](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/src/Orleans.Core/SystemTargetInterfaces/IMembershipTable.cs), [shared silo DI](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/src/Orleans.Runtime/Hosting/ISiloBuilder.cs), [ADO.NET registration](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/src/AdoNet/Orleans.Clustering.AdoNet/AdoNetHostingExtensions.cs), [metadata registration](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/src/Orleans.Runtime/MembershipService/SiloMetadata/SiloMetadataHostingExtensions.cs). Foundation's [EF member entity](../../Essentials/src/Cluster/EntityFrameworkCore/Entities/ClusterMemberEntity.cs) and [store](../../Essentials/src/Cluster/EntityFrameworkCore/Stores/EfClusterMembershipStore.cs) describe the current independent provider.

## Mapping that must survive implementation

| Concern | Proposed direction and required proof |
|---|---|
| Identity | Configure an explicit stable Elsa host ID. Bind reports to cluster ID and the complete Orleans silo address including generation; never join a report to host ID alone. Equal host IDs across incarnations must remain distinguishable. Before serving Elsa work, refuse competing live incarnations with an admission operation serialized against heartbeat renewal. A separate mapping CAS after a membership read is insufficient: the incumbent can renew between that read and the CAS. Concurrent starts must not both win. The mapping must have no separate heartbeat or liveness clock. |
| Fresh reads | Use independent authoritative reads with no inherited stale database transaction. Return all selected membership entries with conservative report interpretation, or fail the entire read. A missing/unrecognized report must not grant capabilities. A read begun after publication returns must observe that report revision or a later one for that same incarnation. An older already-started read may retain its old snapshot. |
| Report writes | Store a versioned payload and monotonic revision keyed to exact incarnation; commit before returning. Define concurrent update refusal and stale-incarnation handling. Do not mutate Orleans heartbeat or table version merely to publish a report. |
| Liveness and lapse | Preserve Elsa's lapse-before-reader-expiry rule using both wall and monotonic elapsed time. A cached Orleans status is insufficient. Trace successful local heartbeat writes and their start timestamps; if the adapter cannot safely rejoin the same silo, stop the host and restart with a new generation. This is a design obligation, not behavior proved by a two-silo join. |
| Status and shutdown | Do not cast Orleans `Dead` to Elsa `Left`: crash suspicion/death is not proof of graceful shutdown. Preserve conservative counting until the expiry rule permits exclusion. Bind an explicit graceful-leave observation to completion of Elsa worker/host shutdown; keep heartbeats/report access available while draining. |
| Missing timing metadata | Orleans membership does not carry Elsa's per-entry expiry setting. Proposed adapter restriction: an immutable, versioned policy per cluster in the same authoritative backend declares heartbeat/expiry/skew settings. Every Elsa-serving host must match it before admission; mismatches fail startup. Fresh reads can then assign the known expiry even when a capability report is absent. Prove concurrent policy initialization; an incompatible policy update is refused while existing incarnations remain, rather than reinterpreting their expiry. Do not guess a missing member's expiry. This stricter optional-provider configuration does not change the built-in provider's settings. |
| Cleanup | No membership/report cleanup may remove an incarnation while a reader must still count it. New reports must not attach to a replacement incarnation, and late old-generation writes must not overwrite its reports. |
| Container ownership | Select membership once on the host; shells resolve that same member. Verify startup failure and shutdown disposal. The optional actor provider must reject conflicting explicit provider selections in either registration order; core fallback registration remains valid. |

Orleans' [status enum](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/src/Orleans.Core/Runtime/SiloStatus.cs) and [membership manager](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/src/Orleans.Runtime/MembershipService/MembershipTableManager.cs) must be checked when implementing these transitions. The latter stamps a local heartbeat before awaiting the selected provider; preserving that timestamp and independently tracking local lapse remains adapter work.

These mapping rules are a proposed implementation contract. The report probe does not implement the stable-host CAS, policy admission, heartbeat observation or shutdown gates. Production integration must test them before advertising a valid `IClusterMembership` provider. If supported lifecycle hooks cannot establish them, retain the probe result and resolve the specific contract/ADR conflict before registering an adapter. A successful report test alone cannot waive this gate.

The admission race is material: Orleans' heartbeat operation deliberately preserves the membership table version/etag, so checking that version does not fence an intervening renewal. A backend-specific operation or a provider-owned serialization boundary covering both admission and every relevant membership renewal/status write must be designed and tested; generic `IMembershipTable` plus a report table does not supply it. An advisory lock used only by the claimant is insufficient. Any proposed wrapper must also prove that Orleans resolves the wrapped selected table and that no write path bypasses the serialization boundary. This is a remaining production compatibility gate, not a reason to weaken duplicate-host semantics.

## Execution and qualification boundaries

The actor provider serializes a whole native execution. It does not reinterpret Sequence, Flowchart joins, activities or accepted results. Existing durable command ownership moves from the unacknowledged transport envelope to scheduler work, then through committed checkpoint/consumption and outbox intent to the next idempotent work item. A lost transient Orleans route must leave that work discoverable. Retain `IExecutionCommandTransport` and its frozen wire unless a separate reviewed change approves otherwise.

After composition is viable, reuse the [native qualification fixture](../../Essentials/tests/Workbench/Tests/NativeHostingFixture.cs) and its REST-published Sequence → Flowchart → held parallel background bodies → final native continuation. Split provider configuration/observations internally without weakening the built-in EF-specific transport evidence. First qualify two real host processes with owner loss and survivor completion; separately replay the unchanged original checkpoint under a live higher fence. Quiescent routing recovery is not stale-commit proof.

## Probe and evidence

The [executable probe and commands](../../tools/hosting/Orleans.Membership.Probe/README.md) use Orleans 10.4.0 and the observed PostgreSQL 16.15 backend. Root independently built Release with locked restore, ran the default probe, both negative controls and the restored default. The [source/binary hashes and four receipts](../../tools/hosting/Orleans.Membership.Probe/evidence/20261020-root.json) bind those pre-commit runs to their actual input files and record backend image identity. They do not pretend the worktree's base commit already contained the new probe. Required review/hosted delivery evidence is recorded on #3098 and its PR.

| Observed case | Result |
|---|---|
| Two actual silos, one selected ADO.NET table per host | Both public table instances returned the same two active silo identities. |
| Held older read across publication | A real PostgreSQL repeatable-read transaction retained revision 1 while later projections from both hosts saw revision 2 and its removed capability. |
| Stale report revision | An update using revision 1 after revision 2 was refused. |
| Actual same-name restart | Silo A stopped and restarted on the same endpoint/name with a different Orleans generation. The new report key began unknown; its published report stayed separate from a later write to the old key. |
| Missing or unsupported report schema | The projection remained unknown and credited no probe capabilities. |
| Report backend read failure | Renaming the owned report table made the complete projection fail; it returned no partial fleet. The table was restored before cleanup. |
| Remove authoritative report refresh | The stale-snapshot mutation exited 1 at `fresh-report-visible`; the default implementation passed. |
| Remove generation from identity keys | The key mutation exited 1 at `new-incarnation-report-unknown`; restoration passed. |

Both normal runs passed all twelve probe assertions; both negative controls failed for their intended reason. These are repeated executions of the same cases, not additive test coverage. Every receipt records completed container cleanup. Bundled schema scripts were checked byte-for-byte against the pinned upstream revision; the included MIT license retains its terms with one trailing empty line normalized. The probe verifies loaded schema hashes before executing them.

The projection carries raw Orleans status and an intentionally small probe payload. It is not an Elsa `FleetView`, status/liveness mapper or placement-capability query. Late old-key writes are issued by the probe driver after the old silo stops, so key isolation is demonstrated without claiming stale live-owner fencing. Two silos in one OS process do not establish separate-process failover, full Elsa membership conformance or native workflow recovery. No production adapter is registered.

## Outcome and next production gate

The chosen backend can support the tested membership/report read boundary without making the report facet a second heartbeat authority. This resolves that experiment; it does not establish that a generic Orleans table plus reports is already a valid Elsa membership provider.

Before production registration, implement and prove the backend's admission serialization against every renewal/status write, immutable timing-policy admission, local lapse and shutdown ordering described above. Then run the full membership conformance suite and native process qualification. Keep these as explicit dependent acceptance, with the report-boundary probe reusable as evidence; do not add another semantic engine or weaken current contracts to make the adapter fit.

The [native built-in qualification receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3087#issuecomment-6282572580) remains pinned to implementation merge `7d4d2183171e7ee1bfcb18132d05a88133f7d8d1`. Historical receipt/HTTP failures remain causally unexplained, and physical external effects remain at least once.
