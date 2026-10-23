# Elsa.Events

In-process pub/sub implementation for the Elsa event pipeline, over the shared `Elsa.Pipelines.Core` pipeline engine. Publishers depend on `IInlineEventPublisher`, which awaits every handler, or `IDeferredEventPublisher`, which queues the event and returns before handlers run; both contracts live in `Elsa.Events.Core`. The publisher, delivery strategies and background worker behind them are internal to the Events domain.

See [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) for the `IEventHandler<T>` contributor interface, the `IEventMiddleware` seam, and the inline and deferred delivery contracts.

## Cross-domain contributions

- **`IBackgroundTask`** *(Core, `Elsa.Tasks.Core`)*: the internal `BackgroundEventPublisher` runs as a long-lived background task that drains the events published through `IDeferredEventPublisher`, in isolation from the caller. Catalog: [`Elsa.Tasks/EXTENSION_POINTS.md`](../../Tasks/Elsa.Tasks/EXTENSION_POINTS.md)
