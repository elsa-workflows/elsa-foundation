# Elsa Foundation Identity OpenIddict Entity Framework Core

The token store for Elsa's OpenIddict feature (`FoundationIdentityOpenIddict`). OpenIddict keeps every access token, refresh token and authorization as a row, so a host that issues tokens needs a store. This package uses OpenIddict's own EF Core model in a companion `OpenIddictIdentityDbContext`, with migrations for SQLite, SQL Server and PostgreSQL. MySQL is refused, because its provider cannot run OpenIddict's token prune.

The store is host-owned, not a shell feature. Compose it once on the host's root container, before CShells; CShells copies the registrations into every shell, so all shells read one store:

```csharp
builder.Services.AddOpenIddictEntityFrameworkCoreStore(builder.Configuration);
```

It reads the default shell's `FoundationIdentityOpenIddict` settings:

| Setting | Meaning |
|---|---|
| `IsDevelopmentOrDemo` | `true` uses an in-memory store: per node, lost on stop. |
| `ConnectionString` | The store's database. SQLite defaults to `Data Source=identity.db`; another engine falls back to `ConnectionStrings:Elsa`. |
| `Provider` | `Sqlite` (default), `SqlServer` or `PostgreSql`. The store moves off SQLite only when this is set. Nodes share tokens only when they share the store. |
| `AutoMigrate` | Migrate at start (default `true`). A stale SQLite migration lock fails the start with the way to clear it. |
| `Prune:Enabled`, `Prune:Interval`, `Prune:MinimumAge`, `Prune:Timeout` | Removes expired, redeemed and revoked entries older than `MinimumAge` (14 days), every `Interval` (1 hour), on every node. |

`Elsa.Foundation.Identity.OpenIddict` itself carries no EF dependency; a host that wants another OpenIddict store (for example `OpenIddict.MongoDb`) composes that instead of this package.
