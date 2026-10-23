# Unified execution evidence workspace

> Historical research snapshot: 17 October 2026. Program implementation has continued since this packet. See [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) for current delivery status and owner decisions. The 19 October update verifies presentation behavior only.

Authoritative program: [Foundation #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892). This workspace contains isolated architecture experiments and evidence, not production APIs or a ratified architecture. The evidence milestone is consolidated below. Research recommendations remain proposals for owner ratification, not production acceptance.

## Resume

1. Read the program and latest issue checkpoint. Check source, current claims and experiment processes before restarting work.
2. The owner authorized protocol exceptions on 17 October 2026 to complete isolated research despite unrelated red main CI. [Recorded exception](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6234014040). This is not a passed CI result or production migration ownership.
3. Current checkpoints: #2896 bounded shared-work evidence is complete; #2898 root verification passed for the pinned BPMN host and service-task bridge; #2901 passed its updated 29-assertion package witness; #2900 passed its corrected built-in/Orleans comparison; #2913 passed 142 assertions after budget admission and invocation-binding fixes.
4. Read the reviewed recommendation, acceptance audit and presentation verification before starting the proposed production specification slice under #2905. Refresh current main and ownership first.
5. For each experiment retain source/package pins, commands, observed outputs, failure controls, review, and untested boundaries. Source inspection or a simulated diagram is not executable proof.

## Evidence ledger

| Witness | Evidence | Current acceptance |
|---|---|---|
| Current source/ownership baseline #2895 | [Reviewed baseline](current-source-baseline.md), full remote report in issue | Complete as a read-only audit; not runtime validation |
| Historical three cases | [Remote source archive](https://github.com/elsa-workflows/elsa-foundation/issues/2895#issuecomment-6233432122) | Historical prototype; not a replacement for current witnesses |
| Shared work #2896 | [WorkProof evidence](https://github.com/elsa-workflows/elsa-foundation/blob/4b464e1637483e89a11f632f2c99c5779cb4e0de/tools/experiments/UnifiedExecution.WorkProof/evidence.json) and [failure matrix](https://github.com/elsa-workflows/elsa-foundation/issues/2896#issuecomment-6233491510) | Bounded witness complete: 111 assertions, 43 process identities, four negative controls. Aggregate record admission and configuration deadlock findings were addressed. Local SQLite/process limits remain. |
| Operational BPMN #2898 | [BPMN proof README](https://github.com/elsa-workflows/elsa-foundation/blob/4b464e1637483e89a11f632f2c99c5779cb4e0de/tools/experiments/UnifiedExecution.BpmnProof/README.md) and [BPMN evidence](https://github.com/elsa-workflows/elsa-foundation/tree/4b464e1637483e89a11f632f2c99c5779cb4e0de/tools/experiments/UnifiedExecution.BpmnProof/evidence) | Root's final verification passed at the pinned interpreter source. Includes the BPMN host and local ETL service-task bridge; no current Elsa integration or broker-backed guarantee. |
| Provider substitution #2900 | [Provider proof README](https://github.com/elsa-workflows/elsa-foundation/blob/4b464e1637483e89a11f632f2c99c5779cb4e0de/tools/experiments/UnifiedExecution.ProviderProbe/README.md) and [provider evidence](https://github.com/elsa-workflows/elsa-foundation/blob/4b464e1637483e89a11f632f2c99c5779cb4e0de/tools/experiments/UnifiedExecution.ProviderProbe/evidence/provider-proof.json) | Corrected comparison passed for both policies, including effect payloads, worker overlap, stale-owner rejection and canonical scope checks. Root verified 21 source and 48 archived-log hashes; cleanup confirmed. Local cluster scope only. |
| Independent model #2901 | [Rule-model proof README](https://github.com/elsa-workflows/elsa-foundation/blob/4b464e1637483e89a11f632f2c99c5779cb4e0de/tools/experiments/UnifiedExecution.RuleModelProof/README.md) and [model evidence](https://github.com/elsa-workflows/elsa-foundation/blob/4b464e1637483e89a11f632f2c99c5779cb4e0de/tools/experiments/UnifiedExecution.RuleModelProof/evidence/proof.json) | Root rerun passed 29 assertions after package-artifact loading and state-trace validation fixes. Bounded cross-review findings were addressed. No package authenticity policy or terminal model cancellation is proved. |
| Agent collaboration #2913 | [Framework semantics probe](https://github.com/elsa-workflows/elsa-foundation/blob/4b464e1637483e89a11f632f2c99c5779cb4e0de/tools/experiments/UnifiedExecution.AgentFrameworkProbe/evidence.json) and [candidate composition evidence](https://github.com/elsa-workflows/elsa-foundation/blob/4b464e1637483e89a11f632f2c99c5779cb4e0de/tools/experiments/AgentCompositionProof/evidence.json) | Actual MAF package probes passed. Root rerun passed 142 native-composition assertions after budget admission and tool invocation fixes. Deterministic usage units; no live LLM guarantee. |
| Architecture recommendation #2904 | [Working recommendation](architecture-recommendation.md) | Reviewed research recommendation with explicit accept/revise/reject dispositions, governance reconciliation and a proposed next spec slice; awaits owner ratification. |
| Interactive HTML #2911 | [Open presentation](presentation/index.html) · [verification](presentation/verification.md) | Five model lenses, breadcrumb drill-down and six labeled execution simulations; scoped evidence links and real browser controls/responsive checks. 19 October isolated Chrome direct-file/offline and reduced-motion checks passed; see the verification record. |

## Candidate provider seam

The existing Foundation actor provider separates execution addressing and mailbox entry from durable commit fencing. The provider witness now compares the unchanged WorkProof Sequence and Flowchart definitions through direct `CompositionCoordinator` hosting and an Orleans execution grain. It exercises overlapping Flowchart workers, two-silo failover, capability placement, durable receipt recovery and stale-owner rejection. The corrected comparison passed after review found a newline scope/key collision and evidence/cleanup gaps. The adapter rejects delimiter-bearing scope fields before mutation and uses length-prefixed actor identity. Shared fixture HMAC encoding remains explicitly non-production.

The candidate adapter uses Orleans membership and placement as its fleet authority, not a competing Elsa liveness registry. Capability selection checks the pinned execution requirement. Durable owed work remains outside notification delivery: an Orleans RPC timeout does not establish whether an application effect happened. Retry uses stable identity, commit receipts and destination idempotency. The experiment's qualitative setup notes describe dependency and operational tradeoffs, not performance or cost measurements, and its one-machine SQLite/loopback setup is not production provider conformance.

Sources: [placement filter contract](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/src/Orleans.Core/Placement/IPlacementFilterDirector.cs), [custom filter sample](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/docs/site/src/content/docs/grains/snippets/placement/CustomPlacementFilter.cs), [delivery guarantees](https://learn.microsoft.com/en-us/dotnet/orleans/implementation/messaging-delivery-guarantees).

## Completion guard

Do not freeze a universal graph, move joins into the storage kernel, equate ETL partitions with persistence partitions, or count an agent composite as the independent model. Do not claim full BPMN conformance from hosting tests. The recommendation must accept, revise or reject each proposed shared boundary, including failed assumptions and migration consequences. HTML must label verified traces versus simulation and preserve accessible offline navigation.

All five executable witness streams passed their stated local scenarios after review. The current evidence supports bounded research conclusions only; lifecycle and production integration gaps are explicit recommendations for revision. None establishes a released migration prerequisite or authorizes product ownership changes.

## Working recommendation

[Architecture recommendation](architecture-recommendation.md) records the reviewed research proposal, subject to owner ratification. It records candidate model/composition/provider boundaries, operational BPMN foundations, agent resource bindings and adoption gates.

[Acceptance audit](acceptance-audit.md) maps current issue requirements to evidence and remaining work.
