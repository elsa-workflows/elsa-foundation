# Extension points — Foundation Data Protection

The host's ASP.NET Core Data Protection composition and its EF key store (#2191). The key ring protects the sign-in
cookie, the antiforgery tokens and every other protected payload of every shell, so it is the host's: composed once, on
the host container, never by a shell feature.

## Composition

- **From configuration.** `AddConfiguredDataProtection(configuration)` (namespace `Elsa.Foundation.DataProtection`, so a
  host names no EF type) is what `Elsa.Workbench` and `Elsa.Foundation.Host` call. It always sets the application name
  to `DataProtectionConfigurationExtensions.ApplicationName` (`Elsa`), never the default derived from the content root. It
  enables the EF key store only when `Elsa:DataProtection:EntityFrameworkCore:Enabled` is `true`, with `Provider`,
  `ConnectionString` or `ConnectionName` (falling back to `ConnectionStrings:Elsa`), `Schema` and `Pooling` beside the
  switch. It encrypts the keys at rest with the PKCS#12 certificate under `Elsa:DataProtection:Certificate` (`Path`, read
  from the content root when relative, and `Password`). Settings without the switch, a value that does not parse, a
  certificate that cannot be loaded or holds no private key, and a certificate without the key store are refused while the
  host composes itself, naming the key.
- **In code.** `IDataProtectionBuilder.PersistKeysToElsaDatabase(EfDataProtectionKeyStoreOptions)` composes the key store
  alone. Call it on the host container, once; a second call is refused.

## The key store

`EfDataProtectionKeyRepository` is an `IXmlRepository` over the `DataProtection.Keys` EF module: one table,
`elsa_data_protection_keys`, on SQLite, SQL Server, PostgreSQL or MySQL, whose rows belong to the `DataProtectionKeys`
schema family (`1.0.0`), with the element's `Xml` as the family's content column. Rows are only added; the key manager
never rewrites or deletes one, so the store implements no deletion. A row stamped with a version this build cannot read
fails the key ring's read rather than being skipped.

- **One instance per host.** The repository is built in the host's container and shared with every shell container
  through `ShareWithShells` (`Elsa.Cluster.Readability`), so a shell reads the host's connection, not its own
  configuration's, and writes through the finalization gate the host's migrator admitted.
- **Migrations.** The module migrates through the plain-host migrator only (`AddEfModuleHostMigrations`), as the host
  starts. Data Protection's own hosted service, which loads the key ring as the host starts, is moved behind that
  migrator, so a host's first start reads the table the migrator has just created rather than logging the key ring as
  failed to load.

## Startup warnings

`DataProtectionStartupCheck` warns as the host starts about the two compositions whose failure looks like success: a
host that composed a durable cluster membership provider while no key repository is configured, and the EF key store
without a certificate. A key repository a host composes in code counts as shared. Neither is a refusal: which shells
protect anything is not known while the host starts.

## Replacing the store

Any other `IXmlRepository` a host configures in code, through `KeyManagementOptions.XmlRepository` or one of ASP.NET
Core's `PersistKeysTo…` extensions, takes the store's place; leave the EF key store disabled then, since the last
configuration of `KeyManagementOptions` wins. Share such a repository with the shells the same way, or each shell builds
its own copy from the host's registrations.
