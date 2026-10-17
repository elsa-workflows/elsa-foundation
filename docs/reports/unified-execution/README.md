# Unified execution evidence workspace

Authoritative program: [Foundation #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).
This workspace holds architecture experiments and evidence, not production APIs or a ratified architecture.

## Resume

1. Read the program and latest issue checkpoint. Check source, current claims and experiment processes before restarting work.
2. Owner authorized protocol exceptions on 17 October 2026 to complete isolated research despite unrelated red main CI. [Recorded exception](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6234014040). Do not interpret that as a passed CI result or as production migration ownership.
3. #2896 shared-work witness is complete. #2900 provider comparison is the next integration leaf; BPMN bridge, agent composition and independent-model review continue as bounded research work.
4. Retain the entire goal: shared activity/ETL work, operational BPMN, agent collaboration, provider substitution, independently packaged execution model, reviewed recommendation, interactive HTML.
5. For each experiment retain source/package pins, commands, observed outputs, failure controls, review, and untested boundaries. A source inspection or simulated diagram is not executable proof.

## Evidence ledger

| Witness | Evidence | Current acceptance |
|---|---|---|
| Current source/ownership baseline #2895 | [Reviewed baseline](current-source-baseline.md), full remote report in issue | Complete as read-only audit; not runtime validation |
| Historical three cases | [Remote source archive](https://github.com/elsa-workflows/elsa-foundation/issues/2895#issuecomment-6233432122) | Historical prototype; not a replacement for new witnesses |
| Shared work #2896 | [Failure matrix](https://github.com/elsa-workflows/elsa-foundation/issues/2896#issuecomment-6233491510) | Complete bounded witness: 111 assertions, 43 OS process identities, four negative controls; aggregate admission reviewed and configuration deadlock fixed |
| Operational BPMN #2898 | Existing #2735 ownership/parity and upstream library | Root rerun passed pinned interpreter host, restart and Sequence-style adapter comparison; custom host, not production integration |
| Provider substitution #2900 | Orleans 10.4.0 / source d114d7522a53b8cce4c6b97d44293e5c7273d766 candidate | Real multiprocess comparison required |
| Independent model #2901 | Separately packed finite-rule model, unchanged shared substrate | Root verified 26 assertions; package/worker pins corrected; cross-review pending |
| Agent collaboration #2913 | [Pinned MAF source analysis](https://github.com/elsa-workflows/elsa-foundation/issues/2913#issuecomment-6233507776) | Root rerun of real MAF 1.25.0 package passed three semantic probes; native composite recovery implementation underway |
| Architecture recommendation #2904 | All five witnesses required | Pending |
| Interactive HTML #2911 | Follows evidence review | Pending |

## Candidate provider seam

The existing Foundation actor provider separates execution addressing and mailbox entry from durable commit fencing. The experiment must retain this distinction. A real Orleans adapter should use Orleans membership/placement as its fleet authority, not maintain a competing Elsa liveness registry. Capability selection must inspect the pinned execution requirement and refuse an incompatible activation; type-level grain version compatibility alone is not proof that a worker can execute a pinned activity implementation.

Orleans 10.4.0 exposes `IPlacementFilterDirector` and silo metadata for candidate filtering. This is a source-grounded implementation lead, not yet a passed adapter. Keep durable owed commands outside notification delivery: an Orleans RPC timeout does not establish whether an application effect happened. Retry with stable identity, commit receipts and destination idempotency. The provider experiment must kill a real silo process, recover the same execution on another, and reject an obsolete owner at the durable commit boundary.

Sources: [placement filter contract](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/src/Orleans.Core/Placement/IPlacementFilterDirector.cs), [custom filter sample](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/docs/site/src/content/docs/grains/snippets/placement/CustomPlacementFilter.cs), [delivery guarantees](https://learn.microsoft.com/en-us/dotnet/orleans/implementation/messaging-delivery-guarantees).

## Completion guard

Do not freeze a universal graph, move joins into the storage kernel, equate ETL partitions with persistence partitions, or count an agent composite as the independent fourth model. Do not claim full BPMN conformance from hosting tests. The recommendation must say accept, revise or reject for each proposed shared boundary, including failed assumptions and migration consequences. HTML must label verified traces versus simulation and preserve accessible offline navigation.

## Working recommendation

[Architecture recommendation](architecture-recommendation.md) is provisional until the combined witnesses and independent review complete. It records model/composition/provider boundaries, operational BPMN foundations, agent resource bindings and adoption gates.

[Acceptance audit](acceptance-audit.md) maps the current issue requirements to evidence and remaining work.
