# Elsa.Agent.Specialists

Executes published specialists through the existing native activity/background-work protocol and
Microsoft Agent Framework. See [runtime extension points](EXTENSION_POINTS.md) for configured
providers, protected continuity, journal ownership and the current qualification boundaries.

## Admission during maintenance

Set `SpecialistsRuntimeFeature.AllowNewOperations = false` when activating a host to refuse new
logical operations with `specialist.turn.admission-disabled`. Both journal adapters decide this
inside the operation's creation transaction or lock. A refusal creates no turn or operation gate
and opens no model client. The default is `true`.

Matching retained operations can still register a native delivery and replay an accepted result.
Already admitted work, readers and application/cancellation settlement remain available; disabling
creation neither cancels work nor resets unknown outcomes, deadlines or conversation history.
The policy belongs to host configuration and does not alter a persisted request's fingerprint.

## Protected content retention

`SpecialistsRuntimeFeature.ContentRetention` defaults to thirty days after native workflow completion.
It accepts a positive interval up to ten years. The existing recurring-task scheduler visits each
host-supplied ordinary persistence partition, scanning one finite metadata page per tenant key every
five minutes. Retention stays active when new admission is disabled.

Active workflows, missing native ownership/artifacts, unfinished deliveries, and pending or
unacknowledged application sources pin content. Claimed or unknown conversation work also pins its
previous history. The relational purge competes with conversation ownership and turn updates in an
owned transaction. A changed proof is refused; it is reconsidered on a later sweep.

A successful purge removes the protected request/result/session pair while preserving status,
operation fingerprint, reservations, usage, delivery/application records, cancellation gates and
conversation references. `specialist.journal.content-unavailable` identifies a retained operation
whose content expired; reusing its operation key cannot create another model request. Native workflow
outputs have their own retention policy. This operation does not erase those copies.

## Cross-domain contributions

`SpecialistsRuntimeFeature` registers these contributions per shell. Implementations stay internal;
their owning contracts remain in the respective Core packages.

| Owning contract | Contribution | Registration and purpose |
|---|---|---|
| Activities.Runtime.Core `IActivityActivationStrategy` | `SpecialistActivationStrategy` | Scoped enumerable contribution for the pinned specialist facade/model-turn consumer |
| Activities.Runtime.Core `IRuntimeActivityConsumerCapability` | `SpecialistConsumerCapability` | Singleton enumerable contribution advertising supported pinned descriptor schemas |
| Tasks.Core `IRecurringTask` | `SpecialistRetentionPumpTask` | Singleton bounded sweep through the existing persistence-scope runner and failure backoff; scoped native/source proof and conditional content purge |
| Tasks.Core `IStartupTask` | `SpecialistTypesStartupTask`, `SpecialistJournalRegistrationStartupTask` | Registers native types and refuses missing/duplicate journal backends at activation |
| Workflows.Runtime.Core `IRuntimeCheckpointCommitEnricher` | `SpecialistCheckpointEnricher` | Scoped enumerable contribution in default phase0; contributes deterministic application/cancellation responsibility, then validates the final checkpoint envelope; no specialist-store writes during enrichment |
| Workflows.Runtime.Core `IRuntimePostCommitDurableSourceIntentHandler` | `SpecialistSettlementHandler` | Scoped keyed contribution for `elsa.specialist.applied/1` and `elsa.specialist.cancel/1`, registered through `AddRuntimePostCommitIntentHandler` with a required physical source and retry-until-acknowledged policy; verifies native/receipt authority before idempotent journal metadata settlement |

The last two contributions use the [owning Workflows.Runtime catalog](../../../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md).
Application and cancellation settlement preserve accepted history and distinguish process application
from journal acceptance. Retained-reader/key/codec and complete independent-process durable
runtime/outbox qualification remain M3 obligations.

Checkpoint and settlement failures preserve their original cause in the specialist-domain exception,
with `contextId` and `workflowExecutionId` identifying the checkpoint or exact source. Native source
boundary and stale-claim exceptions retain their existing runtime delivery semantics. Requested
cancellation propagates unchanged.


## Retained protection and session readers

Configure durable Secrets keys for durable journals. A new active key writes new content; keep the
older key for every retained request, accepted receipt and session that still needs a reader. The
runtime supports current v2 key-labelled protection and retained legacy v1 protection through Secrets.
Missing keys fail explicitly and do not create fresh conversation history. Default retention age
alone is insufficient for key retirement because active or unresolved work can keep content pinned.

Source compatibility tests reopen the file-backed EF journal with separate providers/DbContexts,
rotated or retired keys, new admission disabled and no model contribution. They verify immutable
accepted replay, session restoration, new-key writes and explicit missing-key failure. Authenticated
unsupported session purpose/codec fixtures fail before model construction after an admitted
continuation is reopened; known failure leaves the preceding accepted revision intact.

These are library-reader tests within one test process, including seeded retained/future format
writers. They establish neither an old module binary upgrade/rollback nor independent-process native
workflow/outbox recovery. Those deployment qualifications remain required by the support contract.
