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

The default registration installs `IConnectionCatalogStore`, `IConnectionRegistry`, options, time and
activation validation. Database management requires a selected revision-aware provider-configuration store
or an explicit custom catalog. Activation validates registration and deployment policy without querying
schema tables before the persistence feature's migration phase.

The Core package also defines contracts for guarded mutations, admission, protocol flows and durable artifacts.
Their default implementations and entry points are staged separately. This catalog slice does not install
those services or advertise a usable sign-in/recovery path. Configuration selection is not runtime readiness,
provider sign-in proof or complete external-authentication delivery.
