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

## Cross-domain contributions

`SpecialistsRuntimeFeature` registers these contributions per shell. Implementations stay internal;
their owning contracts remain in the respective Core packages.

| Owning contract | Contribution | Registration and purpose |
|---|---|---|
| Activities.Runtime.Core `IActivityActivationStrategy` | `SpecialistActivationStrategy` | Scoped enumerable contribution for the pinned specialist facade/model-turn consumer |
| Activities.Runtime.Core `IRuntimeActivityConsumerCapability` | `SpecialistConsumerCapability` | Singleton enumerable contribution advertising supported pinned descriptor schemas |
| Tasks.Core `IStartupTask` | `SpecialistTypesStartupTask`, `SpecialistJournalRegistrationStartupTask` | Registers native types and refuses missing/duplicate journal backends at activation |
| Workflows.Runtime.Core `IRuntimeCheckpointCommitEnricher` | `SpecialistCheckpointEnricher` | Scoped enumerable contribution in default phase0; contributes deterministic application/cancellation responsibility, then validates the final checkpoint envelope; no specialist-store writes during enrichment |
| Workflows.Runtime.Core `IRuntimePostCommitDurableSourceIntentHandler` | `SpecialistSettlementHandler` | Scoped keyed contribution for `elsa.specialist.applied/1` and `elsa.specialist.cancel/1`, registered through `AddRuntimePostCommitIntentHandler` with a required physical source and retry-until-acknowledged policy; verifies native/receipt authority before idempotent journal metadata settlement |

The last two contributions use the [owning Workflows.Runtime catalog](../../../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md).
Application and cancellation settlement preserve accepted history and distinguish process application
from journal acceptance. Retention and complete independent-process durable runtime/outbox qualification
remain M3 obligations.

Checkpoint and settlement failures preserve their original cause in the specialist-domain exception,
with `contextId` and `workflowExecutionId` identifying the checkpoint or exact source. Native source
boundary and stale-claim exceptions retain their existing runtime delivery semantics. Requested
cancellation propagates unchanged.
