# Quickstart / Verification

## Build

```bash
dotnet build tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
```

## Targeted tests

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
```

## What to observe

- Under the coalescing policy, an `ActivityAttemptClaimed` checkpoint stamped `ReplaySafe` decides `Deferred`; one stamped `External` (or with no profile metadata) decides `Immediate`. Every other mandatory name still decides `Immediate`.
- A coalesced segment of `ReplaySafe` activities that crashes mid-segment recovers by replay from the last flushed boundary and converges to byte-identical final committed state.
- An `External` activity's claim checkpoint flushes immediately, so its identity + input snapshot is durable before its body runs; attempt/poison attribution is unchanged.
- Two otherwise-identical contracts differing only in profile have different `SchemaFingerprint`; a default-`External` contract's fingerprint equals the profile-unaware path (no golden churn).
- Immediate mode (default runtime) is unaffected: every checkpoint flushes regardless of profile.

## Author usage

```csharp
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class MyPureRouter : StructuralActivity, IRuntimeStructuralActivity { ... }
```
Omit the attribute for any activity that performs an external effect — it defaults to `External`.
