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
| `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` | Durable EF Core user/role store, `SignInManager` cookie sign-in, the login page/endpoints, and dev seeding. |
| `FoundationIdentityOpenIddict` | OpenIddict-backed JWT access-token issuance + local bearer validation for the API surface. |

The composite scheme selector these register becomes the default authenticate/challenge scheme, so an
unauthenticated API call is rejected with `401`. A host-chosen `DefaultScheme` always wins.

## Development / demo defaults

The checked-in `src/Apps/Elsa.Workbench/shells.json` enables the stack with `IsDevelopmentOrDemo: true`, which is
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

- **Stores are in-memory** (EF in-memory database) — data does not survive a restart.
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
| `IsDevelopmentOrDemo` | In-memory store + ephemeral keys. | **`false`.** |
| `ConnectionString` | Sqlite connection string for the identity database. | Set to a durable path, e.g. `Data Source=/var/lib/elsa/identity.db`. Defaults to `identity.db` in the content root when unset. |
| `SeedAdminUserName` | Username of an administrator to provision at startup. | Optional. Requires `SeedAdminPassword`. |
| `SeedAdminPassword` | Password for the seeded administrator. | **Supply via a secret** (user-secrets / environment variable), never committed. |
| `SeedAdminEmail` | Email for the seeded administrator. | Optional; defaults to `<username>@elsa.local`. |
| `SeedAdminRoleName` | Role granted to the seeded administrator. | Optional; defaults to `administrator`. |

The seed account is defined **entirely by the `SeedAdmin*` settings** on both the dev/demo and production
paths — there are no credential constants in code. The committed `admin` / `Password123!` values apply only
under `IsDevelopmentOrDemo`. In production, either provision users through your own onboarding, or seed a first
administrator by setting `SeedAdminUserName` and supplying `SeedAdminPassword` from a secret store (its password
is never written to the log; the username xor password half-configured is a startup error).

### `FoundationIdentityOpenIddict`

| Setting | Meaning | Production requirement |
|---|---|---|
| `IsDevelopmentOrDemo` | In-memory token store + ephemeral keys. | **`false`.** |
| `Issuer` | Logical issuer URI written into (and required from) first-party access tokens. | Set to a stable absolute URI, e.g. `https://elsa.example.com/`. |
| `SigningKey` | Base64-encoded **PKCS#8 RSA private key** used to sign access tokens (RS256). Falls back to `FoundationIdentityOptions.SigningKey`. | **Required.** See generation command below. |
| `EncryptionKey` | Key material for OpenIddict's encryption credentials. Defaults to a key derived (domain-separated) from `SigningKey`. | Recommended: set a **distinct** value from `SigningKey`. |
| `ConnectionString` | Sqlite connection string for the token store. Defaults to the shared identity database. | Optional; set for a dedicated token DB. |

Generate a signing key:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -outform DER | base64
```

(The same command is documented in the remarks of `ConfigureOpenIddictServerOptions`.) Outside
`IsDevelopmentOrDemo`, startup **fails fast** with a clear error if no signing/encryption key is configured.

### `FoundationIdentityOptions` (shared, bound from the `Elsa:Identity` section if you surface it)

| Setting | Default | Notes |
|---|---|---|
| `SigningKey` | — | Fallback signing key material for the OpenIddict server. |
| `RequireHttpsMetadata` | `true` | Keep `true` in production; the security-default guards reject HTTP metadata otherwise. |
| `IsDevelopmentOrDemo` | `false` | Global dev switch surfaced to the security-default guards. |

### Cookie / session hardening

The sign-in cookie is `HttpOnly`, `SameSite=Lax`, sliding-expiration, and — outside `IsDevelopmentOrDemo` —
`SecurePolicy=Always` (HTTPS-only). Serve the server over HTTPS in production or the session cookie will be
dropped by the browser.

### CSRF

The backend-served login page (`GET /_elsa/identity/login`) embeds an antiforgery token and sets the paired
cookie; the login `POST` validates it for the HTML-form flow. JSON API callers are unaffected. No configuration
is required.

### The API kill-switch

`ApiSecurity.AllowAnonymous` disables endpoint security for a shell. **It is honored only in the `Development`
environment** (locked product decision — there is no auth off-switch in production). Outside `Development` the
flag is ignored, the shell stays secure, and a prominent warning is logged. Do not rely on it for anything but
local development or tests.

## Same-origin hosting

The Studio client requests `{shell-origin}/_elsa/identity/token` with `credentials: include`, so host the SPA
same-origin as the server for the session cookie to flow. Cross-origin setups require CORS plus
`SameSite=None; Secure` cookies — avoid unless necessary.

## Production go-live checklist

1. `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore.IsDevelopmentOrDemo = false` and set
   `ConnectionString` to a durable database.
2. `FoundationIdentityOpenIddict.IsDevelopmentOrDemo = false`.
3. `FoundationIdentityOpenIddict.SigningKey` = base64 PKCS#8 RSA private key (generated with the command above),
   sourced from a secret store.
4. `FoundationIdentityOpenIddict.EncryptionKey` = a distinct base64/secret value (recommended).
5. `FoundationIdentityOpenIddict.Issuer` = your stable absolute issuer URI.
6. `FoundationIdentityOptions.RequireHttpsMetadata = true` (default) and serve the server over **HTTPS** (so the
   `SecurePolicy=Always` session cookie is accepted).
7. Provision real user accounts — either through your own onboarding, or by setting `SeedAdminUserName` with a
   secret `SeedAdminPassword` (the committed dev `admin`/`Password123!` values apply only under `IsDevelopmentOrDemo`).
8. Ensure `ApiSecurity.AllowAnonymous` is **not** set on any shell (it is ignored outside `Development`, but
   remove it to avoid the startup warning).
9. Host the Studio SPA same-origin, and set `Studio:Auth:Enabled=true`.
10. **Apply the OpenIddict token-store migrations.** With `IsDevelopmentOrDemo = false`, the auto-initializer
    that creates/migrates the token schema **does not run** — it is a dev-only convenience. A production host
    with `FoundationIdentityOpenIddict` enabled but its migrations unapplied faults on the first token
    issuance (`IssueAsync`, i.e. the first `GET /_elsa/identity/token` after login). Apply them explicitly as
    a deploy step, against the `OpenIddictIdentityDbContext`:

    ```bash
    dotnet ef database update \
      --context OpenIddictIdentityDbContext \
      --project src/Elsa/Foundation/Identity/OpenIddict
    ```

    (The ASP.NET Core Identity feature's own `ApplicationIdentityDbContext` likewise relies on relational
    migrations outside dev/demo; run `dotnet ef database update` for it too if you have not already.)

If a required signing/encryption key is missing outside `IsDevelopmentOrDemo`, the host throws at startup with a
message naming the setting to configure — a missing key never silently degrades to an insecure default.

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
key configured every request that touches token issuance or validation fails with the
"No signing key is configured for the OpenIddict identity module" error. This is why the same image behaves
differently under `ASPNETCORE_ENVIRONMENT=Development` (no `shells.Development.json` exists, so the
`shells.json` value survives — and the Development environment also satisfies the startup guard above). For a
non-Development demo host, don't chase the flag: configure a real `SigningKey` per the go-live checklist. For
a throwaway local demo, run the container with `ASPNETCORE_ENVIRONMENT=Development`.
