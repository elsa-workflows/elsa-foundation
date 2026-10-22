# Rules Core extension points

| Contract | Responsibility | Default registration |
|---|---|---|
| `IRuleArtifactCompiler` | Validate and canonically compile a reusable finite program; return its immutable artifact and validated vocabulary count. | Scoped `RuleArtifactCompiler` in `RulesRuntimeFeature`. |
| `IRuleInvocationFactory` | Freeze one direct invocation's facts, positive budget and trusted implementation pin without writing a run. | Scoped `RuleInvocationFactory` in `RulesRuntimeFeature`. |
| `IRuleImplementationIdentitySource` | Report the actual host-owned implementation identity, or unavailability; never infer package identity from source builds. | Provider-generation snapshot `RuleDeploymentIdentitySource` in `RulesRuntimeFeature`. |

Defaults use `TryAdd`; an earlier host registration is retained and a later explicit registration can replace it. A replacement compiler owns the correctness of its artifact and vocabulary metadata. A replacement identity source owns the trust and deployment-evidence contract. Neither override changes persisted invocation identity: retries must reuse the original envelope.

The native publication contributor consumes the compiler and identity source once at publication. Its runtime adapter consumes the frozen publication, not either selection service. Store/reader, admission/round validation and native delivery proof contracts are separately owned by `Elsa.Rules.Persistence.Core`; see [spec 999](../../../../specs/999-durable-rules/spec.md) for the current acceptance boundaries.
