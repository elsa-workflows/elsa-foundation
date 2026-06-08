# Extension points — Activities.Runtime domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Runtime` — the composition root where `ActivitiesRuntimeFeature` registers the activity construction factory, the descriptor-type → constructor registry, the single aggregating `RegisterActivityConstructors` handler, and the startup task that drives the Registry + StartUp Task pattern.

> Carries **no** `Elsa.*.Design.*` dependency (Elsa §E2.2). Construction is discriminated by the descriptor type's `FullName`, not a `Kind` string.

---

## Implementable contributor interfaces

### `IActivityConstructor<TDescriptor>` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Contribution (one constructor per descriptor type).
- **Signature:** `string DescriptorType { get; }`; `ValueTask<IActivity> Construct(TDescriptor descriptor, IDictionary<string, InputArgument>?, IDictionary<string, OutputArgument>?, CancellationToken)` (with the non-generic `IActivityConstructor` bridge that owns `payload.Deserialize<TDescriptor>()`).
- **Register:** `services.AddSingleton<IActivityConstructor, MyConstructor>()`.
- **Aggregated by:** the single `RegisterActivityConstructors : IEventHandler<OnActivityConstructorsInitializing>` (this feature), which collects every registered constructor and adds it to the registry. The registry enforces one-constructor-per-`DescriptorType` (throws on a duplicate).

**Known implementations (shipped):**
- `Elsa.Activities.Primitives` — `ClrActivityConstructor` *(descriptor type `Elsa.Primitives.Models.TypeInformation`; the default/primitive CLR kind)*
- `Elsa.Activities.Composition.Runtime` — `WorkflowActivityConstructor` *(descriptor type `Elsa.Workflows.Primitives.Models.WorkflowIdentity`; the Workflow kind)*

---

## Events

`CatalogParityTests` scans the `Elsa.Activities.Runtime.Core` assembly, paired with this catalog file, for `IEvent` types.

### OnActivityConstructorsInitializing
`(ICollection<IActivityConstructor> Constructors)`

**Semantic.** The activity constructor registry is initialising. Every registered `IActivityConstructor` is contributed to the `Constructors` collection, then flushed into the registry.

**Delivery strategy.** Sequential — all constructors must be registered before the first activity construction.

**Publication site.** `ActivityConstructorsStartupTask` (`Elsa.Activities.Runtime`) — fired once at startup.

**Expected handler.** Exactly one: `RegisterActivityConstructors` (this feature).

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1; Elsa §E2.2 (no Runtime → Design dependency).
