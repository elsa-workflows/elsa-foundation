# Extension points - Modularity domain

The Modularity API feature owns the shell feature-management surface.

## Overridable contracts

| Contract | Default impl | Override when |
|---|---|---|
| `IShellFeatureConfigurationStore` | `JsonShellFeatureConfigurationStore` (`Elsa.Modularity.Api`) | The host stores shell configuration somewhere other than `shells.json`. |
| `IShellReloader` | `ShellReloader` (`Elsa.Modularity.Api`) | The host needs custom reload semantics or shell inference. |
| `IRuntimeFeatureCatalogAccessor` | `RuntimeFeatureCatalogAccessor` (`Elsa.Modularity.Nuplane`) | The host uses another runtime catalog source instead of CShells feature assembly providers. |
| `IRuntimeFeatureCatalogRefresher` | `RuntimeFeatureCatalogRefresher` (`Elsa.Modularity.Nuplane`) | The host needs custom refresh/reload reporting semantics. |

## Implementable contributor interfaces

| Contract | Purpose |
|---|---|
| `IFeatureCatalogContributor` | Adds runtime, package, or host-specific metadata to the feature catalog returned by `IFeatureManagementService`. |
| `IFeatureActivationGuard` | Refuses an apply request before `FeatureManagementService.ApplyAsync` saves it. Every registered guard runs after the request is validated and before the save, so a refusal leaves `shells.json`, the runtime catalog and the shells untouched, and surfaces as an HTTP 409. A guard must keep the request's restored secrets - connection strings above all - out of its refusal, out of any exception, and out of any log (spec 171 FR-059-FR-061, ADR 0076 D9). |

Known default contributors:

- `RuntimeFeatureCatalogContributor` - merges CShells runtime feature descriptors discovered from public feature assembly providers.
- `PackageManifestFeatureCatalogContributor` - merges Nuplane package manifest metadata and feature settings.

Known activation guards:

- `EfPendingMigrationActivationGuard` (`Elsa.Modularity.EntityFramework`) *(cross-domain)* - refuses a feature whose `[UsesEfModule]` module has a pending migration while the host runs `Elsa:Persistence:EntityFramework:Migrate:Policy=Validate`, naming the `dotnet elsa persistence` command to run. Composed with `AddEfPendingMigrationActivationGuard()`; a host that composes no guard keeps the older behaviour, where the shell's own validate check refuses after the save.
