# Phase 1 Contract Surfaces — Activity Semantic Versioning

Illustrative signatures for the changed/added contract surfaces. These are design intent for `/speckit.tasks`, not final code.

## 1. Read contract (`Elsa.Activities.Design.Core`)

```csharp
public interface IActivityDefinitionVersion
{
    string Id { get; }
    string Version { get; }          // was int
    string DefinitionId { get; }
    string ImplementationKind { get; }
    IImplementationDescriptor ImplementationDescriptor { get; }
    IActivityDefinition Definition { get; }
    IEnumerable<InputDefinition> Inputs { get; }
    IEnumerable<OutputDefinition> Outputs { get; }
    IEnumerable<ActivityDesignFacet> DesignFacets { get; }
    ActivityExecutionType ExecutionType { get; }
    string? ReconcilliationHash { get; }
    // SemVerSortKey is NOT here — persistence-only, §2.9.1.
}
```

## 2. Reconciliation contracts — RELOCATED to `Elsa.Activities.Design.Reconciliation.Core` (FR-021)

```csharp
// was in the feature project; moves to .Core
public interface IActivityReconciliationSource
{
    ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken);
    string SourceId { get; }
    string SourceKind { get; }
}

public sealed record ActivityVersionReconciliationModel(
    string? Id,
    string Version,                  // was int
    string ActivityTypeKey,
    string? DisplayName,
    string? Category,
    string? Description,
    string ImplementationKind,
    object ImplementationDescriptor,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    IEnumerable<ActivityDesignFacet> DesignFacets,
    ActivityExecutionType ExecutionType = ActivityExecutionType.Action);
```

## 3. Reconciliation feature — RESHAPED, non-abstract (FR-021)

```csharp
public class ActivitiesDesignReconciliationFeature : IShellFeature   // public, NOT abstract, NOT sealed
{
    public ActivityVersionReconcilerOptions ReconcilerOptions { get; set; } = new();
    public ActivityVersionReconcilerStartupTaskOptions StartupTaskOptions { get; set; } = new();

    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Options.Create(ReconcilerOptions));
        services.AddSingleton(Options.Create(StartupTaskOptions));
        services.AddSingleton<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();  // replaceable §2.6.2
        services.AddScoped<IActivityVersionReconciler, ActivityVersionReconciler>();
        // + the single startup task + the universal ActivityVersionsReconcilingHandler
    }
    // NO `Sources` property. NO source registration here.
}
```

The universal `ActivityVersionsReconcilingHandler` is unchanged: it injects `IEnumerable<IActivityReconciliationSource>`, calls each `Read(...)`, and contributes versions to `ActivityVersionsReconciling`.

## 4. CLR source feature — NEW `Elsa.Activities.Design.Reconciliation.Clr` (FR-010)

```csharp
public class ClrReconciliationOptions
{
    public string FolderPath { get; set; } = default!;   // folder of activity DLLs
    public string? SourceId { get; set; }                 // optional; defaults to normalised FolderPath (R3)
}

public class ClrActivityReconciliationFeature : IShellFeature   // CLEAN feature — does NOT derive from the reconciliation feature
{
    public ClrReconciliationOptions Options { get; set; } = new();

    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddSingleton<IActivityReconciliationSource, ClrActivityReconciliationSource>();
    }
}

public sealed class ClrActivityReconciliationSource : IActivityReconciliationSource
{
    public string SourceKind => "CLR";
    public string SourceId { get; }   // from options (R3)
    public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken ct);
    // scans Options.FolderPath via MetadataLoadContext (R5), discovers IActivity impls,
    // reads metadata + resolves version (R4/FR-020), emits one model per activity with ImplementationKind="CLR".
}
```

## 5. Runtime activity abstraction — MOVED to `Elsa.Activities.Runtime.Core` (FR-009/FR-018)

```csharp
public interface IActivity
{
    string Type { get; set; }
    string Version { get; set; }     // was int — semver
    // …
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class VersionAttribute(string version) : Attribute   // NEW, optional
{
    public string Version { get; } = version;
}
```

## 6. Ordering (`Elsa.Activities.Design.Persistence.Core`)

```csharp
public sealed class ActivityVersionOrderDefinition(OrderDirection direction = OrderDirection.Descending)
    : OrderDefinition<ActivityDefinitionVersion, string>(v => v.SemVerSortKey, direction);   // was <…, int>(v => v.Version)
```

## 7. Semver value + comparator (R1)

```csharp
public readonly struct SemVer : IEquatable<SemVer>   // parse, precedence, normalise-to-sort-key, equality-ignoring-build-metadata
{
    public static bool TryParse(string s, out SemVer v);
    public string ToSortKey();        // normalised, zero-padded, prerelease < release
    // throws a domain-scoped exception on invalid input at the source boundary (§2.23.5)
}

public sealed class SemVerComparer : IComparer<SemVer> { /* Strategy, §2.24.2 row 9 */ }
```

## 8. Exceptions (domain-scoped, §2.23.5)
- `ActivityVersionHashMismatchException` — version param `int → string`.
- Invalid-semver and unresolvable-assembly-version faults raised by the CLR source as domain-scoped exceptions carrying the activity type + offending value (no raw `FormatException`/reflection exception escapes).
