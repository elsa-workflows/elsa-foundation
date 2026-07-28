# Foundation Identity ASP.NET Core Identity Groundwork

`FoundationIdentityAspNetCoreIdentityGroundwork` is the first-party Groundwork implementation of the
ASP.NET Core Identity provider. It keeps the Identity contracts provider-neutral and supplies one concrete
authority over Groundwork documents.

## What it registers

- ASP.NET Core Identity `UserManager`, `RoleManager`, `SignInManager`, token providers, and the Elsa cookie
  sign-in path.
- Groundwork-backed framework stores for `AspNetCoreIdentityUser` and `IdentityRole`.
- Elsa IAM adapters (`IUserStore`, `IRoleStore`, `IExternalIdentityStore`, `ITenantMembershipStore`) over the
  same authoritative Identity documents.
- Optional initial administrator seeding through one `GroundworkIdentitySeeder` instance registered as both
  `IHostedService` and `IShellInitializer`.

Core Identity modules do not depend on Groundwork. Only this concrete provider package does.

## Host and deployment guidance

Use `FoundationIdentityAspNetCoreIdentityGroundwork` only when the host deliberately selects Groundwork as
the ASP.NET Core Identity authority. The canonical host-composition, schema CLI, topology, and unsupported
capability rules live in
[`src/Elsa/Foundation/Identity/Persistence/Groundwork/EXTENSION_POINTS.md`](../../Persistence/Groundwork/EXTENSION_POINTS.md)
and the root [`EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md). Keep those catalogs as the
source of truth when deployment rules change.

## Performance handoff

The `iam-normalized-lookup-update` workload is defined in
`specs/094-harden-groundwork-stores/workloads/iam-secrets.json`. It is correctness-only in this unit; #646
owns timing, physical-form comparison, and pass/redesign/blocked verdicts.
