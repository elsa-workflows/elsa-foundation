# Elsa.Agent.Specialists

Executes published specialists through the existing native activity/background-work protocol and
Microsoft Agent Framework. See [runtime extension points](EXTENSION_POINTS.md) for configured
providers, protected continuity, journal ownership and the current qualification boundaries.

## Cross-domain contributions

`SpecialistsRuntimeFeature` registers these contributions per shell. Implementations stay internal;
their owning contracts remain in the respective Core packages.

| Owning contract | Contribution | Registration and purpose |
|---|---|---|
| Activities.Runtime.Core `IActivityActivationStrategy` | `SpecialistActivationStrategy` | Scoped enumerable contribution for the pinned specialist facade/model-turn consumer |
| Activities.Runtime.Core `IRuntimeActivityConsumerCapability` | `SpecialistConsumerCapability` | Singleton enumerable contribution advertising supported pinned descriptor schemas |
| Tasks.Core `IStartupTask` | `SpecialistTypesStartupTask`, `SpecialistJournalRegistrationStartupTask` | Registers native types and refuses missing/duplicate journal backends at activation |
| Workflows.Runtime.Core `IRuntimeCheckpointCommitEnricher` | `SpecialistCheckpointEnricher` | Scoped enumerable contribution in default phase0; contributes deterministic application responsibility, then validates the final checkpoint envelope; no specialist-store writes during enrichment |
| Workflows.Runtime.Core `IRuntimePostCommitDurableSourceIntentHandler` | `SpecialistSettlementHandler` | Scoped keyed contribution for `elsa.specialist.applied/1`, registered through `AddRuntimePostCommitIntentHandler` with a required physical source and retry-until-acknowledged policy; verifies native/receipt authority before idempotent journal metadata settlement |

The last two contributions use the [owning Workflows.Runtime catalog](../../../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md).
Application settlement preserves accepted history. Cancellation/tombstones, retention and complete
independent-process durable runtime/outbox qualification remain M3 obligations.

Checkpoint and settlement failures preserve their original cause in the specialist-domain exception,
with `contextId` and `workflowExecutionId` identifying the checkpoint or exact source. Native source
boundary and stale-claim exceptions retain their existing runtime delivery semantics. Requested
cancellation propagates unchanged.
