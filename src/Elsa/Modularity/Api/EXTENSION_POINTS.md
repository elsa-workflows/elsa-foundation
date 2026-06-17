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

Known default contributors:

- `RuntimeFeatureCatalogContributor` - merges CShells runtime feature descriptors discovered from public feature assembly providers.
- `PackageManifestFeatureCatalogContributor` - merges Nuplane package manifest metadata and feature settings.
