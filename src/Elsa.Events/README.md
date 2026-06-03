# Elsa.Events

In-process pub/sub implementation for the Elsa event pipeline. Provides `IEventPublisher`, `BackgroundEventPublisher`, and the default `Sequential` / `Background` delivery strategies over the shared `Elsa.Pipelines.Core` pipeline engine.

See [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) for the `IEventHandler<T>` contributor interface and `IEventMiddleware` seam.

## Cross-domain contributions

- **`IBackgroundTask`** *(Core — `Elsa.Tasks.Core`)* — `BackgroundEventPublisher` runs as a long-lived background task that drains the background-strategy event queue in isolation from the caller. Catalog: [`Elsa.Tasks/EXTENSION_POINTS.md`](../Elsa.Tasks/EXTENSION_POINTS.md)
