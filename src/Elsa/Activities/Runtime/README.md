# Elsa.Activities.Runtime

Runtime-side composition root for **stable consumer-key-driven activity construction**. Hosts the activity
factory and the `(consumer key, schema version)` → constructor registry, and drives the canonical §2.6.1 Registry +
StartUp Task + Domain Event pattern that populates the registry from every contributed
`IActivityConstructor`.

> Carries **no** `Elsa.*.Design.*` dependency (Elsa §E2.2). Construction is discriminated purely by the
> executable artifact's provider-neutral `(ConsumerKey, SchemaVersion)` pair. CLR type names may occur
> inside a consumer-owned payload, but are never universal dispatch identity.

## What this feature provides

- **`IActivityFactory`** → `ActivityFactory` — a pure dispatcher. Given a `RuntimeActivityDescriptor`
  plus inputs and outputs, it resolves the constructor registered for the descriptor's stable consumer
  key and schema version and delegates. Payload interpretation remains consumer-owned.
- **`IActivityConstructorRegistry`** → `ActivityConstructorRegistry` — the `(consumer key, schema version)` → constructor
  dispatch table. Singleton; populated once at startup, read synchronously afterward. Enforces
  one-constructor-per-key/schema claim; duplicate claims throw `DuplicateActivityConstructorException`.

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
  one constructor per stable consumer key and supported schema version. The generic bridge owns
  `payload.Deserialize<TDescriptor>()`; the payload type itself is not the registry key. See
  [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) for the shipped implementations.

## Adding a new activity kind (seam walk)

The construction seam is closed to modification: a hypothetical remote activity provider touches only
provider-owned types in the module that owns it — no edit to
the factory, the registry, the reconciler, or the design domain:

1. **A provider-neutral identity and descriptor** — for example provider/consumer key
   `acme.remote-activity`, schema `1`, and `RemoteActivityDescriptor(string Endpoint)` as opaque payload.
2. **A constructor** — `RemoteActivityConstructor : IActivityConstructor<RemoteActivityDescriptor>`,
   registered with `services.AddSingleton<IActivityConstructor, RemoteActivityConstructor>()`. The startup
   task aggregates it automatically.
3. **A reconciliation source** — an `IActivityReconciliationSource` that emits
   `ActivityVersionReconciliationModel`s with explicit provider and consumer key/schema pairs plus the
   descriptor as opaque payload.

The design domain persists the stable identities and descriptor payload opaquely and never resolves the
consumer's CLR payload type.

## Failure modes

- **`ActivityResolutionException`** — the factory/registry has no constructor for the requested consumer
  key/schema, or the schema is unsupported.
- **`DuplicateActivityConstructorException`** — two constructors claimed the same consumer key/schema.

## Cross-references

- Extension-point catalog: [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md).
- The shipped consumers: `Elsa.Activities.Primitives` (CLR) and `Elsa.Activities.Graph.Runtime` (inline graph composite).
- Constitutional basis: §2.6.1 (Registry + StartUp Task + Domain Event); Elsa §E2.2 (no Runtime → Design).
