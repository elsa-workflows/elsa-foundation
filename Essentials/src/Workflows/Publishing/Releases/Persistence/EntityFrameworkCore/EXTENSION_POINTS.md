# Consumer context composition

- **Layer:** Opt-in SQLite persistence implementation over the [archive Core ports](../Core/ReleaseArchiveContracts.cs).
- **Registration signatures:** `ModelBuilder AddReleaseArchiveModel(this ModelBuilder, string providerName)` maps the tables;
  `IServiceCollection AddReleaseArchiveStore<TContext>(this IServiceCollection, Func<IServiceProvider, SourceScope> scopeFactory,
  ReleaseBundleLimits? limits = null, bool requireDurable = true, bool replaceExisting = false)` selects the scoped backend. See the actual
  [model](ReleaseArchiveModelBuilderExtensions.cs) and [registration](ReleaseArchiveServiceCollectionExtensions.cs) signatures.
- **Kind and default:** Explicit host backend selection; one internal `EfReleaseArchiveStore<TContext>` facade implements
  reader, writer and capability ports. Competing selections are rejected; `replaceExisting: true` deliberately replaces a valid prior selection coherently.
  Final descriptors are checked before facade/context use and by `ValidateOnStart`.
- **Override when:** A host owns another context or deliberately selects another qualified coherent backend.
- **Dependent callers:** Host composition and the archive tests/restart probe. This feature installs no model owner or activation caller.

Hosts retain provider/options, schema and migration ownership. Context construction must satisfy the documented consumer contract.
Custom transport codecs resolve through `IReleaseBundleCodec`; this package adds no codec, authorization framework or startup task.
`requireDurable` defaults to true. Every opener sharing a SQLite pool must use the documented connection-safety mitigation.
