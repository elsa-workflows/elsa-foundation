# Contract: `IActivityDefinitionHasher`

**Location.** `Elsa.Activities.Design.Reconciliation.Core.IActivityDefinitionHasher`

**Kind.** Replacement contract. One implementation per host; a default ships in `Elsa.Activities.Design.Reconciliation`; providers may replace with their own.

**Constitutional citation.** Framework §2.6.2 (replacement contracts); Sipke item 6 (reconciliation provenance includes a hash).

## Surface

```csharp
namespace Elsa.Activities.Design.Reconciliation.Core;

public interface IActivityDefinitionHasher
{
    string Hash(IActivityDefinition definition, IActivityDefinitionVersion version);
}
```

Plan-stage decision: take both the definition AND the most recent version as inputs, since the hash needs to capture both identity-level fields (display metadata) and version-level fields (descriptor, inputs/outputs/ports). Default impl combines a stable hash (e.g. SHA-256) over the canonicalised JSON of both inputs.

## Default implementation contract

- Deterministic: same inputs produce the same hash.
- Stable across process restarts and machine boundaries (no `GetHashCode()`).
- Stable across structurally-equivalent inputs that differ only in property order (canonicalises before hashing).
- Excludes mutable fields irrelevant to drift detection: `LastModifiedAt`, reconciliation-state fields themselves.

## Behaviour in the reconciler

`ActivityVersionReconciler` invokes the hasher when writing/updating an `ActivityDefinitionReconciliationState` row. The hash is stored in `ProvisioningHash`. On the next reconciliation pass, the reconciler compares the candidate's freshly-computed hash with the stored hash; mismatch → row needs update; match → skip the write.

## Failure modes

| Cause | Path |
|---|---|
| Hash function unavailable (unlikely; default is SHA-256) | Default impl is self-contained — no failure path expected. |
| Hash produces inconsistent results for equivalent inputs | Hash contract violation; surfaces as spurious re-writes during reconciliation. Caught by the hasher's own unit tests. |

## Test surface

- Deterministic test: same `(definition, version)` → same hash across calls.
- Stability test: structurally-equivalent inputs with different property orderings → same hash.
- Field-exclusion test: changing `LastModifiedAt` on the input definition does NOT change the hash.
- Field-inclusion test: changing the descriptor on the version DOES change the hash.

## Replacement story

A provider that wants kind-specific hashing (e.g. excluding certain fields for workflow-source activities) can register their own `IActivityDefinitionHasher` and the framework's replacement-contract conflict detection (§2.6.2) will surface multiple registrations at startup.
