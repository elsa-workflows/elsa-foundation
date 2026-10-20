# Unified execution

Status: active, owner-authorized 18 October 2026. Steward: Sipke; delivery lead: this program's Codex workroom.

[Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) is the authoritative queue and progress record. The [program board](https://github.com/orgs/elsa-workflows/projects/62) mirrors the native hierarchy and separates delivery status from verification; issue comments and acceptance remain authoritative. The [approved continuation](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6249319967) and [BPMN authority amendment](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6249325156) supersede the earlier research-only gate within this program's scope. They do not reactivate unrelated parked work.

## Outcome

The target is support for native workflows, first-class and embedded BPMN, and ETL with shared durable infrastructure and tooling. Models retain their own semantics; native composites retain their own composition rules. Agent collaboration builds on native composition and existing Elsa agent integrations. New execution models and hosting providers can extend the foundation without kernel model-name switches.

## Delivery sequence

| Stage | Delivery gate | Tracking |
|---|---|---|
| Native proof | Delivered: native durable offload, recovery and cancellation. Preserve historical evidence limits. | [#2914](https://github.com/elsa-workflows/elsa-foundation/issues/2914), [PR #2915](https://github.com/elsa-workflows/elsa-foundation/pull/2915) |
| Lifecycle compatibility | Existing native and BPMN consumers demonstrate durable lifecycle behavior before neutral API extraction. Completed leaf: [#2960](https://github.com/elsa-workflows/elsa-foundation/issues/2960), [spec 242](../../specs/242-execution-lifecycle/spec.md). | [#2905](https://github.com/elsa-workflows/elsa-foundation/issues/2905) |
| Combined parent outcome | Intentional fault and ordered notifications use one durable callback with legacy compatibility and recovery proof. Completed leaf [#2967](https://github.com/elsa-workflows/elsa-foundation/issues/2967), [spec 126 D6](../../specs/126-runtime-child-parent-notification/spec.md). | [#2905](https://github.com/elsa-workflows/elsa-foundation/issues/2905) |
| Released BPMN adoption | Complete shared-library parity/release prerequisites; preserve frozen contracts; one interpreter for direct and embedded execution. | [#2735](https://github.com/elsa-workflows/elsa-foundation/issues/2735), [#2907](https://github.com/elsa-workflows/elsa-foundation/issues/2907) |
| Hosting and extension | Native built-in qualification [#3056](https://github.com/elsa-workflows/elsa-foundation/issues/3056), [spec 249](../../specs/249-native-hosting-conformance/spec.md), is complete: [exact-main implementation evidence](https://github.com/elsa-workflows/elsa-foundation/pull/3087#issuecomment-6282572580), [documentation closeout](https://github.com/elsa-workflows/elsa-foundation/pull/3096#issuecomment-6283256492) and [claim release](https://github.com/elsa-workflows/elsa-foundation/issues/3056#issuecomment-6283259687). The bounded Orleans composition probe [#3098](https://github.com/elsa-workflows/elsa-foundation/issues/3098) shipped in [PR #3099](https://github.com/elsa-workflows/elsa-foundation/pull/3099#issuecomment-6287456349) with exact-main verification. Further Orleans work [#3101](https://github.com/elsa-workflows/elsa-foundation/issues/3101) is [owner-deferred](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6287993256); its checkpoint is preserved unmerged in [#3108](https://github.com/elsa-workflows/elsa-foundation/pull/3108). Orleans and a production fourth model are not qualified; Proto.Actor has not been selected. | [#2899](https://github.com/elsa-workflows/elsa-foundation/issues/2899), [#2905](https://github.com/elsa-workflows/elsa-foundation/issues/2905) |
| ETL | Active leaf [#3109](https://github.com/elsa-workflows/elsa-foundation/issues/3109), [spec 255](../../specs/255-finite-file-etl/spec.md): finite CSV-to-JSONL with bounded partitions, pinned lookup, native invocation, recovery and Studio. The [runtime checkpoint](../../specs/255-finite-file-etl/evidence/finite-file-foundations.md) proves bounded host execution, and the [native composite checkpoint](../../specs/255-finite-file-etl/evidence/native-composite-checkpoint.md) proves typed result consumption and continuation in Sequence, Flowchart and CompensationScope. ETL terminal-outcome/cancellation, final materialization, API/Studio and two-process recovery acceptance remain owed. Continuous processing, other approved connectors and BPMN invocation remain on the parent. | [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908) |
| Agents | Production native collaboration, resource bindings, durable decisions, exact approvals, budgets and recovery, with Studio. | [#2912](https://github.com/elsa-workflows/elsa-foundation/issues/2912) |
| Integrated acceptance | Historical research HTML [#2911](https://github.com/elsa-workflows/elsa-foundation/issues/2911) is delivered in [PR #3055](https://github.com/elsa-workflows/elsa-foundation/pull/3055), with browser and exact-merge checks. Production journeys, the updated presentation and parent acceptance reconciliation remain required. | [#2906](https://github.com/elsa-workflows/elsa-foundation/issues/2906), [#2910](https://github.com/elsa-workflows/elsa-foundation/issues/2910) |

## Approved scope and boundaries

- Keep the existing EF checkpoint/outbox authority; prove native and BPMN integration before stabilizing neutral public contracts. Actors address whole executions, not every activity or record.
- BPMN foundation APIs: task claim/completion, external work, correlation, timers, incidents/retry, cancellation and inspection. Arbitrary token alteration, live-instance migration and a full operator console are deferred, not demonstrated.
- ETL connectors: CSV/JSON Lines files; paginated JSON HTTP ingestion and JSON delivery; PostgreSQL and SQL Server snapshot/watermark reads and transactional keyed writes; RabbitMQ JSON with manual acknowledgement and publisher confirms. Include local mapping, filtering, validation, routing and lookups. Log CDC, distributed shuffle, windows/stream joins and general analytics are deferred.
- ETL uses partition cursors, bounded batches, byte limits and durable admission budgets. Lease expiry does not prove physical memory reclamation or worker shutdown. External effects remain at least once unless destination guarantees are verified.
- Agent delivery includes typed routes/rounds, scoped memory/knowledge/tool bindings, durable accepted decisions, exact authorized tool approval, budgets before dispatch and deterministic/provider-protocol tests. Live model smoke tests are opt-in; no unexecuted live behavior claim.
- Preserve existing definitions and saved executions with explicit versioning. Rollback stops new capability admission while retaining readers/workers for accepted instances.
- Complete the existing presentation's offline/direct-file, keyboard, reduced-motion and responsive checks through an authorized browser harness.

## Working method

One active implementation leaf, one integration lane and a small prepared successor queue. Reuse matching PRs and honor claims. Use a concise contract for substantial behavior, executable acceptance tests, focused changes, bounded self-review under the owner’s standing authorization, CI/maps and post-merge evidence. Use bounded worker reviews where useful; they do not create an independent-approval gate. Do not create a parallel backlog in this file or duplicate existing native proofs. Progressive elaboration follows real integrations.

BPMN library changes, refactoring, upgrades and necessary releases are explicitly authorized. Use the relevant GitHub App identity and normal review/publication gates. This authority does not extend to unrelated releases/deployments, permission changes, live model costs or overriding active claims.

## Evidence limits and completion

[#2915's final receipt](https://github.com/elsa-workflows/elsa-foundation/pull/2915#issuecomment-6247136655) records accepted descendant CI. Original merge-commit CI/CodeQL were cancelled, not passed. The historical receipt timeout remains causally unexplained. Research witnesses are not production qualification.

Close the program only when the supported journeys work on main, their acceptance evidence is reproducible, migration/rollback constraints are documented, and each parent criterion has a demonstrated result or an explicit owner disposition. A plan, package release, or one merged slice is not program completion.
