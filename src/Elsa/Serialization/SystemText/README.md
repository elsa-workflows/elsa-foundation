# Elsa.Serialization

Provides JSON serialisation infrastructure: registers the `IJsonSerializerOptionsConfigurator` pipeline and the `RegisterJsonConverters` aggregating handler that collects converters from all registered `IJsonConverterSource` implementations.

## Cross-domain contributions

- **`IStartupTask`** *(Core — `Elsa.Tasks.Core`)* — `JsonPayloadConvertersInitializingStartupTask` runs at startup to initialise the JSON converter registry by publishing `JsonPayloadConvertersInitializing`. Catalog: [`Elsa.Tasks/EXTENSION_POINTS.md`](../Elsa.Tasks/EXTENSION_POINTS.md)
