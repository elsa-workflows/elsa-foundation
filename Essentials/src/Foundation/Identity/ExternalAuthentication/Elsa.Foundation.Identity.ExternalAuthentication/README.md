# External authentication connections and flows

This optional module supplies host-wide deployment connection configuration, a live effective catalog and
guarded connection lifecycle operations, protected one-time sign-in flow coordination and administrator preview.
The catalog uses the selected provider-configuration authority only when database management is enabled.
Deployment-only reads never resolve or query the optional provider database.

Register the catalog with `AddFoundationExternalAuthentication`, or use the
`FoundationIdentityExternalAuthentication` shell feature. The feature is not enabled by adding the package.
The optional [protocol API](../Api/README.md) connects these services to the backend chooser and canonical
Identity sessions; the protocol adapter and HTTP API remain separate opt-in packages.

## Connection ownership

Deployment is the baseline. A same-key database document replaces it only when it declares an explicit
full override and deployment policy permits that key. There is no field inheritance. A disabled effective
override still shadows the baseline; archiving or removing it reveals the baseline; restoring it reinstates
that document. `Key` identifies long-lived provider links, while `Id` identifies a source document.
The registry reports provenance, shadowing and authentication-material revision separately from presentation.

Use secret references for credentials. The catalog rejects inline credential fields and credential-bearing
issuer URLs, invalid logical keys and duplicate records. Admission role IDs refer to existing Elsa roles;
unknown users reject by default. See the [core extension contracts](../Core/EXTENSION_POINTS.md).

## Registration at this stage

The default registration installs the catalog/registry, admission policies, guarded connection writer,
delegation/readiness/recovery authorities, protected observations, flow coordination, options, time and activation validation. Database management requires a selected revision-aware provider-configuration store
or an explicit custom catalog. Activation validates registration and deployment policy without querying
schema tables before the persistence feature's migration phase.

Managed protocol, durable artifact/session and HTTP API implementations are optional packages; canonical
session enforcement is supplied by the selected Identity adapter. Missing installed protocols, canonical sessions, durable artifacts
or callback support never count as a usable sign-in/recovery path. Configuration selection is not runtime readiness,
provider sign-in proof or complete external-authentication delivery.

## Provisioning policy and saved roles

`IExternalAdmissionService` assesses verified protocol input without writing. Normal admission rechecks the live
connection and material revision, resolves an exact qualified link, then uses IAM's atomic admission operation
for a new link or user. Unknown identities reject by default. Explicit `jit` policy may create a user;
explicit `match` with `verified-email` may attach a uniquely matched eligible Foundation-owned account.
A preview assessment does not create users, links, cookies, tokens or sessions.

Integrators can contribute an `IExternalProvisioningPolicy`. Its selected role IDs must be existing Elsa roles
within both the connection's approved boundary and deployment policy. Existing linked users retain their saved
roles and permissions; later sign-ins do not call a provisioning policy to replace manual memberships.
Provider groups, role names and permission claims do not grant Elsa authority automatically.

## Guarded lifecycle and recovery

`IConnectionWriter` authorizes before reading global catalog state. Creation starts with a disabled draft;
promotion to an override and lifecycle transitions are explicit. Policy changes may delegate only existing
Elsa roles whose permissions the editor can grant. Writes use the catalog revision and live IAM guards;
unauthorized, stale or unsupported coordination does not save a partial mutation.

Retiring or replacing an effective path requires a proved remaining path or eligible local recovery.
Configuration readiness is separate from successful normal administrator sign-in evidence. Protected evidence
is bounded and tied to current connection material, exact qualified link, credentials, callback and canonical
session/account binding. Preview does not establish recovery evidence. Later callback/session/protocol services
must be installed before the default runtime can establish readiness or recovery; no success placeholder is used.

## Optional managed OIDC adapter

The [managed OIDC package](../Oidc/README.md) contributes the `oidc` protocol through
`AddFoundationExternalOidc` or the `FoundationIdentityExternalOidc` feature. It supplies discovery and
code/PKCE/nonce/issuer validation with secret references resolved when used. Installing the adapter does not
install callback/chooser/API routes, enable a connection or establish recovery evidence. The existing direct
bearer OIDC feature remains a separate composition.

## Optional artifact and session persistence

The [EF persistence package](../Persistence/EntityFrameworkCore/README.md) supplies optional durable
`IAuthenticationArtifactStore` and `IAuthenticationSessionStore` authorities. Installing the stores does not
switch canonical sign-in, cookies or tokens to durable sessions or install callback routes; those integrations
follow in their owning slices. Deployment-only catalog composition still avoids the optional database.

## Protected flow coordination

`IExternalAuthenticationFlowService` starts a challenge against the selected live connection. Its server-owned
state is protected and durably bound to tenant, connection, purpose, browser and, for preview, the initiating
canonical administrator. It retains the selected record/material, callback, nonce, PKCE verifier, issuer and
credential binding. A bound callback consumes the state once before redemption and rechecks the live selection
and return-destination policy. Stale, expired, replayed, wrong-browser or wrong-administrator input supplies no
new user authority.

Normal completion invokes the admission policy and returns the admitted Elsa user ID and verified provenance.
It does not itself issue a cookie or token. Preview invokes read-only assessment and stores a bounded protected
result for the same administrator and browser; consuming the result rechecks authorization and reports changed
connection material as stale. It creates no user, link or normal session and establishes no recovery evidence.
The optional API invokes canonical sign-in after admission; this flow service itself issues no cookie or token.
Production requires a durable artifact authority; a non-durable replacement is accepted only in Development.
The controlled OIDC fixture source exercises these boundaries after execution is authorized; it is not evidence
of a real identity-provider deployment, current cookie/session enforcement or Studio acceptance.

## Optional connection-management API

The optional [protocol and connection API](../Api/README.md) exposes source-aware reads, bounded saved-role/user
lookups, read-only assessment, protocol tests and guarded lifecycle commands. Its capability declaration is
installed with those routes. Current permissions, antiforgery, source/catalog revisions, role delegation and
coordinated final-path recovery remain server-owned checks. This does not install a Studio editor or qualify
provider, browser, restart or multi-node behavior.

The same optional API supplies a bounded user directory and separately authorized link/session panels. Exact
qualified link mutations retain owner/delegation/recovery and revision guards; selected-session revocation uses
its durable store revision without changing another session. The Identity permission catalog contributes the
separate panel grants. These source capabilities still require composed provider, store, HTTP, Studio and
package qualification before delivery.
