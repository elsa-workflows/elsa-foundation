# Jobs native integration extension points

| Contract | Layer/kind | Default and consumers | Signature and override obligation |
|---|---|---|---|
| `IJobWorkflowAssociationSource` | Optional model-owned source contribution | No default; direct native owns itself. Optional Jobs.Bpmn contributes its actual root owner. | `Supports(origin)`, `ResolveAsync(origin)`, `InspectAsync(owner,evidence?)`. Exactly one source must support a model origin; zero is unavailable and duplicates are invalid. Inspection never grants or acknowledges. Null evidence allows only root terminality, not invented consumption. |
| `IJobNativeDeliveryInspector` | Native read contract; one selected implementation | `JobsNativeDeliveryInspector` in this package; consumed by optional model bridges | `InspectWorkerAsync(worker,ct)` returns checked consumed-result evidence or null for an unowned/unconsumed worker. Owned malformed evidence fails closed. Reads use a fresh partition-bound scope. An alternative must preserve full pin, result/grant, retired-receipt and final-attempt proof. |

| `IJobNativeResultSettlement` | Native settlement contract; one selected implementation | `JobsNativeResultSettlement`; optional model bridges call it from their existing result source | `SettleWorkerAsync(worker,ct)` returns only after canonical Jobs ACK or positive no-admission proof. Unknown/retired origins, missing history, and rejected live model inputs remain owed. Reads use a fresh partition-bound scope; no model document is parsed here. |

## Runtime contributions

| Contract/event | Contribution and cadence |
|---|---|
| `IRuntimeCheckpointCommitEnricher` | `JobsCheckpointEnricher`, scoped, freezes actual wait/admission sources and live claim delivery bindings; validates its final enriched admission envelope. |
| Runtime postcommit intents | `JobsNativeIntentHandler` handles `Elsa.Jobs.Start.v1` and `Elsa.Jobs.Cancel.v1`, UntilAcknowledged from a one-second base delay. All three kinds explicitly require durable sources, without a stimulus recipient-cohort protocol. The handler rechecks physical source, committed scope, content fingerprint and live claim. Bare intent delivery is refused. Stable origin/request and native source identity survive retry; cancellation waits only for Jobs cancellation/tombstone confirmation. `JobsNativeSettlementIntentHandler` handles direct-only `Elsa.Jobs.Settlement.v1` until canonical result ACK; model workers use their existing model-result source. |
| `ActivityResumeValidating` | `JobsNativeResumeValidator`, scoped Sequential read-only handler, checks exact existing permission before claim/activation; retained admission/access failure defers the queued resume, while deliberate incoming conflicts refuse it. Skips other activity families. |
| `IStartupTask` / `IHostedService` | One shared configuration validator checks exact-one command/query/evidence/settlement authorities before background work. |
| `IBackgroundTask` / `IHostedService` | Optional `JobsResultDeliveryWorker`, one shared lifecycle. Bounded page, round-robin trusted scopes, fresh per-item scope/timeout and unconditional configured pacing; an item timeout leaves its responsibility owed while later same-page items progress; no physical handler slots or new scheduler authority. |

`JobWorkflowHandlerPin` is the public publication-to-runtime descriptor; `RunJobState`,
`RunJobCompletion` and `RunJobResult` are typed saved-boundary contracts. The node metadata key and
activity type are public constants on `RunJob`. These are not a mutable handler catalog. The adapter
uses the Jobs Core replacement contracts for durable acceptance, grants and acknowledgement.

See [runtime behavior and current qualification limits](README.md), [Jobs Core contracts](../../Core/EXTENSION_POINTS.md),
the [Jobs runtime catalog](../../Elsa.Jobs/EXTENSION_POINTS.md), and the
[feature specification](../../../../../specs/1218-standalone-jobs/spec.md).
