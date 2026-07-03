# Elsa.Activities.Runtime

Runtime-side composition root for **descriptor-type-driven activity construction**. Hosts the activity
factory and the descriptor-type → constructor registry, and drives the canonical §2.6.1 Registry +
StartUp Task + Domain Event pattern that populates the registry from every contributed
`IActivityConstructor`.

> Carries **no** `Elsa.*.Design.*` dependency (Elsa §E2.2). Construction is discriminated purely by the
> descriptor type's `FullName` — there is no `Kind` string and no per-kind branch anywhere in the
> factory, the registry, or the reconciler (SC-001 / SC-004).

## What this feature provides

- **`IActivityFactory`** → `ActivityFactory` — a pure dispatcher. Given `(descriptorType, payload, inputs,
  outputs)` it resolves the constructor registered for `descriptorType` and delegates. No type resolution
  or argument binding lives here — those are kind-specific and owned by each `IActivityConstructor`.
  An unknown descriptor type throws `UnknownDescriptorTypeException`.
- **`IActivityConstructorRegistry`** → `ActivityConstructorRegistry` — the descriptor-type → constructor
  dispatch table. Singleton; populated once at startup, read synchronously afterward. Enforces
  one-constructor-per-descriptor-type (a second, differently-typed constructor for the same descriptor
  type throws `DuplicateActivityConstructorException`; re-adding the same constructor type is idempotent).

## The Registry + StartUp Task + Domain Event wiring (framework §2.6.1)

Features contribute a kind simply by registering an `IActivityConstructor` in DI — no per-consumer
`IEnumerable<TProvider>` injection (G21). Three collaborators, all registered by `ActivitiesRuntimeFeature`:

- **`ActivityConstructorsStartupTask`** (`IStartupTask`) — publishes `OnActivityConstructorsInitializing`
  with an empty collection, then flushes the collected constructors into the registry. Fired once at startup.
- **`OnActivityConstructorsInitializing`** (`IEvent`, in `…Runtime.Core`) — carries the
  `ICollection<IActivityConstructor>` being assembled.
- **`RegisterActivityConstructors`** (`IEventHandler<OnActivityConstructorsInitializing>`) — the single
  aggregating handler. Resolves every `IActivityConstructor` from DI and adds it to the event's collection.

A second startup task, **`RegisterActivityTypesStartupTask`**, seeds the well-known-type registry with
activity CLR types (and their I/O element types) under the shared `TypeAliasConvention`, so a
`ClrActivityDescriptor`'s stable alias resolves back to a real `Type` without `Assembly.Load(name, version)`.

## Contribution contract

- **`IActivityConstructor` / `IActivityConstructor<TDescriptor>`** *(in `Elsa.Activities.Runtime.Core`)* —
  one constructor per descriptor type. `DescriptorType => typeof(TDescriptor).FullName!` is the registry
  key (never hand-authored); the non-generic bridge owns `payload.Deserialize<TDescriptor>()`. See
  [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) for the shipped implementations.

## Adding a new activity kind (seam walk)

The construction seam is closed to modification: a hypothetical new kind (say, a `Remote` kind backed by a
`RemoteActivityDescriptor`) touches **only three new types**, in the module that owns the kind — no edit to
the factory, the registry, the reconciler, or the design domain:

1. **A descriptor** — e.g. `RemoteActivityDescriptor(string Endpoint)` (a plain record; its `FullName`
   becomes the registry key and the persisted `DescriptorType`).
2. **A constructor** — `RemoteActivityConstructor : IActivityConstructor<RemoteActivityDescriptor>`,
   registered with `services.AddSingleton<IActivityConstructor, RemoteActivityConstructor>()`. The startup
   task aggregates it automatically.
3. **A reconciliation source** — an `IActivityReconciliationSource` that emits
   `ActivityVersionReconciliationModel`s with `DescriptorType = typeof(RemoteActivityDescriptor).FullName`
   and the descriptor as its opaque payload.

The design domain persists `(DescriptorType, DescriptorPayload)` opaquely and never resolves the type, so
it needs no change. This is the payoff of the descriptor-type-driven seam (US1 / US6).

## Failure modes

- **`UnknownDescriptorTypeException`** — the factory/registry has no constructor for the requested
  descriptor type (no owning module was composed).
- **`DuplicateActivityConstructorException`** — two different constructor types claimed the same descriptor
  type (a composition error surfaced at startup).

## Cross-references

- Extension-point catalog: [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md).
- The shipped kinds: `Elsa.Activities.Primitives` (CLR) and `Elsa.Activities.Composition.Runtime` (Workflow).
- Constitutional basis: §2.6.1 (Registry + StartUp Task + Domain Event); Elsa §E2.2 (no Runtime → Design).
