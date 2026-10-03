# Foundation Host demo identity

`Elsa.Samples.Nuplane.Demo.Identity` is a feed-loaded CShells feature for `Elsa.Foundation.Host`. It keeps
Foundation.Host feature-free while supplying the authentication composition that Workbench owns in its process root:

- Foundation Identity IAM and provider-configuration stores over SQLite.
- ASP.NET Core Identity login and cookie sessions with an optional seeded administrator.
- Foundation OpenIddict behavior plus an EF Core token store in a separate SQLite database.
- `/_elsa/identity/*` protocol endpoints and `/_elsa/identity/login` login endpoints.
- Credentialed CORS middleware for the Studio origin, ordered before authentication and authorization.

The feature ID is `FoundationDemoIdentity`. Enable it in the Foundation Host shell configuration:

```json
{
  "CShells": {
    "Shells": {
      "default": {
        "Features": {
          "FoundationDemoIdentity": {
            "IdentityConnectionString": "Data Source=foundation-host-demo-identity.db",
            "OpenIddictConnectionString": "Data Source=foundation-host-demo-openiddict.db",
            "Issuer": "http://localhost:5311/",
            "SeedAdminUserName": "admin",
            "SeedAdminPassword": "Password123!",
            "SeedAdminEmail": "admin@elsa.local",
            "SeedAdminRoleName": "administrator",
            "AllowedOrigins": [
              "http://localhost:5313",
              "http://127.0.0.1:5313"
            ]
          }
        }
      }
    }
  }
}
```

The feed root is `Elsa.Samples.Nuplane.Demo.Identity`. Its package closure must retain the host-compatible Elsa
contracts and these identity/provider roots: `Elsa.Foundation.Identity.Api`,
`Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore`, `Elsa.Foundation.Identity.OpenIddict`,
`Elsa.Foundation.Identity.Persistence.EntityFrameworkCore`, `Elsa.Persistence.EntityFramework`,
`Microsoft.EntityFrameworkCore.Sqlite`, and `OpenIddict.EntityFrameworkCore`.

The package declares the `ef-provider` capability for SQLite. A Foundation Host must select that capability and feed
the full package closure, including this package's Foundation Identity dependencies, `Microsoft.EntityFrameworkCore.Sqlite`,
and `OpenIddict.EntityFrameworkCore`. The host's shared EF Core and Elsa contract assemblies must remain the single copies.

The demo flag requires `ASPNETCORE_ENVIRONMENT=Development`; with no `SigningKey`, Elsa uses its existing
process-stable development key and relaxed local cookie transport. That key survives shell/package reloads in the same
Foundation Host process, but access tokens issued before a process restart require a new sign-in. Set the existing
`SigningKey` setting to a stable base64 PKCS#8 RSA private key when the demo must survive a process restart; an optional
`EncryptionKey` can be supplied separately. The seeded password is a demo secret and should be supplied through shell
configuration or an environment-specific overlay. Foundation Host data protection should be configured with its
existing host-level settings when cookies need to survive process recreation or be shared by the two demo hosts.

On shell activation, the IAM/provider-configuration EF modules run through the standard Elsa migration lifecycle, and the
portable OpenIddict context ensures its own schema. The OpenIddict database is deliberately separate because EF's
`EnsureCreated` must not be pointed at the already-populated IAM database.

The feature deliberately stays in legacy connection-string mode. It owns two EF modules, so it must not carry
`EfPersistenceResourceParticipant`: named-resource enrollment requires one module and one context identity per feature.
The EF tooling still discovers both `Identity.Iam` and `Identity.ProviderConfiguration` through `UsesEfModule`; use the
host-selected module operation with an explicit identity database connection when preparing them. The CLI's `all`
selection means every EF module in the host closure, so it is not an identity-only target.
