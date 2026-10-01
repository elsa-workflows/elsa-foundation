# Identity configuration (first-party auth & bearer issuance)

This is the operator-facing configuration reference for the Elsa Foundation identity stack that secures the
`Elsa.Workbench` API and issues the bearer tokens the Studio shell consumes. It covers the features you enable,
the settings each one takes, the development defaults, and the **go-live checklist** you must complete before
running outside `Development`.

For the design rationale see [`docs/plans/studio-bearer-token-issuance.md`](../plans/studio-bearer-token-issuance.md).

## The moving parts

| Feature (shells.json key) | Role |
|---|---|
| `FoundationIdentityAbstractions` | Provider-agnostic auth/IAM contracts and the permission catalog. |
| `FoundationIdentityOidc` | External OpenID Connect / JWT-bearer provider (optional; for an upstream IdP). |
| `FoundationIdentityApi` | The provider-agnostic identity endpoints: `bootstrap`, `capabilities`, `session`, `challenge`, `logout`, `refresh`, and the `GET /_elsa/identity/token` cookie→bearer exchange. Permission checks use the shared Foundation policy path. |
| `FoundationIdentityAspNetCoreIdentity` | The provider-neutral ASP.NET Core Identity substrate (managers, principal factory, sign-in service, provider module, antiforgery). |
| `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` | EF Core-backed durable user/role stores, `SignInManager` cookie sign-in, the login page/endpoints, and configured admin seeding. |
| `FoundationIdentityOpenIddict` | OpenIddict-backed JWT access-token issuance + local bearer validation for the API surface. |

The composite scheme selector these register becomes the default authenticate/challenge scheme, so an
unauthenticated API call is rejected with `401`. A host-chosen `DefaultScheme` always wins.

## Development / demo defaults

The checked-in `src/apps/Elsa.Workbench/shells.json` enables the stack with `IsDevelopmentOrDemo: true`, which is
intended **only** for local development and demos:

```jsonc
"FoundationIdentityApi": {},
"FoundationIdentityAspNetCoreIdentity": {},
"FoundationIdentityAspNetCoreIdentityEntityFrameworkCore": {
  "IsDevelopmentOrDemo": true,
  "SeedAdminUserName": "admin",
  "SeedAdminPassword": "Password123!",
  "SeedAdminEmail": "admin@elsa.local",
  "SeedAdminRoleName": "administrator"
},
"FoundationIdentityOpenIddict": { "IsDevelopmentOrDemo": true }
```

Under `IsDevelopmentOrDemo`:

- **Identity stores use the configured EF Core provider** — with the default Workbench SQLite provider, users,
  roles, external identities, and tenant memberships survive a restart.
- **Signing/encryption keys are ephemeral per process** — issued tokens do not survive a restart.
- **An admin account is seeded** from the `SeedAdmin*` settings above — the committed dev defaults are
  username `admin`, password `Password123!` (logged prominently at startup). There are no credential
  constants in code; the values come entirely from configuration. The administrator role is granted the
  all-access permission (`*`), so it can reach every `ConfigurePermissions()`-secured endpoint.
- **The sign-in cookie relaxes to `SameAsRequest`** so a plain-HTTP `localhost` host can establish a session.

## Configuration surface (production)

Set these on the relevant feature in `shells.json` (or via any configuration provider — environment variables,
etc.). Secrets should come from a secret store, not source control.

### `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore`

| Setting | Meaning | Production requirement |
|---|---|---|
| `IsDevelopmentOrDemo` | Enables development/demo seeding and the relaxed local cookie posture. | **`false`.** |
| `SeedAdminUserName` | Username of an administrator to provision at startup. | Optional. Requires `SeedAdminPassword`. |
| `SeedAdminPassword` | Password for the seeded administrator. | **Supply via a secret** (user-secrets / environment variable), never committed. |
| `SeedAdminEmail` | Email for the seeded administrator. | Optional; defaults to `<username>@elsa.local`. |
| `SeedAdminRoleName` | Role granted to the seeded administrator. | Optional; defaults to `administrator`. |

The EF Core module owns the identity tables and their migrations; set its `Provider` and `ConnectionString`
(or let it fall back to `ConnectionStrings:Elsa`) to configure the storage connection. The seed account
is defined **entirely by the `SeedAdmin*` settings** on both the dev/demo and production
paths — there are no credential constants in code. The committed `admin` / `Password123!` values apply only
under `IsDevelopmentOrDemo`. In production, either provision users through your own onboarding, or seed a first
administrator by setting `SeedAdminUserName` and supplying `SeedAdminPassword` from a secret store (its password
is never written to the log; the username xor password half-configured is a startup error).

### `FoundationIdentityOpenIddict`

| Setting | Meaning | Production requirement |
|---|---|---|
| `IsDevelopmentOrDemo` | In-memory token store (per node, lost when the host stops, whatever `Provider` says) + ephemeral keys. | **`false`.** |
| `Issuer` | Logical issuer URI written into (and required from) first-party access tokens. | Set to a stable absolute URI, e.g. `https://elsa.example.com/`. |
| `SigningKey` | Base64-encoded **PKCS#8 RSA private key** of at least 2048 bits, used to sign access tokens (RS256). Falls back to `FoundationIdentityOptions.SigningKey`. | **Required.** See generation command below. |
| `EncryptionKey` | Key material for OpenIddict's encryption credentials. Defaults to a key derived (domain-separated) from `SigningKey`. | Recommended: set a **distinct** value from `SigningKey`. |
| `Provider` | The database engine under the token store: `Sqlite` (the default when unset), `SqlServer` or `PostgreSql`. Any other value, `MySql` included, fails the host's start, see [Where the token store lives](#where-the-token-store-lives). **Ignored under `IsDevelopmentOrDemo`**, whose token store is in memory and per node (the host warns at start when both are set). | **Not `Sqlite` when more than one node serves requests.** |
| `ConnectionString` | Connection string for the OpenIddict token store, in the syntax of `Provider`. Without one, `Sqlite` uses its own file (`identity.db`) and another engine uses `ConnectionStrings:Elsa`, the connection every Elsa EF module shares. | Optional for one node; set for a dedicated token DB. |
| `AutoMigrate` | Lets Workbench's host-owned OpenIddict EF provider migrate its schema during startup. Defaults to `true`. | Safe to leave on for several nodes on `SqlServer` or `PostgreSql`, which serialise concurrent migrations; turn off if you apply migrations out-of-band. |
| `Prune:Enabled` | Whether this node prunes the token store. Defaults to `true`. | Leave on, see [Pruning the token store](#pruning-the-token-store). |
| `Prune:Interval` | The time between prunes, a `TimeSpan`. Defaults to `01:00:00`. The first prune runs when the host starts. | Optional. |
| `Prune:MinimumAge` | Only entries created longer ago than this are pruned, a `TimeSpan`. Defaults to `14.00:00:00`. | Optional. |
| `Prune:Timeout` | How long one prune call (the tokens', then the authorizations') may take before it is cancelled and left for the next interval, a `TimeSpan`. Defaults to `00:10:00`. | Optional. |

`Elsa.Foundation.Identity.OpenIddict` contains only provider-neutral OpenIddict behavior. It no longer references
EF Core, and the former `configureDbContext` parameter on `AddFoundationIdentityOpenIddict` has been removed.
Hosts must register an OpenIddict vendor store explicitly before composing the feature. Workbench makes that host
choice with `OpenIddict.EntityFrameworkCore`; another host may select a different vendor provider.

#### Where the token store lives

Every access token and every refresh token the server issues is a row in the token store, and validating an access token reads
its row (token-entry validation is on). A bearer token issued by one node is therefore only valid on another node if both read
**the same store**. **A deployment of more than one node needs a shared store:** set `Provider` to `SqlServer` or `PostgreSql`
and point every node at one database. The default, a SQLite file, is for one node, or for nodes that share one file, and the demo store
(`IsDevelopmentOrDemo`) is in memory and per node, so it is for one node too.

The store moves off SQLite only when `Provider` is set, so an existing store never moves silently. When the platform's persistence
provider (`Elsa:Persistence`) is another engine and `Provider` is not set, the host warns at start that the token store is still a
per-node SQLite file and names the setting to change.

A node also needs the **same Data Protection keys** as the others, so a token or cookie one node protects the others can read. That
is a separate, shared key ring, configured through the host's Data Protection settings (tracked in
[#2191](https://github.com/elsa-workflows/elsa-foundation/issues/2191)); the token store does not provide it. A multi-node
deployment needs both the shared store and the shared keys.

The store follows the platform's provider conventions: the engine names are the ones every Elsa EF module takes, and without a
`ConnectionString` another engine uses `ConnectionStrings:Elsa`. The shared persistence resource (`Elsa:Persistence`) does not
select the token store, which is host-owned. On SQL Server and PostgreSQL the tables and the migrations history table
(`__EFMigrationsHistory_OpenIddict`) are both in the `Identity` schema, so the history sits beside the tables it records. MySQL is not supported: OpenIddict's prune deletes through a subquery with a
limit, which MySQL refuses and its provider does not rewrite, so a MySQL store could never be pruned.

#### Pruning the token store

Nothing else deletes a token or an authorization, so Workbench prunes the store, calling OpenIddict's own `PruneAsync` on the token
manager and then the authorization manager. It removes tokens that are expired, redeemed or revoked, or whose authorization is no
longer valid, and authorizations that are not valid or are ad hoc with no token left; a token that is still valid is never
removed. An entry is only pruned once it is older than `Prune:MinimumAge`, so a redeemed refresh token stays long enough to be
recognised if it is presented again.

The prune runs as a hosted service on **every node**, on the `Prune:Interval`, and the first runs when the node starts. It is not
claimed by one node, because a prune is idempotent: what one node has deleted another finds already gone, and a prune that fails
(a store not migrated yet, a sibling's delete in the way) is logged and tried again on the next interval, never thrown into the
host. It is a root hosted service and not an `IRecurringTask` because the token store is host-owned and registered once for the
process, while a recurring task belongs to a shell's Tasks feature, would run once per shell that enables it, and would not run in a
host that does not. Each prune call runs under `Prune:Timeout`, so a call that hangs is cancelled and cannot hold the node's later
prunes.

Generate a signing key:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 | openssl pkcs8 -topk8 -nocrypt -outform DER | base64
```

Pipe through `openssl pkcs8 -topk8`: `genpkey -outform DER` on its own writes a PKCS#1 key, which the host
rejects. (The same command appears in the errors `ConfigureOpenIddictServerOptions` throws for a malformed or short
key.) Outside `IsDevelopmentOrDemo`, a missing or malformed signing key, or an RSA key under 2048 bits, fails with a
clear error when the OpenIddict
server options are built. The feature builds them at startup, so the error fails shell activation (in Workbench,
`/health/ready` reports `503 shell_activation_failed`) rather than the first request that authenticates. An
`Issuer` that `System.Uri` cannot parse as absolute fails activation the same way, in any mode, with a
`UriFormatException`.

### `FoundationIdentityOidc`

Only for an external IdP. Its settings are `Authority`, `ClientId`, `ClientSecret` (secret; supply it from a
secret store), `RequireHttpsMetadata` and `IsDefault`. With no `ClientId` the interactive sign-in handler is not
registered and the provider only validates bearer tokens. For what each setting means and per-IdP recipes, see
[authentication-architecture §8](authentication-architecture.md#8-per-idp-recipes-for-the-oidc-module).

### `FoundationIdentityOptions` (shared, bound from the `Elsa:Identity` section if you surface it)

| Setting | Default | Notes |
|---|---|---|
| `SigningKey` | — | Fallback signing key material for the OpenIddict server. |

### Cookie / session hardening

The sign-in cookie is `HttpOnly`, `SameSite=Lax`, sliding-expiration, and — outside `IsDevelopmentOrDemo` —
`SecurePolicy=Always` (HTTPS-only). Serve the server over HTTPS in production or the session cookie will be
dropped by the browser.

### CSRF

The backend-served login page (`GET /_elsa/identity/login`) embeds an antiforgery token and sets the paired
cookie; the login `POST` validates it for the HTML-form flow. JSON API callers are unaffected. No configuration
is required.

### No API kill-switch

The former `ApiSecurity.AllowAnonymous` setting has been removed, and no configuration disables authentication for a
shell's API routes. Workflow-defined HTTP endpoints are anonymous unless their `HttpEndpoint` activity sets
`Authorize`; see [Security posture](authentication-architecture.md#7-security-posture).

## Same-origin hosting

The Studio client requests `{shell-origin}/_elsa/identity/token` with `credentials: include`, so host the SPA
same-origin as the server for the session cookie to flow. Cross-origin setups require CORS plus
`SameSite=None; Secure` cookies — avoid unless necessary.

## Production go-live checklist

1. `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore.IsDevelopmentOrDemo = false` and configure its
   relational `Provider` and connection.
2. `FoundationIdentityOpenIddict.IsDevelopmentOrDemo = false`.
3. `FoundationIdentityOpenIddict.SigningKey` = base64 PKCS#8 RSA private key (generated with the command above),
   sourced from a secret store.
4. `FoundationIdentityOpenIddict.EncryptionKey` = a distinct base64/secret value (recommended).
5. `FoundationIdentityOpenIddict.Issuer` = your stable absolute issuer URI.
6. If you compose `FoundationIdentityOidc`, keep its `RequireHttpsMetadata` setting at the default `true` so the
   upstream IdP's metadata must be HTTPS, and supply `ClientSecret` from a secret store. Either way, serve the
   server over **HTTPS** (so the `SecurePolicy=Always` session cookie is accepted).
7. Provision real user accounts — either through your own onboarding, or by setting `SeedAdminUserName` with a
   secret `SeedAdminPassword` (the committed dev `admin`/`Password123!` values apply only under `IsDevelopmentOrDemo`).
8. Remove any leftover `ApiSecurity` entry from shell feature lists: the feature no longer exists, and CShells
   logs a warning listing the unknown feature names.
9. Host the Studio SPA same-origin, and set `Studio:Auth:Enabled=true`.
10. **Choose the token store, and apply its migrations.** For more than one node, select a shared engine
    (`Provider`: `SqlServer` or `PostgreSql`) and the one database every node reads (see
    [Where the token store lives](#where-the-token-store-lives)). Workbench migrates its host-owned vendor EF schema at
    startup while `AutoMigrate=true`; the engines that have one serialise concurrent migrations with their own lock. To apply
    the migrations out-of-band instead, set `AutoMigrate=false` and apply them once as a deploy step against the context of
    your engine (`OpenIddictIdentityDbContext` for SQLite, `OpenIddictIdentitySqlServerDbContext`,
    `OpenIddictIdentityPostgreSqlDbContext`) before starting nodes:

    ```bash
    dotnet ef database update \
      --context OpenIddictIdentityPostgreSqlDbContext \
      --project src/apps/Elsa.Workbench \
      -- --connectionString "<connection string>"
    ```

    The Elsa IAM schema is owned by `IdentityIamEntityFrameworkCore` and migrates separately from the
    OpenIddict vendor context.

If the signing key is missing, malformed, or under 2048 bits outside `IsDevelopmentOrDemo`, startup fails (shell activation, for a
shell host) with an error that says how to fix it (see the `SigningKey` note above). The encryption key falls back to
the signing key, so it cannot be missing on its own. A missing key never silently degrades to an insecure default.

`IsDevelopmentOrDemo` is also **safe by construction**: if it is left `true` while the host runs in any
environment other than `Development` (e.g. the unedited default deployed to Production), the host **hard-fails
at startup** with an actionable message rather than silently booting the insecure posture (ephemeral keys +
the administrator seeded from the committed dev credentials). There is no insecure escape hatch in production — set
`IsDevelopmentOrDemo = false` (and configure real keys) for any non-Development deployment.

### Environment overlays override `shells.json`

`Elsa.Workbench` layers `shells.{Environment}.json` **on top of** `shells.json` (see `Program.cs`), and the
shipped `shells.Production.json` resets `IsDevelopmentOrDemo` to `false` for both identity features. In a
container the default environment is `Production`, so editing (or mounting) `shells.json` with
`"IsDevelopmentOrDemo": true` has **no effect** — the overlay wins, the flag is `false`, and with no signing
key configured the default shell fails activation with the
"No signing key is configured for the OpenIddict identity module" error. (The overlay also blanks the seed admin
password; if that is missing too, activation fails on it first.) This is why the same image behaves
differently under `ASPNETCORE_ENVIRONMENT=Development` (no `shells.Development.json` exists, so the
`shells.json` value survives — and the Development environment also satisfies the startup guard above). For a
non-Development demo host, don't chase the flag: configure a real `SigningKey` per the go-live checklist. For
a throwaway local demo, run the container with `ASPNETCORE_ENVIRONMENT=Development`.
