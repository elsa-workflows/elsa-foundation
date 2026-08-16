# Foundation Identity Wave 3 Minimal API evidence

This report records the nine protocol routes owned by the two Wave 3 assemblies. The runtime
manifest and endpoint metadata are the source of route, owner, operation-name, tag, public-security,
and permission facts; the HTTP matrix is exercised by the identity TestServer suites.

| Owner | Method | Route | Disposition | Protocol owner |
|---|---|---|---|---|
| `Elsa.Foundation.Identity.Api` | GET | `/_elsa/identity/bootstrap` | public | provider-neutral bootstrap |
| `Elsa.Foundation.Identity.Api` | GET | `/_elsa/identity/capabilities` | `identity.providers.read` | Foundation permission evaluator |
| `Elsa.Foundation.Identity.Api` | GET | `/_elsa/identity/challenge/{provider}` | public | configured provider challenge handler |
| `Elsa.Foundation.Identity.Api` | POST | `/_elsa/identity/logout/{provider}` | public | configured provider sign-out handler |
| `Elsa.Foundation.Identity.Api` | POST | `/_elsa/identity/refresh` | public transport; token validated by `ITokenService` | OpenIddict token service |
| `Elsa.Foundation.Identity.Api` | GET | `/_elsa/identity/session` | public | `IAuthSessionService` |
| `Elsa.Foundation.Identity.Api` | GET | `/_elsa/identity/token` | public transport; interactive schemes only | cookie/external provider + `ITokenService` |
| `Elsa.Foundation.Identity.AspNetCoreIdentity` | GET | `/_elsa/identity/login` | public | ASP.NET Core Identity login page |
| `Elsa.Foundation.Identity.AspNetCoreIdentity` | POST | `/_elsa/identity/login` | public | ASP.NET Core Identity sign-in/antiforgery |

The deterministic matrix covers anonymous 401/200 behavior, exact/implied/wildcard permission
grants, a trusted authenticated principal without the requested grant (403), malformed and empty
JSON, unsupported content types, challenge and logout status/redirect behavior, login form and JSON
flows, antiforgery and `Set-Cookie`, local `returnUrl` validation, and first-party bearer rejection
at `/token`. `PermissionEndpointAdapterIntegrationTests` runs the capabilities matrix beside a
remaining FastEndpoints endpoint so both endpoint styles use the same evaluator and normalized
principal contract.

All nine routes have explicit stable operation names (`FoundationIdentity*` or
`AspNetCoreIdentityLogin*`) and the `Identity` tag. Public routes carry the public disposition;
capabilities carries exactly one catalog permission. Wildcard is evaluated only as a caller grant,
never as endpoint metadata.

## Approved differences

There are no approved protocol differences. Any immutable before/after evidence must be captured
from the parent FastEndpoints host and consumed by the real-host comparer; this report intentionally
does not hand-author legacy bytes or waive a mismatch.
