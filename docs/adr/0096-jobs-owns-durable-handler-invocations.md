---
status: accepted
date: 2026-11-06
---

# Jobs owns durable handler invocations

Status: Accepted (6 November 2026), under Sipke Schoorstra's explicit scoped owner adoption of the Jobs
domain amendment in this delivery chat: “Adopt this scoped Jobs amendment on my authority”. This
records domain admission after the clean independent design review, not multi-architect consensus or
runtime qualification. [Spec 1218](../../specs/1218-standalone-jobs/spec.md) owns implementation acceptance.

## Problem and evidence

A standalone .NET application must submit a registered handler with durable inputs/results without
authoring a workflow. Current `Elsa.Tasks` runs host lifecycle tasks; its presence is not a durable job
product. Workflow scheduling owns per-execution FIFO, native identities and workflow-heavy contracts;
pretending jobs are workflow executions would obscure model semantics and block unrelated shared-queue work.

The [current-source and provider comparison](../../specs/1218-standalone-jobs/research.md) evaluates
Hangfire, Quartz.NET and TickerQ. None directly establishes the selected four-scope conjunctive durable
pause contract. An adapter would still need an Elsa admission/result authority; two independent
lifecycle/retry authorities would increase recovery risk. This is a design inference, not provider
runtime qualification.

## Decision

Admit optional `Elsa.Jobs`, with the purpose **“Executes registered handler invocations with durable
attempts and retained results.”** It owns invocation/handler/schema identity, durable acceptance,
schedules/occurrences, attempts, fencing, retry, cancellation/timeout, pause/start arbitration,
concurrency permits, immutable results and delivery responsibility.

The proposed first-party default is Elsa-owned, using existing persistence/hosting policy and narrowly
shared mechanics. No third-party scheduler is a required product dependency. Optional adapters can
produce idempotent submissions; an execution-provider replacement must demonstrate the complete
contract before claiming interchangeability. No universal scheduler, new cluster membership service,
kernel model-name switch or job-chain graph language is introduced.

Native workflows and BPMN retain model state and continuation. Ordinary optional adapters submit the
same job capability through existing checkpoint/outbox and exact consumer-proof patterns, following
[ADR 0088](0088-rules-owns-its-execution-domain.md). Model delivery does not own physical job retries.
The [proposed lifecycle contract](../../specs/1218-standalone-jobs/contracts/job-lifecycle.md) defines
the remaining boundaries; a read of pause flags followed by an unrelated write is not atomic arbitration.

## Confirmed owner selections

- First-class standalone usage and optional native/BPMN integrations in the initial release; Studio optional.
- Queue, job-type, workflow-instance and workflow-definition pauses gate every new attempt, including retry.
  Intake remains durable, held work consumes no capacity, and unrelated shared-queue work remains eligible.
- Running attempts finish; physical results remain durable. Instance/definition pauses also hold new
  result-driven continuation permissions until resume. This never requires rerunning completed work.
- Attempt start is the durable permission commit, ordered against pause. A prior grant may enter its
  handler afterward and is shown Starting/Running.
- Recurring work defaults to one catch-up invocation after pause/outage, then ordinary recurrence;
  explicit replay-every-occurrence drains boundedly. Already accepted jobs remain distinct and recoverable.

The engineering proposal applies an analogous disclosed permission cutoff to model-owned continuation.
That is an implementation recommendation to review, rather than a separately selected owner decision.
It implies neither cross-store physical suspension nor an atomic transaction across model stores.

## Package and shared-mechanism boundary

- `Elsa.Jobs.Core`: handler/client/result identities and contracts consumed by standalone hosts/adapters;
  no workflow, Studio, EF or third-party scheduler implementation dependency.
- `Elsa.Jobs`: client/handler registration and independent worker implementation, using ordinary .NET hosting.
- `Elsa.Jobs.Persistence.EntityFrameworkCore`: job-owned transitions, schema and store; reuse
  `Elsa.Persistence.EntityFramework` policy, bounded retries and exact-target shared transactions where applicable.
  Provider engines stay host dependencies. No parallel general persistence framework is proposed.
- Optional native/BPMN adapter packages preserve the runtime/design split. Operations API and Studio
  are optional, with normal tenant/permission boundaries and concrete consumers.
- Only demonstrated common mechanics may be extracted: existing generic fenced renewal has multiple
  real pump consumers; recurrence arithmetic can be shared with #3582 without moving activation,
  job occurrence state or model semantics into a universal scheduler. These extractions need their
  own reviewed ownership/dependency delta before implementation; they are not required wholesale rewrites.

## Guarantees and limits

The job store orders pause writes and each start permission with all applicable holds and deployment
permits. It retains input/result and schedule responsibility independently of dispatch eligibility.
Distributed concurrency bounds authorized live ownership; lease loss can leave stale physical work.
Timeout, cancellation and shutdown are cooperative. External effects may repeat and require handler
idempotency. Late physical receipts cannot supersede a fenced authoritative result.

Results and parent checkpoints remain separately owned. Durable outboxes, idempotent result identity
and model-owned continuation permission/consumption proof bridge them. A granted continuation is
already started for pause purposes; delivery retries must reconcile that grant rather than create an
unrelated new permission or physical invocation. Exact parent terminal proof can retire owed delivery.
No general cross-store transaction or exactly-once external-effect guarantee is claimed.

Initial proposed qualification profiles are local SQLite restart and PostgreSQL independent-process
multi-node operation. Other engines/deployments are unqualified until demonstrated. Final scope,
provider admissions and package closures must be reconciled with the current accepted constitution.

## Constitution amendment and authority

The owner adopted this additive amendment:

1. Insert `Elsa.Jobs` in the alphabetically ordered pinned root domains in Elsa constitution §E2.1.
2. Add its verb-led purpose above to `docs/reference/elsa-domain-tree.md`.
3. Add a Sync Impact Report and minor version bump from the actual merged Elsa constitution;
   after merging ADR0095's Decisions admission from main, this amendment applies 6.4.0 → 6.5.0.
   The earlier Decisions admission and its authority remain intact.
4. Keep framework 6.1.0 and every existing domain purpose unchanged. Recheck derived templates;
   current templates contain no pinned domain list. Ratify no draft/provisional sections.

Framework **Governance → Amendment process** normally requires amendments to be “ratified by consensus
of the architects”: Joey Barten, Sipke Schoorstra and Frans van Ek. For this specific additive admission,
the owner explicitly selected adoption on his authority after the concrete amendment and normal
consensus route were presented in the owning chat. This scoped exception covers Jobs admission and
its propagated minor version/purpose only; existing rules and provisional sections stay unchanged.
Bot review, publishing identity and the earlier general delivery instruction did not establish it.
The ownership/decision receipt remains on [#3586](https://github.com/elsa-workflows/elsa-foundation/issues/3586#issuecomment-6672110215)
and [PR #3909](https://github.com/elsa-workflows/elsa-foundation/pull/3909).

The new domain has no #3052 initiative ancestry/topic. If a model adapter needs to amend that
initiative's background eligibility, variable lifecycle or recorded-step recovery decisions, route
only that amendment through its final-human-agreement gate. Consuming accepted contracts does not
pull the entire job product into the initiative.

## Acceptance and consequences

This proposal adds a product authority because the job lifecycle is a separate domain responsibility;
it avoids silently repurposing host tasks or requiring synthetic workflows. The cost is new durable
state, package/public contracts and operational surface that must earn their existence through the
[acceptance matrix](../../specs/1218-standalone-jobs/acceptance-matrix.md).

Production implementation follows domain agreement and reviewed spec/plan/tasks. Completion requires
packaged standalone usage, genuine native/BPMN integration, races/restart/handover, inspected composed
API/Studio permissions, current-head review/checks/merge and resulting-main qualification. Neither
this ADR nor a first milestone establishes product completion.
