# External authentication connection catalog

This optional module supplies host-wide deployment connection configuration and a live effective catalog.
The catalog uses the selected provider-configuration authority only when database management is enabled.
Deployment-only reads never resolve or query the optional provider database.

Register the catalog with `AddFoundationExternalAuthentication`, or use the
`FoundationIdentityExternalAuthentication` shell feature. The feature is not enabled by adding the package.
No backend chooser, callback route or protocol adapter is registered by this source slice.

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

The default registration installs `IConnectionCatalogStore`, `IConnectionRegistry`, admission assessment,
explicit provisioning policies, options, time and activation validation. Database management requires a selected revision-aware provider-configuration store
or an explicit custom catalog. Activation validates registration and deployment policy without querying
schema tables before the persistence feature's migration phase.

The Core package also defines contracts for guarded mutations, protocol flows and durable artifacts.
Their default implementations and entry points are staged separately. This catalog slice does not install
those services or advertise a usable sign-in/recovery path. Configuration selection is not runtime readiness,
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
