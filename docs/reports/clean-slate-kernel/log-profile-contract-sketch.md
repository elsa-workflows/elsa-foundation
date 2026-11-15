# Log profile: contract sketch for round 5 H

> **Proposed paper, 15 November 2026. Owner review pending.** Log is **planned, not designed**; no Log provider or contract split is implemented. This paper is an input to the design review, not approval for code.

The owner accepted **recorded, delivered later** on 15 November: a send is recorded atomically with its sender's commit; delivery may follow when a provider cannot transact across partitions. That choice is adopted. The four-contract decomposition below remains a proposal to review before changing APIs.

Release 1 remains Embedded and Cluster, with the same Native structured containers, Flowchart and StateMachine in both. Orleans requires proven demand. Log remains later. Round 5 A–F hardening is still queued behind round 4; this paper implements none of it. [Program #4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229) owns delivery status.

## What exists and what is proposed

The prototype's [IKernelStore](../../../prototypes/kernel-model/src/Kernel.Durable/Store.cs) combines commit, lease ownership, inbox/event routing, membership and projection operations. [IActorRuntime](../../../prototypes/kernel-model/src/Kernel.Runtime/Seam.cs) places and activates scopes. EF Core and MongoDB implement the current store; see [Storage model](../../architecture/storage-model.md) for the physical inventory and [Ownership and fencing](../../architecture/ownership-and-fencing.md) for its protocol. Those remain the as-built references.

The proposed separation is by guarantees, not SQL statements. It does not rename or split either prototype interface. A relational provider can implement all four contracts in one provider family, sharing transactions where available.

| Proposed contract | Responsibility | Relational implementation | Log profile direction — planned, not designed |
|---|---|---|---|
| Committed append | Append a fenced batch: sender state changes, consumed deliveries, recorded sends, subscriptions and journal facts become committed together, or none do. Reject stale authority; distinguish unknown, transient and definite failure. | A checked transactional batch, with instance revision guards. | Append a fenced batch to a Raft-replicated partition log; apply committed records to local state. |
| Ownership | Acquire or lose a partition's authority; stop accepting commits under superseded authority. Preserve committed recovery state. | Conditional take/release and leases, with the fence checked in the transaction. | Leader election and term-based authority, integrated with committed append. |
| Delivery | Route a stable message identity to its target partition; retain the obligation until accepted and deduplicate redelivery. Preserve one sender's order to one target. | The recipient inbox can be inserted in the sender transaction. | Record sends in the source log, deliver across partitions and deduplicate at the destination. |
| Read models | Serve all non-owner queries, including lists, counts and run inspection, from committed read models; state any lag/currentness contract. | Projected rows and a committed journal tail where required. | An exporter reads committed log records into queryable views. |

The two integration seams remain **IKernelStore** for persistence and **ownership in IActorRuntime** for placement/activation. Moving ownership from leases to leadership crosses both. The exact future API boundaries and their conformance suites are still to be designed.

## Sends: recorded once, delivered later

The sender's commit records its state changes and its send obligation atomically. If the provider can also insert the destination inbox in that transaction, it may do so: the current shared-database path already has this stronger visibility boundary. Otherwise a durable outbox obligation is part of the committed source batch and survives owner loss. Delivery retries use a message ID derived from the recorded send, not a fresh ID per retry; destination deduplication and consumption prevent repeating a committed state change.

Delivery and acknowledgement order must preserve the sender-to-target order, including after a crash or ownership move. The design must settle how a source obligation is retired only after durable destination acceptance, how deduplication survives that interval and how replay resumes. Recorded destination identity must remain pinned across caller-key reuse and physical retirement; retries cannot silently target a new run. These details are not supplied by the mapping table.

**Accepted guarantee:** the send and sender state changes are recorded once together; delivery can lag. A committed send does not promise that the destination can already read it. Backlog, lag, retry pressure and admission control are operational costs to expose and measure. No global transaction or exactly-once external HTTP/email effect is promised. External side effects retain the [design sketch's effect policy](design-sketch-draft-5.md#decisions-taken-so-far-12-november).

## Read and correlation paths

All queries made outside the owning actor use read models, including lists and counts. They do not query the instance checkpoint table directly. Owner recovery may still load its own state by identity. This is a read-path requirement for the proposal; it does not claim that every current prototype query already follows it or prescribe a new actor-table schema.

Run inspection retains the [committed view plus journal-tail contract](../../architecture/storage-model.md#viewer-read). A queryable tail is itself a committed read surface, not access to an owner's uncommitted memory. A future exporter must preserve that currentness contract explicitly rather than quietly turning the run view into a lagging list.

Event subscriptions are partitioned by **correlation key**. Publishing routes to the key's partition and produces addressed deliveries to waiting actors; it must not depend on a global subscription-table query. Registration/publish races, kept-event catch-up and duplicate matches must retain their existing semantics. This is a proposed routing boundary, not adoption of the optional correlation-actor sample or an implemented replacement for today's indexed key lookup.

## Append-compatible payloads and words

Decision 10, values by reference, remains the first option for large values. A Log design may instead need compact deltas plus periodic snapshots for its execution envelope; it must not assume an ever-growing series of whole-state appends. Recovery, state-version upgrades, retention and erasure need an explicit design before selecting that representation. Neither choice adds product storage here.

Use delivery and ownership concepts in the proposed contracts; physical schema names stay in the provider references and existing APIs remain unchanged:

| Current database-shaped term | Contract wording in this proposal |
|---|---|
| `poll` | Receive/recover committed deliveries; a provider may implement this by polling or by consuming a log. |
| `seen` | Delivery held under an ownership epoch. |
| `trial` | Isolated delivery attempt and its recovery/poison evidence. |
| `lease_until` | Ownership validity; a lease deadline is one implementation. |
| `epoch`, `position` | Retained, with their authority and ordering namespaces explicit. |

## Round 5 A/B: neutrality checks before code

The adopted A experiment compares **1,024 and 4,096 fixed virtual partitions**, provisionally 4,096. Both fence variants must retain a **gapless ordered journal per virtual partition**. Range moves preserve instance placement, journal identity and bounded issuer identities without rehashing; removal retains the accounting needed to distinguish removed positions from unwritten ones. The owner chooses final fence granularity only after flush, takeover and rebalance measurements. Neither A variant is implemented by this paper.

A range fence and a virtual partition's journal position are different scopes. A shared physical Raft log index may interleave several virtual partitions and include ownership or delivery records; it is **not** automatically that virtual partition's gapless journal position. Keep `(virtual partition, journal position)`, `(Raft group, term, log index)` and any range ownership epoch distinct. A later design must specify their mapping, replay, snapshot and removal accounting before claiming conformance; no one-to-one Raft-group/virtual-partition layout is selected here.

Round 5 B's owned PostgreSQL/SQL Server statement order is a provider optimization. It must preserve the four guarantees without requiring a relational database, a global inbox table, SQL polling, a database lease clock or atomic destination visibility in every provider. EF schema/cold-path retention in that experiment does not define the abstraction. Neither virtual partitions nor explicit SQL should close the future Log path accidentally.

## Log mapping and remaining design work

| Kernel concept | Log profile placeholder — planned, not designed |
|---|---|
| Commit | A fenced batch appended to the Raft-replicated partition log, acknowledged only after consensus commitment. |
| Fence / epoch | Raft term-based authority; the commit path must reject superseded authority. A term number alone is not a fence implementation. |
| Lease / take | Leader election supplies acquisition and loss of authority. |
| Journal | The committed log supplies journal facts; logical per-virtual-partition positions still need the mapping above. |
| Inbox | Log plus local state; no inbox-table polling. |
| Projector | An exporter reading committed log records from its recorded position. |

This is an analogy, not a Zeebe adapter design or proof. Raft's [original paper, sections 5.1–5.4](https://raft.github.io/raft.pdf) separates terms, election, replication and commitment. Camunda's [partition reference](https://docs.camunda.io/docs/8.9/components/zeebe/technical-concepts/partitions/) describes replicated partition streams; its [exporter reference](https://docs.camunda.io/docs/8.9/self-managed/concepts/exporters/) documents position-based export and at-least-once delivery. Those sources support the analogy, not Elsa conformance or a throughput claim.

Before any contract code: present this sketch to the owner, resolve review findings and specify ownership loss, committed-append acknowledgement, ordered outbox recovery, correlation routing, read currentness, payload replay/compaction and per-virtual-partition journal mapping. Preserve the accepted send guarantee and A journal requirement. The broader architecture review and design approval remain outstanding.
