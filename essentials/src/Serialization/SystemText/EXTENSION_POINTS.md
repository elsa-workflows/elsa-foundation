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
- **Kind:** Contributor for CLR conversions outside the JSON conversion contract.
- **Signature:** `Type TargetType { get; set; }` and `object? Convert(object? value);`
- **Register:** `services.AddSingleton<ITypeConverter, MyConverter>()`. The object converter is a singleton and resolves every registered `ITypeConverter` from the root provider on each conversion, so register it as a singleton or a transient, not scoped.
- **Consumed by:** `ObjectConverter` (this feature). JSON elements/nodes, strings and temporal values use the selected conversion contract first. Already assignable CLR values return directly; implicit floating-point to integral conversions are refused. For other CLR values, the first registered `ITypeConverter` whose `TargetType` equals the requested target type exactly runs before the fallback serialization/read (a `Nullable<T>` target does not match a `T` converter). `ObjectConverterOptions` do not apply to the extension converter.

**Known implementations (shipped):** none.

### `IJsonConversionMemberMatcher` *(Core, `Elsa.Serialization.Core`)*

- **Kind:** Optional capability on a custom `JsonConverter` used by a typed conversion contract.
- **Signature:** `bool TryGetMemberType(string name, out Type memberType);`
- **Select:** implement it on the converter selected by the target type's or member's JSON metadata, for example through `JsonConverterAttribute`.
- **Consumed by:** `UnmatchedMemberScanner` before deserialization. Return true and the effective value type for each object key the converter consumes; return false for an unmatched key. The selected scoped contract controls warning or refusal, and matching keys are inspected recursively with their declared value type.
- **Without it:** custom converters still perform conversion, with `json-unmatched-coverage-unavailable` instead of invented unmatched-member warnings.

**Known implementations (shipped):** none in production.

---

## Events

None public. `JsonPayloadConvertersInitializing` is internal to this domain: `JsonPayloadConvertersInitializingStartupTask` publishes it once at startup, before the application handles its first request, and its single handler, `RegisterJsonConverters` (this feature), fills its `Converters` collection from every `IJsonConverterSource`. Contribute converters through `IJsonConverterSource`, not by handling the event.

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
