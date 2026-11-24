# Extension points — Serialization domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Serialization` — the composition root where `SerializationFeature` wires `RegisterJsonConverters` and the built-in converter source.

---

## Implementable contributor interfaces

### `IJsonConverterSource` *(Core — `Elsa.Serialization.Core`)*
- **Kind:** Source (returns values — pull pattern).
- **Signature:** `IEnumerable<JsonConverter> GetConverters();`
- **Register:** `services.AddScoped<IJsonConverterSource, MySource>()`.
- **Aggregated by:** the single `RegisterJsonConverters : IEventHandler<JsonPayloadConvertersInitializing>` (this feature), which injects `IEnumerable<IJsonConverterSource>`, calls `GetConverters()` on each, and adds every converter to the event's `Converters` collection.
- **Adding one does not replace the others** — all registered sources contribute their converters.

**Known implementations (shipped):**
- `Elsa.Serialization` — `BuiltInJsonConverterSource` *(intra-domain — default)*
- `Elsa.Expressions` — `ExpressionsJsonConverterSource` *(cross-domain — registers expression-related converters)*

### `IRuntimeTypeNullability` *(Core, `Elsa.Serialization.Core`)*

- **Kind:** Replacement: one implementation is active per host. Register a singleton with `services.Replace(...)`;
  competing registrations are refused by the startup composition check.
- **Contract:** `TryGetSupportsNull(alias, out supportsNull)` finds the activated scalar alias's null contract.
  Returning false means unknown, not non-nullable. Collection nullability remains the caller's type-shape rule.
- **Default:** internal `RuntimeTypeNullability`, registered by `SerializationFeature`, snapshots the shared
  `IVariableTypeDescriptorProvider` contributions without resolving the design picker catalog. Alias matching follows
  the case-insensitive `IWellKnownTypeRegistry`, including convention aliases through its canonical lookup.
- **Consumers:** workflow variable reads, assignments, captures, graph execution and operator alterations. Publishing
  and picker APIs independently dispatch `IVariableTypeDescriptorCatalog` over the shared descriptor shape.
- **Replace when:** a host supplies its own activated runtime type metadata authority. It must preserve the contracts
  of the same aliases used during publication; a CLR reference-type fallback does not override a known null contract.

### `ITypeConverter` *(Core, `Elsa.Serialization.Core`)*
- **Kind:** Contributor (a custom conversion `IObjectConverter` tries first).
- **Signature:** `Type TargetType { get; set; }` and `object? Convert(object? value);`
- **Register:** `services.AddSingleton<ITypeConverter, MyConverter>()`. The object converter is a singleton and resolves every registered `ITypeConverter` from the root provider on each conversion, so register it as a singleton or a transient, not scoped.
- **Consumed by:** `ObjectConverter` (this feature). Before any built-in strategy, it takes the first registered `ITypeConverter` whose `TargetType` equals the requested target type exactly (a `Nullable<T>` target does not match a `T` converter) and returns what its `Convert` returns. `ObjectConverterOptions` do not apply to it.

**Known implementations (shipped):** none.

---

## Events

None public. `JsonPayloadConvertersInitializing` is internal to this domain: `JsonPayloadConvertersInitializingStartupTask` publishes it once at startup, before the application handles its first request, and its single handler, `RegisterJsonConverters` (this feature), fills its `Converters` collection from every `IJsonConverterSource`. Contribute converters through `IJsonConverterSource`, not by handling the event.

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
