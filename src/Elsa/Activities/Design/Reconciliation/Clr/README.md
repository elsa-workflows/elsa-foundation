# Elsa.Activities.Design.Reconciliation.Clr

A reconciliation **source** (§2.6.1 DI-source pattern) that discovers CLR activities by scanning a
folder of assemblies and contributes one catalog row per discovered `IActivity` implementation. It
plugs into the universal reconciliation lifecycle owned by `Elsa.Activities.Design.Reconciliation`
without depending on that feature project — it references only `…Reconciliation.Core` (for the
`IActivityReconciliationSource` contract + model) and `Elsa.Activities.Runtime.Core` (for the
activity abstractions and the `[Version]` attribute it reads).

## What this feature provides

- **`ClrActivityReconciliationSource`** — `IActivityReconciliationSource` with `SourceKind => "CLR"`.
  Its `Read` drives the scanner and emits one `ActivityVersionReconciliationModel` per activity, each
  carrying `DescriptorType = typeof(ClrActivityDescriptor).FullName` and a `ClrActivityDescriptor`
  wrapping the activity's stable alias (`TypeAliasConvention.CanonicalAlias(type)`).
- **`ClrAssemblyScanner`** — reflection-only scanner (R5) built on `MetadataLoadContext` +
  `PathAssemblyResolver`. Discovers `IActivity` implementations by metadata, never loads author code
  into the execution context, and never pollutes the default `AssemblyLoadContext`. Resilient
  (FR-023): a DLL with no activities is skipped silently; a DLL that fails to load or reflect is
  logged and skipped; the pass never aborts wholesale.
- **`ActivityTypeVersionResolver`** (`IActivityTypeVersionResolver`) — pure version-precedence
  decision (FR-020): the `[Version]` attribute wins; else a valid `AssemblyInformationalVersion`; else
  the 4-part assembly version's `Major.Minor.Build` → `MAJOR.MINOR.PATCH`. Invalid attribute semver /
  unresolvable assembly version raise domain-scoped exceptions carrying the activity type + offending
  value (§2.23.5).
- **`ActivityTypeCategoryResolver`** (`IActivityTypeCategoryResolver`) — pure category decision: the
  last dot-separated segment of the declaring assembly's simple name (e.g.
  `Elsa.Runtime.Activities.Primitives` → `Primitives`), so every activity an author ships in one
  assembly shares one catalog bucket.
- **`ClrActivityReconciliationFeature`** — `IShellFeature` that registers the options plus the two
  resolvers, the scanner, and the source — each against its contract so an inheriting feature can
  replace one in isolation (§2.5). `ConfigureServices` is `virtual`; the options instance is the only
  singleton (an application-wide static value), everything else is scoped (§2.5.1). It does **not**
  derive from the reconciliation feature.

## What the scanner reads from an activity

The runtime carries no author-supplied UI metadata, so the scanner reads only structural shape plus
the two values derived from assembly/type identity:

- the CLR type **full name** → `ActivityTypeKey` (excludes assembly identity, FR-022);
- public `InputArgument<T>` / `OutputArgument<T>` properties → `InputDefinition` / `OutputDefinition`
  (`ReferenceKey` = `Name` = the property name; value `Type` from the generic argument);
- the `[Required]` attribute on an argument property → `IsRequired`;
- the resolved semver (above);
- the resolved category, derived from the declaring assembly's name (above).

`DisplayName` and `Description` are left null — they are author-supplied design-time concerns, not
something the runtime assembly is allowed to dictate. `Category` is the exception: it is *derived*
from assembly identity (not an author UI string), so it is structural metadata the scanner may set.

## Registration

Compose the feature alongside `ActivitiesDesignReconciliationFeature`; the universal
`CollectActivityVersions` discovers the source from DI:

```csharp
shell.AddFeature(new ClrActivityReconciliationFeature
{
    Options = { FolderPath = "/path/to/activity/plugins" }
});
```

## Options

- **`ClrReconciliationOptions.FolderPath`** — the folder scanned for activity-bearing assemblies.
- **`ClrReconciliationOptions.SourceId`** — the source identity recorded on every contributed row.
  When unset it defaults to the normalised `FolderPath` (R3), so two distinct folders are distinct
  sources.

## Constitutional basis

§2.6.1 (DI-source pattern), §E2.2 (the `…Reconciliation.Clr → Elsa.Activities.Runtime.Core`
Design→Runtime edge is the allowed direction), §E2.8 (author-controlled semver), §2.23.5
(domain-scoped faults).
