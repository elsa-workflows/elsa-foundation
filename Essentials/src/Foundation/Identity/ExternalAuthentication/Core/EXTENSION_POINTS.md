# External authentication contracts

Optional managed-connection contracts within Foundation Identity. This Core depends on Identity.Core and
Secrets.Core; base Identity.Core has no dependency on managed connections. Canonical identity/authentication
terms: [authentication architecture](../../../../../../docs/reference/authentication-architecture.md).

| Contract | Kind | Consumer and invariant |
|---|---|---|
| `IExternalAdmissionService` | Replacement | Assesses without writes; normal admission rechecks the effective connection and delegates to atomic IAM admission. Inputs come only from a validating server protocol adapter. |
| `IExternalProvisioningPolicy` | Named contribution | Integrator supplies a read-only policy under an explicit unique name. It selects existing users or declared Elsa default roles; it cannot import provider permissions or expand the approved role boundary. |
| `IConnectionRegistry` | Replacement | Backend authentication/admin reads a live effective view with provenance. Deployment is baseline; same-key data overrides only with explicit intent and deployment permission. |
| `IConnectionCatalogStore` | Replacement | Complete bounded catalog read/CAS. A null revision is create-only. Never split the final-path mutation into independent per-record writes. |
| `IConnectionWriter` | Replacement | Management authorizes before global access, validates policy/lifecycle and performs one revision-checked catalog save. |
| `IConnectionWriteAuthorizer` | Replacement | Host-tenant provider-management permission and existing-Elsa-role delegation. Destructive retirement needs management; activation/policy changes also need delegation. |
| `IConnectionReadiness` | Replacement | Installed protocol, required settings, admission/role references and required secret resolution. This describes configuration readiness, not observed health or eligible recovery identities. |
| `IConnectionRecovery` | Replacement | An eligible external recovery identity/path, separate from valid provider configuration. A deny-by-default connection with no eligible link does not become recovery merely by being enabled. |
| `ILocalLoginRecovery` | Replacement | Actual usable local recovery, not an enabled feature flag. |

Register replacements before `AddFoundationExternalAuthentication`; duplicate competing registrations fail
through the shared replacement-contract diagnostics. Protocol and IAM/session adapters supply readiness and
recovery implementations. Missing implementations do not count as ready/recovery. A removal/replacement of an
enabled effective path requires a proved remaining path or local recovery; configuration validity alone is insufficient.

`ConnectionRecord` is a full source document. `Id` identifies a management record; immutable `Key` identifies
long-lived provider links. Source/shadowing comes from the registry, not a writable source field. Secrets are
`SecretReference` objects. Admission/default/mapped role IDs reference Elsa roles; upstream role names are not
Elsa definitions. Mapped membership persistence is still awaiting the owner's design choice in spec 1205.

`ExternalAdmissionContext` is server-only input. Client preview/login requests carry a flow handle; the validating
protocol adapter supplies issuer, subject and claims after verification. An assessment is prospective: it creates
no user, link, cookie, token or session. Custom provisioning policies must honor this read-only contract.
The IAM authority's atomic admission operation receives the existing-user and tenant-membership snapshots so
changes during matching cause a conflict instead of attaching a more privileged identity.
