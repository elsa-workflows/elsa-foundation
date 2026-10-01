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
| `IsDevelopmentOrDemo` | In-memory token store + ephemeral keys. | **`false`.** |
| `Issuer` | Logical issuer URI written into (and required from) first-party access tokens. | Set to a stable absolute URI, e.g. `https://elsa.example.com/`. |
| `SigningKey` | Base64-encoded **PKCS#8 RSA private key** of at least 2048 bits, used to sign access tokens (RS256). Falls back to `FoundationIdentityOptions.SigningKey`. | **Required.** See generation command below. |
| `EncryptionKey` | Key material for OpenIddict's encryption credentials. Defaults to a key derived (domain-separated) from `SigningKey`. | Recommended: set a **distinct** value from `SigningKey`. |
| `ConnectionString` | Sqlite connection string for the OpenIddict token store. | Optional; set for a dedicated token DB. |
| `AutoMigrate` | Lets Workbench's host-owned OpenIddict EF provider migrate its schema during startup. Defaults to `true`. | Turn off for multi-instance deployments that apply migrations out-of-band. |

`Elsa.Foundation.Identity.OpenIddict` contains only provider-neutral OpenIddict behavior. It no longer references
EF Core, and the former `configureDbContext` parameter on `AddFoundationIdentityOpenIddict` has been removed.
Hosts must register an OpenIddict vendor store explicitly before composing the feature. Workbench makes that host
choice with `OpenIddict.EntityFrameworkCore`; another host may select a different vendor provider.

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

### Data Protection key ring

The sign-in cookie and the antiforgery tokens are protected with ASP.NET Core Data Protection, whose key ring is the
host's: `Elsa.Workbench` and `Elsa.Foundation.Host` compose it once on the host container with
`AddConfiguredDataProtection(configuration)` (`Elsa.Foundation.DataProtection`), and every shell reads the host's key store.
Its application name is configured, `Elsa` by default, never derived from the host's content root, so the hosts of one
deployment still read each other's payloads when they share a key ring, wherever each is installed.

| Setting (under `Elsa:DataProtection`) | Default | Notes |
|---|---|---|
| `ApplicationName` | `Elsa` | The name every protected payload is bound to. Give every host of one deployment the same name, and each deployment that must not read another's payloads a name of its own; see below. |
| `EntityFrameworkCore:Enabled` | `false` | Keeps the key ring in the platform database, in the `DataProtection.Keys` EF module's `elsa_data_protection_keys` table, so every host on that database shares it and a recreated host keeps it. `false` leaves it where ASP.NET Core keeps it by default, on this machine. |
| `EntityFrameworkCore:Provider` | `Sqlite` | `Sqlite`, `SqlServer`, `PostgreSql` or `MySql`. SQLite serves several processes on one machine only. |
| `EntityFrameworkCore:ConnectionString`, `ConnectionName` | `ConnectionStrings:Elsa` | As for every other EF module. `Schema` and `Pooling` are accepted too. |
| `Certificate:Path`, `Certificate:Password` | — | A PKCS#12 certificate with its private key, the same on every host, that encrypts every key at rest. A relative path is read from the content root. |

- **The application name is the boundary between deployments.** The key table records no application, and ASP.NET
  Core's default key directory is shared by every application one user runs on a machine. Deployments that share a key
  store, a database or a machine and use the same name read each other's sign-in cookies and antiforgery tokens: two
  Elsa deployments on one machine, both left at `Elsa` without the key store, now do, where each used to be isolated by
  its content root. A deployment that must not share keys with another gets its own `ApplicationName`, its own key
  store (its own database or `EntityFrameworkCore:Schema`), or both.
- **Clustered hosts share it.** A host that enables durable cluster membership but keeps its key ring to itself logs a
  warning as it starts, naming `Elsa:DataProtection:EntityFrameworkCore:Enabled`: behind a load balancer without sticky
  sessions, a cookie or antiforgery token issued by one host is refused by the next. It is a warning rather than a
  refusal because only the shells that sign users in need it, and a cluster whose features sign nobody in has nothing
  to share.
- **Keys are encrypted at rest only with a certificate.** Without one, each key's secret is stored as written, and
  whoever can read the table can forge a sign-in; the host warns about that every time it starts. DPAPI is not used,
  because it ties the keys to one machine, which defeats sharing them.
- **Misconfiguration is refused at startup, naming the key**: settings under `EntityFrameworkCore` without `Enabled`, a
  value that does not parse, a certificate without the key store, and a certificate that cannot be loaded or holds no
  private key.
- **Migrations.** The key table belongs to an EF module the host composes itself, as it does cluster membership: under
  `AutoMigrate` the host creates it as it starts, and under `Validate` it refuses to start until
  `dotnet elsa persistence apply --modules DataProtection.Keys` has.
- **The first upgrade signs everyone out once.** Hosts upgraded from a build that named the application after its
  content root issued their cookies and antiforgery tokens under that name, so those are refused after the upgrade:
  every user signs in again, and a form open across the upgrade is posted again. Changing `ApplicationName` later has
  the same effect.

`src/essentials/Foundation/DataProtection/EntityFrameworkCore/EXTENSION_POINTS.md` is the reference; Docker deployments
are covered in [Docker: Data Protection keys](../docker.md#data-protection-keys).

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
10. **Apply the OpenIddict token-store migrations.** Workbench migrates its host-owned vendor EF schema at
    startup while `AutoMigrate=true`. For multi-instance deployments, set `AutoMigrate=false` and apply the
    migrations once as a deploy step against Workbench's `OpenIddictIdentityDbContext` before starting nodes:

    ```bash
    dotnet ef database update \
      --context OpenIddictIdentityDbContext \
      --project src/apps/Elsa.Workbench
    ```

    The Elsa IAM schema is owned by `IdentityIamEntityFrameworkCore` and migrates separately from the
    OpenIddict vendor context.
11. **Share and encrypt the Data Protection key ring.** Set `Elsa:DataProtection:EntityFrameworkCore:Enabled=true` and
    give every host the same `Elsa:DataProtection:Certificate`, so a recreated or second host keeps every session (see
    [Data Protection key ring](#data-protection-key-ring)).

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
