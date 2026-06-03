# Extension points — Serialization domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Serialization` — the composition root where `SerializationFeature` wires `RegisterJsonConverters` and the built-in converter source.

---

## Implementable contributor interfaces

### `IJsonConverterSource` *(Core — `Elsa.Serialization.Core`)*
- **Kind:** Source (returns values — pull pattern).
- **Signature:** `IEnumerable<JsonConverter> GetConverters();`
- **Register:** `services.AddScoped<IJsonConverterSource, MySource>()`.
- **Aggregated by:** the single `RegisterJsonConverters : IEventHandler<OnJsonPayloadConvertersInitializing>` (this feature), which injects `IEnumerable<IJsonConverterSource>`, calls `GetConverters()` on each, and adds every converter to the event's `Converters` collection.
- **Adding one does not replace the others** — all registered sources contribute their converters.

**Known implementations (shipped):**
- `Elsa.Serialization` — `BuiltInJsonConverterSource` *(intra-domain — default)*
- `Elsa.Expressions` — `ExpressionsJsonConverterSource` *(cross-domain — registers expression-related converters)*

---

## Events

### OnJsonPayloadConvertersInitializing
`(ICollection<JsonConverter> Converters)`

**Semantic.** The JSON serialisation pipeline is initialising and collecting converters. Contributor-shaped: the `Converters` collection is the directly-accessible write sink that `RegisterJsonConverters` fills from all `IJsonConverterSource` implementations.

**Delivery strategy.** Sequential — all converters must be registered before serialisation begins.

**Publication site.** `JsonPayloadConvertersInitializingStartupTask` (`Elsa.Serialization`) — a startup task that fires once before the application handles its first request.

**Expected handler.** Exactly one `IEventHandler<OnJsonPayloadConvertersInitializing>`: `RegisterJsonConverters` (this feature).

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
