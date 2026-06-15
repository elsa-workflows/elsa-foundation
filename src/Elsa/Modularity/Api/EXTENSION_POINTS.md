# Extension points - Modularity domain

The Modularity API feature owns the shell feature-management surface.

## Overridable contracts

| Contract | Default impl | Override when |
|---|---|---|
| `IShellFeatureConfigurationStore` | `JsonShellFeatureConfigurationStore` (`Elsa.Modularity.Api`) | The host stores shell configuration somewhere other than `shells.json`. |
| `IShellReloader` | `ShellReloader` (`Elsa.Modularity.Api`) | The host needs custom reload semantics or shell inference. |
| `IRuntimeFeatureCatalogRefresher` | `RuntimeFeatureCatalogRefresher` (`Elsa.Modularity.Nuplane`) | CShells exposes a direct public refresh contract or another catalog source is used. |

## Implementable contributor interfaces

| Contract | Purpose |
|---|---|
| `IFeatureCatalogContributor` | Adds runtime, package, or host-specific metadata to the feature catalog returned by `IFeatureManagementService`. |

Known default contributors:

- `RuntimeFeatureCatalogContributor` - merges CShells runtime feature descriptors.
- `PackageManifestFeatureCatalogContributor` - merges Nuplane package manifest metadata and feature settings.
