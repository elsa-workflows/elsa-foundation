# Managed external authentication

`FoundationIdentityExternalAuthentication` is an optional Identity feature for host-wide provider configuration.
It shares the existing Foundation Identity permission/token authority. Its contracts live in sibling `Core`;
managed OIDC belongs to the optional ExternalAuthentication/Oidc adapter; existing Identity.Oidc retains direct bearer support. The dependency envelope includes Secrets.Core
and persistence scope contracts without adding them to every base Identity.Core consumer.

Current implementation: live deployment/catalog selection, explicit complete overrides, catalog persistence/CAS,
role-delegation checks, lifecycle commands, the final-path guard and policy assessment/atomic admission. Protocol/login/preview/session/API/Studio
integration is still in progress under [spec 1205](../../../../../../specs/1205-external-authentication/spec.md).
This module alone is not a qualified external sign-in deployment.

## Configuration and composition

Enable the normal `FoundationIdentityAbstractions` feature and the session/protocol adapters required by the
host. `AddFoundationExternalAuthentication` is the programmatic registration entry point.

- `TenantId` is the deployment's identity/administration tenant (`default` initially). Connections have no editable tenant field.
- `Connections` is the read-only deployment baseline. A record's deployment ID derives from its logical key.
- `AllowDatabaseManagement` defaults to false; disabled management never reads optional persisted catalog rows.
- `AllowDeploymentOverrides` defaults to false; `AllowedOverrideKeys` may constrain the permitted keys.
- `AllowedRoleIds` may further constrain default/mapped Elsa roles for database policy editing. The editor must also be authorized to delegate every role permission.

The default catalog adapter uses the existing `IRevisionAwareProviderConfigurationStore`, one global
`external-authentication` record of kind `external-connections/v1`. Compose a provider-configuration backend
when database management is enabled. Reads/writes use their own explicit global scopes; writes are privileged
for this fixed catalog purpose after writer authorization. Caller tenant scope is not rebound. No EF package
or new provider schema is required here. Catalog bounds: 256 records and a one-megacharacter serialized payload;
settings/references/policy collections are bounded separately.

Baseline and same-key ordinary database drafts coexist without shadowing. Explicit override promotion selects
one full database document; no settings, secrets, policy or presentation fields merge. Disabled overrides still
shadow. Archive/removal reveals the baseline; restore reinstates the saved full override. Logical keys cannot
be edited. Source settings with duplicate keys fail closed. Create is always disabled; activation and lifecycle
commands are explicit. Test observations belong outside authentication material and do not alter material revisions.

Configuration readiness and recovery eligibility are separate Core contracts. A structurally valid provider
with no eligible identity is not a recovery path. Before disabling/replacing an enabled effective provider, the
writer proves that a remaining external recovery path or actual local recovery exists, then saves the complete
catalog with CAS. Concurrent workers cannot both apply stale decisions against the same catalog revision.

## Extension points and tests

See [Core catalog](../Core/EXTENSION_POINTS.md). Seven replacement defaults are scoped. The immutable system clock and startup-only activation check are
singletons. `ExternalAuthenticationActivation` implements both `IShellInitializer` and `IHostedService` so plain
and shell hosts reject malformed deployment policy or missing opt-in persistence before serving requests.
It performs no table queries ahead of the persistence feature's migrations. No event handlers or recurring tasks are added in this stage.
Cross-domain consumption: Secrets.Core reference metadata and Runtime.Core's explicit persistence access scopes;
there are no anonymous dispatch or cross-domain event contributions.

Behavior tests: `Essentials/tests/Foundation/Identity/Tests/ExternalConnectionTests.cs` and
`ExternalConnectionPersistenceTests.cs` cover source lifecycle, stale/concurrent changes, invalid/unlinked recovery,
permission/delegation boundaries, redacted credential rejection, detached views, durable catalog restart and
shared-store CAS. These tests do not replace actual provider/Studio/multi-node qualification.

## Admission policies

Canonical role/identity behavior: [authentication architecture](../../../../../../docs/reference/authentication-architecture.md).
The default `Admission.Type = "reject"` refuses unlinked identities. Existing qualified links resolve only active
Elsa users and active tenant memberships. `"jit"` explicitly provisions a new external-only user with the configured
`DefaultRoleIds`; its user name is a deterministic opaque key, so equal email addresses at different providers do
not cause an implicit match. The existing IAM mutation creates the user and link together, with no local password.

`Type = "match"` requires `Matcher = "verified-email"`. The provider must supply exactly one email and one boolean
`email_verified = true`; only a unique active Foundation-managed address is eligible. An ambiguous address,
unverified evidence or an earlier imported external profile refuses rather than falling back to JIT. The existing
user's roles are preserved. Their effective role/direct-permission boundary must fit the connection's approved
`DefaultRoleIds`; a provider editor cannot use matching to attach an identity to a more privileged account.
Ownership/capability settings prevent local provisioning/link creation when user authority is external, while existing
links can still resolve through the selected IAM authority.

An integrator can register `IExternalProvisioningPolicy` with `services.AddScoped<IExternalProvisioningPolicy,
CompanyPolicy>()`, and select its unique `Name` in `Admission.Type`. `AssessAsync` returns
`ExternalProvisioningDecision` and must perform no writes or external side effects. For new users, it may select a
subset of the connection's declared `DefaultRoleIds`; host `AllowedRoleIds` also applies. This API assigns saved
Elsa roles at initial provisioning. It does not settle the separate pending choice about recurring group mappings.
For example, a policy can choose the declared `operator` role for a trusted department claim and `reader` otherwise:

```csharp
public sealed class CompanyPolicy : IExternalProvisioningPolicy
{
    public string Name => "company-onboarding";

    public ValueTask<ExternalProvisioningDecision> AssessAsync(
        ExternalAdmissionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ExternalProvisioningDecision
        {
            Kind = ExternalAdmissionKind.NewUser,
            RoleIds = context.Claims.Any(claim => claim.Type == "department" && claim.Value == "Operations")
                ? ["operator"] : ["reader"]
        });
}
```

Both role IDs must already exist in Elsa and be declared for that connection. Raw upstream role and permission
claims never flow into the canonical principal. Policies return a closed failure category; no arbitrary provider
message is exposed. Preview calls only `IExternalAdmissionService.AssessAsync`; normal admission calls `AdmitAsync`,
which reloads current source/lifecycle/material state and reassesses before the atomic write. A disabled draft can
be assessed but cannot establish a normal login. The broker and interactive preview endpoints remain pending.
