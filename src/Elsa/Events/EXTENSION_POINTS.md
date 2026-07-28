# Extension points — Events domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Events` — the composition root where `EventsFeature` wires the event publisher, channel, pipeline, and background worker. This is the **substrate** that all other domain catalogs rely on: `IEventHandler<T>` is the universal fan-in mechanism underpinning every single aggregating handler in the repo.

---

## Implementable contributor interfaces

### `IEventHandler<T>` *(Core — `Elsa.Events.Core`)* where `T : IEvent`
- **Kind:** Contributor (event subscriber — handles a specific event type).
- **Signature:** `Task Handle(T @event, CancellationToken cancellationToken);`
- **Register:** `services.AddEventHandler<TEvent, MyHandler>()` (additive), `services.TryAddEventHandler<TEvent, MyHandler>()` (idempotent — for aggregators several features may each register), or `AddEventHandlersFrom(assembly)` assembly scan. All three record the handler under both the closed generic `IEventHandler<TEvent>` (the dispatch path) and the non-generic `IEventHandler` marker. **Do not register only under the bare `IEventHandler` marker** (`AddScoped<IEventHandler, MyHandler>()`): the pipeline resolves handlers exclusively through the closed generic, so a marker-only registration silently never dispatches.
- **Consumed by:** `EventPublisher` (this feature), which resolves the registered `IEventHandler<T>` implementations for the published event type (via the closed generic service type) and dispatches them according to the publishing strategy.

**The single-aggregating-handler convention** (framework §2.24.2): by convention, for every contributor-interface fan-in event (e.g. `OnDraftValidating`, `OnJsonPayloadConvertersInitializing`), exactly ONE `IEventHandler<OnXxx>` is registered — the aggregator that loops the typed contributor implementations. Feature code never registers its own `IEventHandler` for these events; it registers a typed contributor (e.g. `IDraftValidator`, `IJsonConverterSource`). This makes the contributor count visible at a glance.

**Known implementations (shipped):** aggregating handlers such as `ExecuteValidations`, `RegisterJsonConverters`, `PreProcessScript`, `PostProcessScript`, `BuildDeclarationsDocument`, `ApplyEntitySavingHandlers`, `ApplyEntityLoadingHandlers`, `CollectActivityVersions`, and `WorkflowVersionsReconcilingHandler`. See each domain's feature catalog for detail.

### `IEvent` *(Core — `Elsa.Events.Core`)*
- **Kind:** Marker interface — implement to define an event type. Typically a `sealed record`.
- No registration needed; passed to `IEventPublisher.Publish(...)`.

### `IEventMiddleware` *(Core — `Elsa.Events.Core`)*
- **Kind:** Contributor (event pipeline middleware). Composes the event dispatch pipeline.
- **Signature:** `ValueTask InvokeAsync(EventContext context, Func<EventContext, ValueTask> next);`
- **Register:** via `UseMiddleware<TMiddleware>()` on the event pipeline builder.

---

## Publishing strategies

Defined in `Elsa.Events.Strategies`. Each strategy determines how the published event is dispatched:

| Strategy | Behaviour | Use when |
|---|---|---|
| **Sequential** (default) | Publisher awaits the full dispatch chain synchronously. Handler exceptions propagate to the publisher. | The publisher needs to read back contributions (`OnDraftValidating`, `OnEntitySaving`). |
| **Background** | Event is enqueued on `IEventChannel`; `BackgroundEventPublisher` drains asynchronously. Publisher returns before handlers run. Handler exceptions are caught + logged. | Notification / observation (`OnDraftCreated`, `OnDraftValidated`). One subscriber failure must not break others. |

---

## Cross-references

- Pipeline middleware shape: [`Elsa.Pipelines.Core/EXTENSION_POINTS.md`](../Elsa.Pipelines.Core/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1 + §2.24.2.
