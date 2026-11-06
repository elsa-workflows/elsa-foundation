# Decisions extension points

| Contract | Kind | Registration | Lifetime | Default | Replacement behavior |
| --- | --- | --- | --- | --- | --- |
| `Elsa.Decisions.Core.IDecisionCompiler` | Single replacement | `DecisionsFeature.ConfigureServices` / application DI | Scoped | `DecisionEngine` | One explicit host registration survives TryAdd; duplicate existing replacements fail composition. Replacement owns safe compilation and immutable semantics identity. |
| `Elsa.Decisions.Core.IDecisionEvaluator` | Single replacement | `DecisionsFeature.ConfigureServices` / application DI | Scoped | `DecisionEngine` | One explicit host registration survives TryAdd; duplicate existing replacements fail composition. Replacement owns byte/pin validation, bounded pure evaluation and truthful trace. |

Feature name: `Decisions`. Standalone applications can construct the public `DecisionEngine` façade directly without a shell. No global evaluator registry, workflow switch or opaque mutable model state is introduced.

Profile/semantics IDs are compatibility contracts. A replacement cannot reuse an existing ID with changed meaning; preserve the original interpreter for retained artifacts or admit a new profile/semantics identity. The owning publication adapter must refuse unavailable semantics rather than silently select a replacement.
