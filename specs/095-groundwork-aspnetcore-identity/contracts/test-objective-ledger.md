# Contract: Identity Test-Objective Ledger

## Rule

The EF project remains a source-only frozen temporary oracle. Frozen means no new EF behavior, schema,
migration, package, dependency edge, or test objective. The 2026-08-17 project-owner direction is an
explicit clean break: every still-valid behavior gains a public-v2 replacement, while v1 storage-shape,
unconditional-upsert, migration, fallback, alias, and compatibility objectives are retired. The exact
25-objective replacement denominator is enforced by `AspNetCoreIdentityV2AcceptanceCatalog`; provider
execution is owned by `AspNetCoreIdentityV2ProviderMatrixTests`.

## Current Objectives

The exact denominator is 44 source test identities (66 currently enumerated xUnit cases: 40 facts plus 26 member/inline-data rows). An identity below means the fully qualified `<namespace>.<class>.<method>`; the namespace is stated for each surface. Helper methods and fixture lifecycle methods are not tests and are not counted.

### EF ASP.NET Core Identity Sign-In

Namespace: `Elsa.Foundation.Identity.Tests.AspNetCoreIdentity`.

| Exact current identity | Objective | #644 disposition / replacement gate |
|---|---|---|
| `AspNetCoreIdentitySignInTests.PasswordSignIn_Succeeds_With_Valid_Credentials` | Valid password creates an authenticated default-tenant session. | Preserve unchanged; reproduce through T022/T035 and the T070 Groundwork highest seam. |
| `AspNetCoreIdentitySignInTests.PasswordSignIn_Fails_With_Bad_Password` | Wrong password returns generic failure with no session. | Preserve unchanged; reproduce through T022/T070. |
| `AspNetCoreIdentitySignInTests.PasswordSignIn_Fails_For_Unknown_User` | Unknown user is observably indistinguishable from bad credentials. | Preserve unchanged; reproduce through T022/T070, including tenant-bound lookup. |
| `AspNetCoreIdentitySignInTests.Seeder_Creates_Admin_That_Can_Sign_In_With_Admin_Permissions` | Seeded administrator can sign in and receives catalog permissions. | Preserve unchanged; reproduce through T069–T074 and T079. |

### EF Registration, Seeder, Provider, And Claims Projection

Namespace: `Elsa.Foundation.Identity.Tests.AspNetCoreIdentity`.

| Exact current identity | Objective | #644 disposition / replacement gate |
|---|---|---|
| `AspNetCoreIdentityRegistrationTests.EntityFrameworkCoreFeature_Registers_Full_SignIn_Stack` | Feature resolves framework managers, claims factory, sign-in service, Elsa user adapter, and provider metadata. | Preserve unchanged; Groundwork capability/lifetime/registration replacement is T006, T014, T033, and T034. |
| `AspNetCoreIdentityRegistrationTests.Dev_Seeder_Runs_Under_Both_Lifecycle_Hooks` | The same development seeder participates in host and CShells lifecycle hooks. | Preserve unchanged; reproduce through T069 and T074. |
| `AspNetCoreIdentityRegistrationTests.Non_Dev_Does_Not_Register_The_Seeder` | Production without initial-admin configuration does not seed implicitly. | Preserve unchanged; reproduce through T069/T074. |
| `AspNetCoreIdentityRegistrationTests.Configured_Initial_Admin_Registers_Seeder_When_Not_Dev` | Explicit production initial-admin configuration enables both lifecycle hooks. | Preserve unchanged; reproduce through T069/T074. |
| `AspNetCoreIdentityRegistrationTests.Configured_Admin_Is_Seeded_And_Idempotent` | Repeated seed invocation produces one configured user/role and wildcard access. | Preserve unchanged as the EF oracle; replace its `UserManager.Users` assertion with bounded Groundwork lookup in T069/T073/T079. |
| `AspNetCoreIdentityRegistrationTests.LocalProvider_Is_Surfaced_With_LoginPage_Challenge` | Password provider metadata exposes the local login challenge URL and cookie scheme. | Preserve unchanged; reproduce in T006/T022/T035. |
| `AspNetCoreIdentityRegistrationTests.ClaimsPrincipalFactory_Projects_Tenant_Roles_And_Permissions` | Principal contains tenant, subject, roles, direct permissions, and role permissions. | Preserve unchanged; reproduce in T021/T022/T035/T070. |

### EF Elsa IAM Store Oracle

Namespace: `Elsa.Foundation.Identity.Tests.AspNetCoreIdentity`.

| Exact current identity | Objective | #644 disposition / replacement gate |
|---|---|---|
| `EfCoreIdentityStoreTests.User_RoundTrips_By_Id_And_Email_With_Roles_And_Permissions` | User fields, role IDs, and direct permissions round-trip by ID and case-insensitive email. | Preserve unchanged; add framework and Elsa-adapter Groundwork contracts in T018/T021/T031/T036. Pending #647 deletion. |
| `EfCoreIdentityStoreTests.User_Save_Is_An_Upsert` | A second save with the same ID overwrites unconditionally. | Retired by the 2026-08-17 clean-break decision. Replacement is create-only plus expected-version update and stale conflict in `AspNetCoreIdentityConcurrencyContractTests` and the native v2 provider matrix. |
| `EfCoreIdentityStoreTests.Role_RoundTrips_By_Id_And_Lists_By_Tenant` | Role fields/permissions round-trip, list is tenant-local, and wrong-tenant lookup returns null. | Preserve unchanged; replace with T019/T021/T025/T031/T036. Pending #647 deletion. |
| `EfCoreIdentityStoreTests.ExternalIdentity_RoundTrips_By_Subject_And_Lists_For_User` | External identity fields round-trip by subject and list for owner. | Preserve unchanged; replace with T020/T021/T027/T032/T036. Pending #647 deletion. |
| `EfCoreIdentityStoreTests.TenantMembership_RoundTrips` | Membership status, role IDs, and direct permissions round-trip. | Preserve unchanged; replace with revision-aware T021/T032/T044. Pending #647 deletion. |

### Legacy Groundwork Elsa IAM Store Tests

Namespace: `Elsa.Foundation.Identity.Persistence.Groundwork.Tests`.

| Exact current identity | Objective | #644 disposition / replacement gate |
|---|---|---|
| `IdentityGroundworkStoreTests.User_RoundTrips_By_Id_And_Email` | Legacy user round-trips by ID and case-insensitive email. | Preserve the objective in T021/T031 and real-provider T058–T063 before any fixture removal. |
| `IdentityGroundworkStoreTests.User_Survives_A_Store_Restart` | User, email, and role IDs survive a fresh store instance. | Preserve in SQLite reopen/restart T037/T058/T062. |
| `IdentityGroundworkStoreTests.Role_Lists_By_Tenant_And_Survives_Restart` | Tenant-local role list survives a fresh store instance. | Preserve in T019/T021/T031 and T058–T063. |
| `IdentityGroundworkStoreTests.ExternalIdentity_RoundTrips_By_Subject_And_Lists_For_User` | External identity round-trips by subject and owner list. | Preserve in T020/T021/T027/T032 and T058–T063. |
| `IdentityGroundworkStoreTests.TenantMembership_RoundTrips_And_Survives_Restart` | Membership fields survive a fresh store instance. | Preserve in T021/T032 and T058–T063. |
| `IdentityGroundworkStoreTests.Role_Save_replaces_an_existing_record_without_a_revision_contract` | The domain-level non-revisioned `IRoleStore.SaveAsync` replaces the current role row. | Retain as an explicit v2 domain API contract, not as v1 compatibility. Revision-aware and ASP.NET Identity mutation paths separately prove create-only/CAS conflicts. |
| `IdentityGroundworkStoreTests.Explicit_tenant_mismatch_fails_before_provider_io` | Explicit tenant mismatch fails without provider I/O or identifier disclosure. | Preserve and broaden to every operation family in T039/T045/T053. |

### Legacy Groundwork Durable-Shape Fixtures

Namespace: `Elsa.Foundation.Identity.Persistence.Groundwork.Tests`. Each theory currently has four cases: user, role, external identity, and tenant membership.

| Exact current identity | Objective | #644 disposition / replacement gate |
|---|---|---|
| `IdentityGroundworkDocumentFixtureTests.Fixture_Matches_What_The_Store_Writes_Today` | Committed v1 JSON matches the legacy serializer shape for all four document kinds. | The legacy shapes are replaced under the ratified greenfield/no-data-migration boundary. T008–T010/T016 must provide deterministic manifest/serialization coverage for every new authority unit before an exact deletion row is approved. |
| `IdentityGroundworkDocumentFixtureTests.Committed_Fixture_Loads_Through_The_Store_Under_The_Legacy_Stamp` | Legacy v1 fixtures load through the current read path. | Do not claim historical compatibility for the replacement. Keep visible until the greenfield decision and new manifest evidence are cited in an exact deletion row. |

### Persistence-Independent Login Page

Namespace: `Elsa.Foundation.Identity.Tests.AspNetCoreIdentity`.

| Exact current identity | Objective | #644 disposition / replacement gate |
|---|---|---|
| `LoginPageTests.Render_Produces_SelfContained_Html_Form_Posting_To_Login` | Self-contained form posts username/password to the local login route. | Leave unchanged; persistence-independent. |
| `LoginPageTests.Render_Shows_Error_Banner_When_Requested` | Generic credential error visibility follows request state. | Leave unchanged; persistence-independent. |
| `LoginPageTests.Render_Embeds_The_Antiforgery_Token_As_A_Hidden_Field` | Supplied antiforgery token is emitted under the configured field name. | Leave unchanged; persistence-independent. |
| `LoginPageTests.Render_Omits_The_Antiforgery_Field_When_No_Token_Is_Supplied` | No empty/false antiforgery field is fabricated. | Leave unchanged; persistence-independent. |
| `LoginPageTests.LocalUrl_Accepts_Only_Local_Paths` | Ten inline cases reject protocol-relative, absolute, script, empty, and null redirects while accepting local paths. | Leave all cases unchanged; persistence-independent. |
| `LoginPageTests.LocalUrl_Sanitize_Falls_Back_To_Root_For_NonLocal` | Untrusted return URLs sanitize to root. | Leave unchanged; persistence-independent. |
| `LoginPageTests.Sanitize_Honours_Absolute_Url_On_A_Trusted_Origin` | Explicit trusted origin preserves path/query while local paths remain valid. | Leave unchanged; persistence-independent. |
| `LoginPageTests.IsTrustedAbsolute_Matches_Only_Allow_Listed_Origins` | Eight inline cases enforce exact trusted scheme/host/port rules. | Leave all cases unchanged; persistence-independent. |

### Enabled-Shell Highest Seam

Namespace: `Elsa.Foundation.Identity.Tests.Api`.

| Exact current identity | Objective | #644 disposition / replacement gate |
|---|---|---|
| `EnabledShellCompositionTests.Anonymous_Request_To_A_Permission_Secured_Endpoint_Is_Rejected_With_401` | Anonymous request cannot access a permission-secured endpoint. | Retain the existing EF/OpenIddict composition; add explicit Groundwork Identity composition in T070/T078. |
| `EnabledShellCompositionTests.Login_Then_Token_Yields_A_Bearer_That_Satisfies_ConfigurePermissions` | Login -> cookie -> token -> bearer satisfies Elsa permission authorization. | Retain the current combined oracle; reproduce the ASP.NET Core Identity half with Groundwork in T070/T078. OpenIddict remains separately owned. |

### Token, Cookie, CSRF, And Logout Seam

Namespace: `Elsa.Foundation.Identity.Tests.Api`.

| Exact current identity | Objective | #644 disposition / replacement gate |
|---|---|---|
| `TokenEndpointTests.Anonymous_Request_Gets_401_So_The_Client_Stays_Anonymous` | Anonymous cookie-to-token exchange returns 401. | Retain; reuse the helper/fixture shape for the Groundwork highest seam where applicable. |
| `TokenEndpointTests.Authenticated_Cookie_Principal_Gets_200_With_A_Bearer_Whose_Claims_RoundTrip` | Authenticated cookie exchanges for a bearer carrying tenant and permission claims. | Retain; reproduce Groundwork Identity cookie/claims input in T070/T078. |
| `TokenEndpointTests.Login_Then_Token_Yields_A_Bearer_That_Authenticates_A_Protected_Endpoint` | Login-to-bearer flow authenticates a protected endpoint. | Retain; reproduce Groundwork Identity half in T070/T078. |
| `TokenEndpointTests.Form_Login_Without_A_Csrf_Token_Is_Rejected_And_Issues_No_Session` | Form login with return URL but no CSRF token is rejected without a cookie. | Retain unchanged; exercise the same endpoint with Groundwork in T070. |
| `TokenEndpointTests.Form_Login_Without_A_Csrf_Token_And_Without_A_ReturnUrl_Is_Rejected` | Form login cannot bypass CSRF by omitting return URL. | Retain unchanged; exercise the same endpoint with Groundwork in T070. |
| `TokenEndpointTests.Logout_Then_Token_Returns_401` | Removing the identity cookie invalidates token exchange. | Retain unchanged; logout/session invalidation is a highest-seam requirement in T070. |
| `TokenEndpointTests.Logout_For_An_Unknown_Provider_Returns_204_And_Does_Not_500` | Unknown/non-signout provider logout is idempotent 204. | Retain unchanged; provider-routing behavior is persistence-independent. |
| `TokenEndpointTests.Logout_On_The_Cookie_Provider_Clears_The_Session` | First-party cookie-provider logout expires the session. | Retain unchanged; reproduce Groundwork Identity session input in T070. |
| `TokenEndpointTests.Refresh_With_A_Garbage_Token_Returns_401_Not_500` | Invalid refresh token maps to 401 rather than 500. | Retain unchanged; OpenIddict/token-service behavior is outside #644. |

## Exact Replacement Coverage Mapping

The authoritative member and operation lists are in [identity-store-contract.md](identity-store-contract.md); this ledger maps every row there to its red-first replacement owner without duplicating the method vocabulary.

| Contract denominator | Red-first replacement owner | Implementation/evidence owner |
|---|---|---|
| `IUserStore`, password, security-stamp, email, lockout, phone, and two-factor rows | T018 `AspNetCoreIdentityUserStoreContractTests` | T024, T036–T037, then tenant/concurrency extensions T039–T054 |
| `IRoleStore` and `IRoleClaimStore` rows | T019 `AspNetCoreIdentityRoleStoreContractTests` | T025, T036–T037, then tenant/concurrency extensions T039–T054 |
| User claim, login, role, authentication-token, authenticator-key, and recovery-code rows | T020 `AspNetCoreIdentityRelationshipContractTests` | T026–T030, T036–T037, then atomicity/concurrency extensions T040–T054 |
| Queryable-user, queryable-role, passkey, and protected-user absence rows | T006 `AspNetCoreIdentityGroundworkRegistrationTests` | T033 and T036 prove the interfaces do not resolve |
| Elsa `IUserStore` and `IRoleStore` operation rows | T021 `AspNetCoreIdentityAuthorityAdapterTests` | T031, with additive revision coverage in T044–T046 |
| Elsa `IExternalIdentityStore` and `ITenantMembershipStore` operation rows | T021 `AspNetCoreIdentityAuthorityAdapterTests` | T032, with additive revision/atomicity coverage in T044 and T048–T049 |
| Inherited `IDisposable.Dispose` and scoped lifetime | T006 registration/lifetime contracts | T014/T033 and T036 |

## New Required Objective Groups

- exact framework capability registration and unsupported-interface absence;
- same-tenant duplicate and cross-tenant equal-normalized identity races;
- wrong-scope non-disclosure for every operation family;
- user/role envelope-version concurrency and public stamp rotation;
- relationship UoW atomicity and no-orphan delete under races/failure windows;
- lockout lost-update resistance;
- ambiguous-email fail-closed behavior and conditional unique-email reservation;
- concurrent-start seeding, wildcard/catalog permission expansion, and secret-safe logs;
- explicit Groundwork-vs-EF feature conflict and one authority manifest;
- four-provider close/reopen/process-restart result equality;
- native bounded route evidence;
- architecture rejection of new EF surface, duplicate authority, load-all, and false capability registration.

## Clean-break approval record

| Exact test identity | Original objective | Why objective is invalid or replacement evidence | Replacement test/evidence | Architect | Decision | Date |
|---|---|---|---|---|---|---|
| `EfCoreIdentityStoreTests.User_Save_Is_An_Upsert` | Unconditional repeated-save overwrite | Conflicts with fail-closed create-only/CAS semantics and the explicit no-migration clean break. | `AspNetCoreIdentityConcurrencyContractTests`; `AspNetCoreIdentityV2ProviderMatrixTests` | Project owner | Retire | 2026-08-17 |
| `IdentityGroundworkStoreTests.Role_Save_replaces_an_existing_record_without_a_revision_contract` | Domain `IRoleStore.SaveAsync` replacement semantics | This is a current v2 domain contract rather than a v1 data or compatibility path; the separate revision-aware surface remains fail-closed. | `IdentityGroundworkStoreTests`; `AspNetCoreIdentityRoleStoreContractTests`; `AspNetCoreIdentityConcurrencyContractTests` | Project owner | Retain | 2026-08-17 |
| `IdentityGroundworkDocumentFixtureTests.Fixture_Matches_What_The_Store_Writes_Today` | Preserve v1 serialized fixture shape | V1 storage shape is explicitly out of scope; deterministic v2 manifest/serialization is the new authority. | `IdentityStorageManifestTests`; public-v2 provider matrices | Project owner | Retire | 2026-08-17 |
| `IdentityGroundworkDocumentFixtureTests.Committed_Fixture_Loads_Through_The_Store_Under_The_Legacy_Stamp` | Load committed v1 fixtures | Historical compatibility and migration are explicitly forbidden by the clean-break direction. | Clean-room Feedz package consumer and public-v2 process restart matrix | Project owner | Retire | 2026-08-17 |
