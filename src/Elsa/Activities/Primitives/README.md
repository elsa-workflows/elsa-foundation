# Elsa.Activities.Primitives

The **CLR activity kind**: primitive hand-written activities plus the constructor that builds any CLR
`IActivity` from its descriptor. A runtime feature — references **no** `Elsa.*.Design.*` project
(Elsa §E2.2) and **no** other feature project (G4 / SC-006).

## What this feature provides

`ActivitiesPrimitivesFeature.ConfigureServices` registers:

- **`ClrActivityConstructor`** → contributed as `IActivityConstructor` — owns descriptor type
  `Elsa.Primitives.Models.ClrActivityDescriptor`. It deserializes the descriptor, resolves the activity's
  stable alias (`ClrActivityDescriptor.TypeAlias`) to a live `Type` via the well-known-type registry,
  activates it with `ActivatorUtilities`, and binds the author-supplied arguments with the
  `ActivityArgumentBinder`. The runtime feature's `ActivityConstructorsStartupTask` aggregates it into the
  registry — this feature registers nothing else to wire it in.
- **`ActivityArgumentBinder`** (feature-internal, singleton) — matches author `InputArgument`/`OutputArgument`
  values to activity properties by name (case-insensitive) and assignable type, rewrapping widened argument
  generics where needed (e.g. `InputArgument<int>` → a property typed `InputArgument<object>`). Missing
  property, incompatible type, and no-public-setter each throw.

## Shipped activities

- **`WriteLine`** — a leaf `IActivity` with an `InputArgument<string> Text`; writes to the console. The
  concrete activity the CLR construction round-trip binds against.

## Registration & tests

`ClrActivityConstructor` and `ActivityArgumentBinder` are registered against their contracts so the CLR
round-trip is provable end-to-end through `IActivityFactory`. Coverage lives in
`tests/Elsa/Activities/Runtime/Tests` (`ClrActivityConstructorTests`, `ActivityArgumentBinderTests`,
`ActivitiesPrimitivesFeatureTests`) — the Primitives tests colocate there rather than in a separate project.

## Cross-references

- The construction seam this plugs into: [`../Runtime/README.md`](../Runtime/README.md).
- The sibling Workflow kind: [`../Composition/Runtime/README.md`](../Composition/Runtime/README.md).
- Constitutional basis: §2.6.1 (contribution contract); Elsa §E2.2; G4 (no feature → feature).
