# Quickstart: Groundwork ASP.NET Core Identity Validation

This guide is the executable evidence path for #644. The current candidate targets Groundwork `0.0.1-preview.60`, Docker is available for non-SQLite providers, and the repository-local tool manifest is restored at the same version as the packages.

> **Evidence status:** Preview.60 provider evidence is accepted for committed candidate `1aed4f5989b9aed0ddb9837a61597d4cb584fbaa` (tree `00f5f518c79429dcd1e175ca71e38e719004dc65`) under immutable generation `4c541bf48f087c5073dd4f39a88bdce542651e2e6453d9e3d060c951e93a1f9f`. The dated preview.55-preview.59 material below remains historical provenance only. The checked-in EF contract baseline is non-executed; live EF/Groundwork equality and timing remain owned by #646.

## 1. Establish The Exact Baseline

```bash
git rev-parse HEAD
git diff --check
dotnet tool restore
dotnet restore Elsa.Server.slnx
```

Record in the PR/evidence summary:

- exact baseline and candidate commits;
- .NET SDK/OS/architecture;
- Groundwork package and tool version;
- SQL Server/PostgreSQL/MongoDB image digests;
- MongoDB replica-set name/topology;
- focused and full test counts.

The implementation baseline is `e4d61bef889675902dc177865efb9e6166d71611`. Do not advance it silently; a rebase requires repeating red baselines and independent review.

### Historical Setup Evidence

- Base commit: `e4d61bef889675902dc177865efb9e6166d71611`
- Planning baseline commit: `bbf435153c97525d84b0665cb6e1e50e0bc149fd`
- Setup candidate commit: the branch `HEAD` commit that contains this section and marks T001-T005 complete; verify with `git rev-parse HEAD`
- .NET SDK: `10.0.300`
- OS/runtime: macOS `26.5.2` build `25F84`, Darwin `25.5.0`, `arm64`
- Historical Groundwork packages/tool: `0.0.1-preview.56` (`dotnet groundwork --version` -> `Groundwork.Tool 0.0.1-preview.56`)
- Docker provider images:
  - SQL Server: `mcr.microsoft.com/mssql/server@sha256:d38c2a64812d775f844088e4e44ab33846eefc61157431bf9c8a3943a534c22e`
  - PostgreSQL: `postgres@sha256:ef257d85f76e48da1c64832459b59fcaba1a4dac97bf5d7450c77753542eee94`
  - MongoDB: `mongo@sha256:4c65244b50910461b9641a76131f84a2dcfd4da487f928298cea626b3842842c`
- MongoDB topology: single-node replica-set testcontainer, replica set `rs0`, writable primary, transaction-capable clients
- Setup focused validation:
  - Existing ASP.NET Core Identity suite: `119` passed, `0` failed
  - Existing Foundation Identity Groundwork suite: `15` passed, `0` failed
  - Architecture guard after solution-folder correction: `198` passed, `0` failed
  - Structured Logs SQLite timeout rerun: `1` passed, `0` failed
  - Storage scope provider theory rerun: `4` passed, `0` failed
- Full-solution setup validation: `4,360` passed, `1` failed, `0` skipped across `63` TRX files. The single failed test was `Elsa.Diagnostics.StructuredLogs.Persistence.Tests.SqliteStructuredLogsPersistenceFeatureTests.PersistentStoreResolvesWhenStructuredLogCaptureIsEnabled`, a timeout in full-solution execution that passed in isolation above. The inherited setup run also exposed one real setup defect, fixed by collapsing the production Groundwork project into the existing AspNetCoreIdentity solution folder.

### Foundational Authority Evidence

- Foundation boundary candidate: the branch `HEAD` commit that contains this section and marks T006-T017 complete; verify with `git rev-parse HEAD`.
- Red baselines captured before implementation:
  - `AspNetCoreIdentityGroundworkRegistrationTests`: failed because the public Groundwork ASP.NET Core Identity feature/registration path did not exist.
  - `GroundworkPersistenceCoverageTests` Spec 095 slices: failed because legacy IAM authority constants and unified provider implicit Identity registration were still present.
  - `IdentityStorageManifestTests`: failed because the then-planned nine-unit ASP.NET Core Identity authority manifest, bounded routes, physical storage, and SQL Server key-budget assertions were not yet declared. This is red-baseline provenance, not the current twelve-unit manifest denominator.
- Final foundational validation:
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `20` passed, `0` failed.
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `5` passed, `0` failed.
  - `tests/Elsa/Persistence/Groundwork/Composition/Tests/Elsa.Persistence.Groundwork.Composition.Tests.csproj`: `42` passed, `0` failed.
  - `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Elsa.Persistence.Groundwork.UnifiedHost.Tests.csproj`: `9` passed, `0` failed.
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj` Spec 095 slices: `3` passed, `0` failed.
- Review verdict: foundational boundary accepted for continuing into US1. This proves one explicit physical Identity authority, explicit feature selection, provider substrate/Identity separation, manifest route admission, and fixture continuity. It does not claim complete ASP.NET Core Identity user/role behavior; T018-T038 own that surface.

### US1 T018-T020 Contract Evidence

- Red baseline captured before store implementation:
  - Command: `dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityUserStoreContractTests|FullyQualifiedName~AspNetCoreIdentityRoleStoreContractTests|FullyQualifiedName~AspNetCoreIdentityRelationshipContractTests" --logger "trx;LogFileName=spec095-us1-red-t018-t020.trx"`
  - Result: `0` passed, `6` failed.
  - Intended failures: framework user/role create/update/delete returned `GroundworkIdentityStoreNotImplemented`/`GroundworkIdentityRoleStoreNotImplemented`; claims, logins, roles, tokens, authenticator key, and recovery-code lists returned empty/null/no-op values.
- Green validation after document-backed T018-T020 implementation:
  - Focused T018-T020 command above with `LogFileName=spec095-us1-green-t018-t020.trx`: `6` passed, `0` failed.
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `20` passed, `0` failed.
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `11` passed, `0` failed.
- T021 same-authority adapter evidence:
  - Initial adapter projection tests passed after T018-T020 because the framework stores already wrote the shared authority documents.
  - Preservation red: `Framework_and_elsa_user_updates_preserve_each_others_authoritative_fields` failed with a null password hash after an Elsa user save (`spec095-us1-t021-preservation-red.trx`).
  - Preservation green: the same test passed after preserving `FrameworkState` on Elsa saves and preserving Elsa IAM fields on framework saves (`spec095-us1-t021-preservation-green.trx`).
- T022 highest-seam evidence:
  - `AspNetCoreIdentityHighestSeamTests`: `3` passed, `0` failed (`spec095-us1-t022-protected-endpoint.trx`), covering real DI, `UserManager`, `SignInManager`, Groundwork stores, username/email sign-in, equivalent bad-password/unknown-user failure, cookie issuance, permission claim/session projection, and a TestServer-hosted `ConfigurePermissions()` endpoint authorized by the issued cookie.
  - Red-to-green note: the protected endpoint first redirected to login (`302`) because the provider-neutral cookie was issued but not configured as the default authenticate/challenge scheme in a Groundwork-only host. `ConfigureAspNetCoreIdentityDefaultAuthenticationSchemes` now sets the cookie defaults unless the host or OpenIddict selector has already chosen defaults.
- T026-T030 relationship-file/coordinator evidence:
  - Relationship behavior is split into `GroundworkIdentityUserClaims`, `GroundworkIdentityUserLogins`, `GroundworkIdentityUserRoles`, and `GroundworkIdentityUserTokens`.
  - `GroundworkIdentityRelationshipCoordinator` owns relationship document save/delete intent routing for claims, logins, role links, and tokens while preserving deterministic IDs and existing bounded-query read paths.
  - Focused relationship validation: `AspNetCoreIdentityRelationshipContractTests`: `2` passed, `0` failed.
  - Provider portability correction: compound portable indexes for claim/login/token/membership keys were replaced with deterministic scalar key fields (`claimKey`, `loginKey`, `tokenKey`, `membershipKey`) so file-backed SQLite can materialize the Identity manifest.
- T031-T033 adapter/registration evidence:
  - Elsa user/role stores preserve framework state, and framework user/role stores preserve Elsa IAM fields when both seams write the same authority document.
  - Elsa external identity and tenant membership stores use the same Identity authority document kinds and manifest-backed IDs.
  - Registration coverage proves the exact supported framework interface denominator, absence of queryable/passkey/protected-user false capabilities, scoped lifetimes, and exactly one Groundwork authority marker.
- T036-T037 full US1 validation:
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `20` passed, `0` failed. Includes the file-backed SQLite close/reopen scenario (`spec095-us1-sqlite-reopen.trx`) proving user, role, claim, external-login, role-link, and token state survive a fresh SQLite client over the same database.
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `20` passed, `0` failed.
  - `tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj`: `119` passed, `0` failed. This preserves existing EF/OpenIddict Identity behavior, including anonymous protected-endpoint `401` and bearer-token authorization.
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed.
- Scope note: this proves the framework scalar contracts, deterministic relationship contracts, same-authority adapter projection, explicit registration denominator, existing EF Identity continuity, and protected-endpoint sign-in/cookie/session behavior over the shared Groundwork authority. It does not complete manager call sequencing audit beyond current scenario coverage, or US2 concurrency/tenant race guarantees.
- T038 review verdict:
  - US1 candidate commit: the branch `HEAD` commit that contains this section and marks T018-T038 complete; verify with `git rev-parse HEAD`.
  - Capability truthfulness accepted: registration exposes only implemented user/role/login/claim/role/token/authenticator/recovery-code interfaces and proves queryable, passkey, and protected-user interfaces do not resolve.
  - One-authority verdict accepted: framework stores, Elsa user/role stores, external identity, and tenant membership use the same Identity authority document kinds; architecture reconciliation maps the new #644 units and rejects stale legacy IAM authority units.
  - Field-preservation verdict accepted: framework writes preserve Elsa IAM ownership/roles/permissions, and Elsa writes preserve framework password/security/contact/lockout state.
  - Test-continuity verdict accepted: existing EF/OpenIddict Identity tests remain green, the new Groundwork suite includes file-backed SQLite close/reopen, and the architecture ratchets pass against the updated authority names.

### US2 T039 Tenant Binding Evidence

- Red baseline:
  - `AspNetCoreIdentityTenantContractTests`: `1` passed, `2` failed (`spec095-us2-red-t039.trx`).
  - Intended failures: cross-tenant same normalized username/email lookup returned `null` for the second tenant because lookup used a global normalized index and post-filtered the first result; framework role create could not reject mismatched tenant data because `IdentityRole` carries no tenant field.
- Green validation:
  - `AspNetCoreIdentityTenantContractTests`: `3` passed, `0` failed (`spec095-us2-green-t039.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `23` passed, `0` failed (`spec095-us2-t039-full-aspnet-groundwork.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `20` passed, `0` failed (`spec095-us2-t039-persistence-fixtures.trx`).
- Implementation note: user/role normalized lookup indexes now use deterministic tenant-scoped scalar keys (`normalizedUserNameKey`, `normalizedEmailKey`, `normalizedRoleNameKey`) while retaining the original normalized values for diagnostics/projection.

### US2 T040 Concurrency Contract Evidence

- Red baseline:
  - Command: `dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityConcurrencyContractTests" --logger "trx;LogFileName=spec095-us2-red-t040.trx"`
  - Result: `0` passed, `4` failed.
  - Intended failures: same-tenant duplicate normalized user and role creates succeeded; stale user update succeeded instead of returning `ConcurrencyFailure`; two independently loaded lockout increments both succeeded but persisted only one increment.
- Green validation after initial revision/uniqueness/CAS implementation:
  - `AspNetCoreIdentityConcurrencyContractTests`: `4` passed, `0` failed (`spec095-us2-green-t040-attempt1.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `27` passed, `0` failed (`spec095-us2-t040-full-aspnet-groundwork-attempt1.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `20` passed, `0` failed (`spec095-us2-t040-persistence-attempt1.trx`).
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed (`spec095-us2-t040-architecture-attempt1.trx`).
- Scope note: this proves the first T040 contracts only. T046, T047, and T050 are now covered by their dedicated evidence sections below.

### US2 T041 Atomicity Contract Evidence

- Red baseline:
  - Command: `dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityAtomicityContractTests" --logger "trx;LogFileName=spec095-us2-red-t041.trx"`
  - Result: `0` passed, `2` failed.
  - Intended failures: deleting a user left dependent claim/login/role/token relationship documents behind; a stale user object could create a role link after the user document had been deleted.
- Green validation after bounded dependent-delete cleanup and relationship owner existence guards:
  - `AspNetCoreIdentityAtomicityContractTests`: `2` passed, `0` failed (`spec095-us2-green-t041-attempt1.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `29` passed, `0` failed (`spec095-us2-t041-full-aspnet-groundwork-attempt1.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `20` passed, `0` failed (`spec095-us2-t041-persistence-attempt1.trx`).
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed (`spec095-us2-t041-architecture-attempt1.trx`).
- Scope note: this closes the visible orphan/stale-link contracts introduced by T041. T048/T049 remain open for full owner-registry CAS, injected failure windows, and multi-document atomicity truthfulness.

### US2 T042 Email Contract Evidence

- Red baseline:
  - Command: `dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityEmailContractTests" --logger "trx;LogFileName=spec095-us2-red-t042.trx"`
  - Result: `0` passed, `2` failed.
  - Intended failures: same-tenant duplicate normalized email create succeeded; ambiguous normalized-email lookup returned an arbitrary user instead of failing closed.
- Green validation after tenant-scoped email conflict detection and fail-closed email lookup:
  - `AspNetCoreIdentityEmailContractTests`: `2` passed, `0` failed (`spec095-us2-green-t042-attempt1.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `31` passed, `0` failed (`spec095-us2-t042-full-aspnet-groundwork-attempt1.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `20` passed, `0` failed (`spec095-us2-t042-persistence-attempt1.trx`).
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed (`spec095-us2-t042-architecture-attempt1.trx`).
- Scope note: this proves the T042 duplicate/ambiguous email behavior. T047 below adds explicit email-reservation documents and native race reconciliation.

### US2 T043 Reconciliation Contract Evidence

- Red baseline:
  - Command: `dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityReconciliationTests" --logger "trx;LogFileName=spec095-us2-red-t043.trx"`
  - Result: `1` passed, `2` failed.
  - Intended failures: the not-committed failure path already propagated and left no document; committed lost-acknowledgement windows still surfaced raw `OperationCanceledException`/`IOException` after the document had been saved.
- Green validation after deterministic saved-document reconciliation:
  - `AspNetCoreIdentityReconciliationTests`: `3` passed, `0` failed (`spec095-us2-green-t043-attempt1.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `34` passed, `0` failed (`spec095-us2-t043-full-aspnet-groundwork-attempt1.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `20` passed, `0` failed (`spec095-us2-t043-persistence-attempt1.trx`).
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed (`spec095-us2-t043-architecture-attempt1.trx`).
- Scope note: this proves single-document save reconciliation for committed and not-committed outcomes. T051 remains open for independent bounded reconciliation tokens and broader multi-document uncertain-commit handling.

### US2 T044 Revision Capability Evidence

- Red baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~IdentityGroundworkRevisionContractTests" --logger "trx;LogFileName=spec095-us2-red-t044.trx"`
  - Result: `0` passed, `4` failed.
  - Intended failures: Groundwork IAM user, role, external identity, and tenant membership stores still implemented only the base contracts and were not assignable to the optional provider-neutral revision-aware contracts.
- Green validation after additive optional contracts and Groundwork `ExpectedVersion` consumption:
  - `IdentityGroundworkRevisionContractTests`: `4` passed, `0` failed (`spec095-us2-green-t044.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `24` passed, `0` failed.
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `34` passed, `0` failed.
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed.
- Scope note: this adds `IRevisionAware*Store` overlay contracts and `IamRevision*` result types in the provider-neutral IAM abstractions. Groundwork maps these to envelope versions and CAS writes; malformed revisions fail closed as conflicts. Base contracts remain unchanged, so EF and in-memory implementations stay on the compatibility path with no new Groundwork dependency or EF behavior.

### US2 T045 Sign-In Tenant Binding Evidence

- Red baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityHighestSeamTests.Sign_in_binds_requested_tenant_before_manager_lookup" --logger "trx;LogFileName=spec095-us2-red-t045.trx"`
  - Result: `0` passed, `1` failed.
  - Intended failure: secondary-tenant sign-in still executed manager lookup in the primary/default persistence scope and failed before it could resolve the secondary user's password/session.
- Green validation after binding the effective tenant before `UserManager` lookup and removing post-lookup tenant filtering:
  - `AspNetCoreIdentityHighestSeamTests.Sign_in_binds_requested_tenant_before_manager_lookup`: `1` passed, `0` failed (`spec095-us2-green-t045.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `35` passed, `0` failed.
  - `tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --filter "FullyQualifiedName~AspNetCoreIdentitySignInTests|FullyQualifiedName~AspNetCoreIdentityRegistrationTests"`: `11` passed, `0` failed.
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed.
- Scope note: this registers provider-neutral persistence scope services from the ASP.NET Identity base registration and binds the requested/default sign-in tenant before any framework manager lookup. Groundwork framework/Elsa stores continue to enforce entity scope at their write/read boundaries; EF remains behaviorally covered by the existing sign-in/registration slice.

### US2 T046 Authoritative Revision Stamp Evidence

- Red baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityConcurrencyContractTests" --logger "trx;LogFileName=spec095-us2-red-t046.trx"`
  - Result: `4` passed, `3` failed.
  - Intended failures: malformed user stamps and cross-user/cross-role stamp replay were accepted as unconditional or same-version writes instead of returning concurrency failures without mutation.
- Green validation after scoped opaque framework stamps and strict expected-version parsing:
  - `AspNetCoreIdentityConcurrencyContractTests`: `7` passed, `0` failed (`spec095-us2-green-t046.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `38` passed, `0` failed.
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `24` passed, `0` failed.
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed.
- Scope note: framework user/role stamps now encode an opaque fingerprint of entity kind, tenant, entity id, and Groundwork envelope version. Update/delete operations reject malformed or wrong-entity stamps as `ConcurrencyFailure`; successful saves rotate the stamp from the saved envelope version. The provider-neutral IAM revision overlay from T044 keeps its generic version-only revision for adapter CAS and remains separate from framework concurrency stamps.

### US2 T047 Email Reservation Evidence

- Red baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityEmailContractTests" --logger "trx;LogFileName=spec095-us2-red-t047-resume.trx"`
  - Result: `2` passed, `1` failed.
  - Intended failure: two independent same-tenant creates using the same normalized email were allowed past bounded email preflight without a native create-only reservation, so the contract did not converge to exactly one success and one `DuplicateEmail` result.
- Green validation after create-only email reservations:
  - `AspNetCoreIdentityEmailContractTests`: `3` passed, `0` failed (`spec095-us2-green-t047.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `39` passed, `0` failed (`spec095-us2-green-t047-aspnet-groundwork.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `24` passed, `0` failed (`spec095-us2-green-t047-persistence-groundwork.trx`).
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed (`spec095-us2-green-t047-architecture.trx`).
- Scope note: normalized email reservations now use the declared `identityEmailReservation` physical unit. The tenant-scoped normalized email key is both the reservation document id and projected `emailReservationKey`; saves use `ExpectedVersion: 0` to linearize concurrent creates. Existing email preflight remains for duplicate classification and ambiguous-document fail-closed behavior. User update/delete reserve or release email ownership around successful CAS writes. T049-T051 remain open for dependent delete registry CAS, lockout retry, and independent reconciliation tokens.

### US2 T048 Relationship Registry Unit-Of-Work Evidence

- Red baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityAtomicityContractTests" --logger "trx;LogFileName=spec095-us2-red-t048.trx"`
  - Result: `3` passed, `1` failed.
  - Intended failure: adding a user-role link created the scalar link document but did not register the deterministic link id on the user and role owner documents, and therefore did not advance the user envelope revision/stamp through the relationship mutation.
- Green validation after owner-registry UoW implementation:
  - `AspNetCoreIdentityAtomicityContractTests`: `4` passed, `0` failed (`spec095-us2-green-t048.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `41` passed, `0` failed (`spec095-us2-green-t048-aspnet-groundwork.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `24` passed, `0` failed (`spec095-us2-green-t048-persistence-groundwork.trx`).
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed (`spec095-us2-green-t048-architecture.trx`).
- Scope note: user-role add/remove now stages the scalar `identityUserRole` link plus user `RoleLinkIds` and role `UserLinkIds` registry updates inside one Groundwork `DocumentCommitScope` with expected owner versions. Successful user-role mutations rotate the in-memory user's public revision stamp. Ordinary user/role saves preserve registry fields. T049 below covers registry-driven dependent delete for user-role links; T050-T051 remain open for lockout retry and independent reconciliation tokens.

### US2 T049 Dependent Delete Registry Evidence

- Red baselines:
  - User delete command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityAtomicityContractTests" --logger "trx;LogFileName=spec095-us2-red-t049.trx"`
  - User delete result: `3` passed, `1` failed. Intended failure: deleting a user removed the scalar user-role link but left the deleted user's link id in the role owner registry.
  - Role delete command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityAtomicityContractTests" --logger "trx;LogFileName=spec095-us2-red-t049-role-rerun.trx"`
  - Role delete result: `4` passed, `1` failed. Intended failure: deleting a role left the scalar `identityUserRole` link behind instead of deleting it and removing the link id from the user registry.
- Green validation after registry-driven user/role dependent delete:
  - `AspNetCoreIdentityAtomicityContractTests`: `5` passed, `0` failed (`spec095-us2-green-t049.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `42` passed, `0` failed (`spec095-us2-green-t049-aspnet-groundwork.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `24` passed, `0` failed (`spec095-us2-green-t049-persistence-groundwork.trx`).
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed (`spec095-us2-green-t049-architecture.trx`).
- Scope note: user deletion now opens one Groundwork unit of work over user, role, and user-role documents, removes registered role links, CAS-updates linked role registries, and deletes the user at its expected version. Role deletion mirrors this by removing registered user links, CAS-updating linked user registries, and deleting the role at its expected version. Existing claim/login/token cleanup still uses bounded relationship routes where no registry population exists yet; T051 remains open for independent reconciliation tokens.

### US2 T050 Lockout CAS Retry Evidence

- Red baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityConcurrencyContractTests" --logger "trx;LogFileName=spec095-us2-red-t050.trx"`
  - Result: `7` passed, `2` failed.
  - Intended failures: independent stale lockout increments did not persist through the lockout store method and converged to count `1` instead of `2`; reset/end-date lockout transitions did not persist without a later broad `UpdateAsync`.
- Green validation after bounded CAS retry lockout transitions:
  - `AspNetCoreIdentityConcurrencyContractTests`: `9` passed, `0` failed (`spec095-us2-green-t050.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `44` passed, `0` failed (`spec095-us2-green-t050-aspnet-groundwork.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `24` passed, `0` failed (`spec095-us2-green-t050-persistence-groundwork.trx`).
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity"`: `65` passed, `0` failed (`spec095-us2-green-t050-architecture.trx`).
- Scope note: `IncrementAccessFailedCountAsync`, `ResetAccessFailedCountAsync`, `SetLockoutEndDateAsync`, and `SetLockoutEnabledAsync` now persist narrow lockout transitions immediately with the caller's expected user envelope version. CAS conflicts reload the exact user, reapply the same logical lockout transition, retry up to the bounded limit, and refresh the caller's public revision stamp on success. T051 below covers independent reconciliation tokens and broader uncertain-commit truthfulness.

### Historical US2 T051 Independent Reconciliation Token Evidence (Superseded)

The following run records the original T051 design. T084 remediation superseded its standalone `identityReconciliationToken` shape; it is not the current persistence contract.

- Red baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityReconciliationTests" --logger "trx;LogFileName=spec095-us2-red-t051.trx"`
  - Result: `3` passed, `1` failed.
  - Intended failure: the content-only reconciler accepted an equivalent target document written by another actor after this request failed before commit.
- Green validation after independent reconciliation-token UoW:
  - `AspNetCoreIdentityReconciliationTests`: `4` passed, `0` failed (`spec095-us2-green-t051-reconciliation.trx`; token-id strategy rerun `spec095-us2-green-t051-reconciliation-rerun.trx`).
  - `IdentityStorageManifestTests`: `5` passed, `0` failed (`spec095-us2-green-t051-manifest.trx`).
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `45` passed, `0` failed (`spec095-us2-green-t051-aspnet-groundwork.trx`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `24` passed, `0` failed (`spec095-us2-green-t051-persistence-groundwork.trx`).
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`: initial run `200` passed, `1` failed due to the new token storage unit missing from the checked-in coverage mapping (`spec095-us2-green-t051-architecture.trx`); rerun after mapping update: `201` passed, `0` failed (`spec095-us2-green-t051-architecture-rerun.trx`).
- Historical scope note: at that point, `GroundworkIdentityAtomicWrite` staged an independent `identityReconciliationToken` document and the target document inside one `DocumentCommitScope`. Reconciliation after cancellation/transport uncertainty first required the per-attempt token and matching request fingerprint before accepting the target content, so equivalent external writes no longer produced false-positive success.

Current manifest `1.0.4` uses the `identityMutationReceipt` physical entity unit instead. A deterministic operation/request fingerprint identifies the mutation; the receipt and authority changes are staged in one unit of work; the receipt records the durable outcome used after an uncertain acknowledgement; and an explicitly bounded expiry query drives cleanup. This supports aggregate and relationship mutations without treating an equivalent external write as proof that the current attempt committed.

### US2 T052 SQLite Independent-Client Race Evidence

- Green validation:
  - Focused file-backed SQLite race command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~File_backed_sqlite_independent_clients_linearize_identity_races" --logger "trx;LogFileName=spec095-us2-t052-sqlite-races.trx"`
  - Focused result: `1` passed, `0` failed; the test runs `100` iterations each for duplicate create, stale revision update, lockout increment, link/delete, and seed-like create races against two independent clients over the same file-backed SQLite database (`spec095-us2-t052-sqlite-races.trx`, duration `1 m 45 s`).
  - Concurrency class command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~AspNetCoreIdentityConcurrencyContractTests" --logger "trx;LogFileName=spec095-us2-green-t052-concurrency.trx"`
  - Concurrency class result: `10` passed, `0` failed (`spec095-us2-green-t052-concurrency.trx`, duration `1 m 39 s`).
- Scope note: the SQLite stress path reuses two long-lived independent provider clients for the full run and gives every iteration unique user/role identities. Link/delete accepts either valid winner but rejects orphaned scalar links and mismatched user/role owner registries.

### US2 T053 Direct Branch Coverage Evidence

- Green validation:
  - Focused branch command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj -c Release --nologo --verbosity minimal --no-restore --filter "FullyQualifiedName~Duplicate_user_normalized_name_on_update|FullyQualifiedName~Duplicate_role_normalized_name_on_update|FullyQualifiedName~Malformed_role_stamp|FullyQualifiedName~Missing_user_delete|FullyQualifiedName~Missing_role_delete|FullyQualifiedName~AspNetCoreIdentityFailureMappingTests" --logger "trx;LogFileName=spec095-us2-green-t053-branch-focused.trx"`
  - Focused result: `11` passed, `0` failed (`spec095-us2-green-t053-branch-focused.trx`).
  - Full ASP.NET Groundwork Identity result: `57` passed, `0` failed (`spec095-us2-green-t053-aspnet-groundwork.trx`, duration `2 m`).
- Scope note: added direct coverage for duplicate-on-update user/role classification, malformed role stamp decoding, not-found delete write mapping, and all public Groundwork Identity exception-to-`IdentityResult` mappings including timeout/generic fallback. Existing US2 suites cover scope binding, owner registries, delete coordination, cancellation, and uncertain-commit branches.

### US2 T054 Full US1/US2 Validation Evidence

- Green validation:
  - `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj`: `57` passed, `0` failed (`spec095-us2-green-t054-aspnet-groundwork.trx`, duration `1 m 34 s`, SHA-256 `bdbad6f0f2d7dd4ec5197d20ef6f3acc58d71cfc2beb1cb266c1023d904df0ce`).
  - SQLite close/reopen scenario: `1` passed, `0` failed (`spec095-us2-green-t054-sqlite-reopen.trx`, duration `1 s`, SHA-256 `672ccfa706afbe32e622f97e34ab4927dff53f1c1d0ddb93915f99bf111d4865`).
  - `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj`: `24` passed, `0` failed (`spec095-us2-green-t054-persistence-groundwork.trx`, duration `1 s`, SHA-256 `039ee0881f78360cb40e83e3efae74b814c031fd54f8a79cb1bec18e94b845ad`).
  - `tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj`: `119` passed, `0` failed (`spec095-us2-green-t054-foundation-identity.trx`, duration `21 s`, SHA-256 `bf1ef13a5a2704e23ae9e40f7a1505d05ba35f7fa215c8f73a4da7e008bd2ef8`).
  - `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`: `201` passed, `0` failed (`spec095-us2-green-t054-architecture.trx`, duration `40 s`, SHA-256 `8be7db2816ebcd5b97ad17c7d0c7cfcc23b50fbdb2c0de6859533b37b67e266e`).
- Race digest / zero-orphan evidence:
  - SQLite race TRX from T052: SHA-256 `4ee3e7540be8c2554a84468d14a8c149ea4c25f003ce89f990c2d55d53699016` (`spec095-us2-t052-sqlite-races.trx`).
  - The full ASP.NET Groundwork suite includes the same `100`-iteration file-backed SQLite race test. It covers duplicate create, stale revision update, lockout increment, link/delete, and seed-like create races against two independent provider clients. The link/delete invariant rejects orphaned scalar `identityUserRole` links and mismatched user/role owner registries; zero orphan assertions passed in the focused and full-suite runs.

### US2 T055 Exact-Head Review Verdict

- Reviewed commit: `d8614401af161a434af742dee1420fb2b8fef66e`.
- Verdict: accepted for closing US2.
- Review findings:
  - Tenant non-disclosure: accepted. Tenant-bound lookup keys and wrong-scope load tests prove same normalized values can coexist without leaking cross-tenant users or roles.
  - Concurrency linearization: accepted. Groundwork envelope versions drive framework stamps, stale operations fail closed, lockout retries preserve both increments, and the SQLite `100`-iteration race test converges duplicate/stale/lockout/seed-like creates without split-brain writes.
  - Email ambiguity: accepted. Duplicate normalized email creates return `DuplicateEmail`, ambiguous email reads fail closed, and native reservation documents linearize concurrent creates.
  - Manager call sequencing: accepted. Sign-in binds the effective tenant before `UserManager` lookup, and the highest-seam tests cover successful tenant-specific cookie/session/claims behavior plus bad-password/unknown-user equivalence.
  - Owner registries and delete coordination: accepted. Relationship mutations update both owner registries in one unit of work; user/role dependent delete removes registered links and the SQLite link/delete stress invariant found zero orphaned scalar links or mismatched registries.
  - Historical uncertain-commit verdict: accepted for the superseded token implementation. Current acceptance must instead prove the mutation-receipt outcome/fingerprint and bounded-cleanup contract on the exact preview.60 candidate.
- Open scope after US2: provider matrix/performance/schema/seeding work remains in US3/US4 and polish tasks. This verdict does not claim #646/#647 completion.

### US3 T063-T067 Provider Evidence And Schema Contract Summary

- Provider-specific manifest reconciliation commit: `9c02e215 Reconcile Identity manifest provider limits`.
  - Identity persistence tests: `37` passed, `0` failed (`spec095-us3-t063-identity-persistence-final.trx`, SHA-256 `68a099317bb8db7e42da82fa3c8c5f7effbb01d64d49b131888b140b3f3fd516`).
  - ASP.NET Core Identity Groundwork tests: `57` passed, `0` failed (`spec095-us3-t063-identity-groundwork-final.trx`, SHA-256 `6b0aa61328fef29b18ecf25c85fe5971e0d6fa598e40a12720458ba167c8b074`).
  - Selected SQLite/SQL Server conformance tests: `4` passed, `0` failed (`spec095-us3-t063-conformance-sqlite-sqlserver-final.trx`, SHA-256 `5a674eceb231f604661631ff28d1ac77e3a9fe9a099065a15f04391552451f96`).
  - Provider capability tests: `9` passed, `0` failed (`spec095-us3-t063-provider-capability-final.trx`, SHA-256 `9fa0008e0f8d23ec536dc9f37d470a28ac7fbb14656d7600f79a51392df81be1`).
- Schema CLI readiness commit: `7414dc6f Add Identity schema CLI readiness contracts`.
  - Identity schema CLI tests: `4` passed, `0` failed (`spec095-us3-t064-schema-cli-final.trx`, SHA-256 `c52de0b0c48757bda15d7a0620558ced71db91f23e3e16d3d01fdc7b1d8e183a`).
  - Identity persistence rerun: `37` passed, `0` failed (`spec095-us3-t064-identity-persistence-rerun.trx`, SHA-256 `185478d7c4b328c1086ee7b1969b9cf34e81610083939590f7c494c307dbfd37`).
- Explicit Identity deployment-schema selection commit: `12def50d Add Identity-selected reference deployment schema`.
  - Full composition suite: `44` passed, `0` failed (`spec095-us3-t065-composition-full.trx`, SHA-256 `fa94720681eee474271ca90aa36773bfe66cf0a2a41b96c4bf4115d16b387cdd`).
  - Reference schema CLI regression: `1` passed, `0` failed (`spec095-us3-t065-reference-schema-rerun.trx`, SHA-256 `c96ed5c90394254402983e47ad750c03fb2a27051c49d12160b8d31359747566`).
- Provider matrix evidence commit: `e324cd98 Capture Identity provider evidence matrix`.
  - Four-provider matrix plus native-plan acceptance: `6` passed, `0` failed (`spec095-us3-t066-provider-matrix.trx`, SHA-256 `35147e8c9fb2c29787f26757ef7efed7d1f90e69c5be95ed8eadf2c9deed8477`).
  - Sanitized public scenario: `identity-authority-baseline`.
  - Canonical public result digest: `719708f5192bc589a05bd319468b55e62c25801b9c45b6a02c5ec4770b4a9c49`.
  - Canonical input fingerprint: `5d60763e3cbebda467e8a8375411a1b2fc0b51d8329222628fc548bc72e7ea35`.
  - Provider evidence ledger: `specs/095-groundwork-aspnetcore-identity/evidence/providers/matrix.md`.
  - T062 native-plan reruns retained:
    - `spec095-us3-t062-conformance-rerun.trx`: `3` passed, SHA-256 `df4719012561a863d7a49ef8720d8fb33e12177951146df5d34d3f8edf2bb75a`.
    - `spec095-us3-t062-identity-groundwork-rerun.trx`: `57` passed, SHA-256 `bad28ac0d7cf540b352730a4c44f0ce65fac5d60144512b872cc162d195910a0`.
- Spec 094 coverage-ledger link commit: `dd4eb319 Link Identity provider evidence into coverage ledger`.
  - Linked rows: `iam-user`, `iam-role`, `iam-external-identity`, and `iam-tenant-membership`.
  - Performance/final readiness status was not advanced; #646/#647 gates remain open.
  - Architecture coverage rerun: `35` passed, `0` failed (`spec095-us3-t067-coverage-ledger-rerun.trx`, SHA-256 `f7da263f1b6c8a5f1aff85782991880efeda35a90b8fd43be06e6072784e259a`).

### US3 T068 Exact-Head Review Verdict

- Reviewed commit: `dd4eb319c312d5c4add92dc962281d50fb2c792d` plus this T068 evidence commit.
- Verdict: accepted for closing US3.
- Review findings:
  - Four-provider semantic equivalence: accepted. SQLite, SQL Server, PostgreSQL, and MongoDB execute the same Identity authority scenario and retain the same public result digest `719708f5192bc589a05bd319468b55e62c25801b9c45b6a02c5ec4770b4a9c49`.
  - Topology truthfulness: accepted. Relational providers and SQLite advertise persistent storage, independent clients, multi-document transactions, and restart support; MongoDB requires a transaction-capable replica-set topology and retains standalone rejection evidence.
  - Native bounded execution: accepted. The 100,000-record T062 verdict proves physical bounded routes for normalized user, normalized email, normalized role, tenant role listing, login, claims, role claims, user roles, role users, logins, tokens, and tenant membership. Runtime calls issue finite `Take` values before materialization where exercised.
  - Schema immutability/readiness: accepted. CLI validate/plan/status/apply coverage proves the Identity manifest can be validated offline/live, planned/applied explicitly, and kept read-only during runtime admission.
  - Explicit feature selection: accepted. Provider substrate schemas still exclude Identity by default; the public reference composition includes Identity only through `GroundworkAllFeaturesWithIdentityDeploymentSchema`.
  - Sanitized evidence: accepted. Provider evidence records carry only package/provider/topology/fingerprint/result hashes and sanitized scenario artifacts; connection strings, credentials, tenant identifiers, and raw process/container details are not retained.
- Open scope after US3: seeding/highest-seam operational readiness and #646 performance handoff remain in US4; polish/landing tasks still need full-candidate validation and Model B PR/merge. This verdict does not claim performance completion or final zero-EF deletion readiness.

### US4 T069 Seeder Contract Red Baseline

- Red baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj --filter FullyQualifiedName~AspNetCoreIdentitySeederContractTests --logger "trx;LogFileName=spec095-us4-t069-red-seeder-contracts.trx"`
  - Result: `0` passed, `6` failed.
  - TRX SHA-256: `b5387247cad4496bcf7126efed38b50ed8c8a49c1a0097c47ae3635026c80119`.
- Intended failures:
  - `IdentitySeedOptions` still lives in the EF namespace rather than the provider-neutral ASP.NET Core Identity layer.
  - `IdentitySeedCoordinator` does not exist yet, so partial configuration, password-policy rejection, wildcard/catalog grants, and two-instance convergence have no provider-neutral contract.
  - `GroundworkIdentitySeeder` and the optional Groundwork registration overload do not exist yet, so dual lifecycle and secret-safe logging are not wired for the Groundwork implementation.

### US4 T070 Production Highest-Seam Red Baseline

- Red baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj --filter FullyQualifiedName~Production_seeded_groundwork_host_signs_in_authorizes_survives_restart_and_locks_out --logger "trx;LogFileName=spec095-us4-t070-red-production-highest-seam.trx"`
  - Result: `0` passed, `1` failed.
  - TRX SHA-256: `155d001de70f4fa7795dbf4f577f1f435fdffc64263d806be46f5275a5cacbdd`.
- Intended failure: the production-shaped Groundwork host cannot configure the seed account because provider-neutral `IdentitySeedOptions` and the Groundwork seed registration overload do not exist yet.
- Regression guard rerun:
  - Existing cookie/protected-endpoint seam: `1` passed, `0` failed (`spec095-us4-t070-existing-cookie-rerun.trx`, SHA-256 `b3e8bf0f190b413fe3f4079baf503eec5ef86391b3df4b6fcb971a46d5b50d29`).

### US4 T071 Performance Workload Red Baseline

- Red baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj --filter FullyQualifiedName~AspNetCoreIdentityPerformanceWorkloadTests --logger "trx;LogFileName=spec095-us4-t071-red-performance-workload.trx"`
  - Result: `0` passed, `4` failed.
  - TRX SHA-256: `e6e68f0700cecc676c3828292357bdc66621847b9b2065b07446ed02d9722660`.
- Intended failures:
  - The deterministic `iam-normalized-lookup-update` correctness workload does not exist yet.
  - The frozen temporary EF oracle adapter does not exist yet.
  - Timing remains blocked until the Groundwork workload and frozen EF oracle expose matching public result digests.

### US4 T072-T074 Seeder Implementation Green Baseline

- Provider-neutral seeding moved into `Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding`:
  - `IdentitySeedOptions` is no longer owned by the EF namespace.
  - `IdentitySeedCoordinator` owns Groundwork admin role/user convergence and grants `*` plus every catalog permission.
  - EF seeding keeps its pre-US4 schema and account seeding behavior for the frozen temporary oracle.
  - Groundwork seeding registers one `GroundworkIdentitySeeder` instance under both `IHostedService` and `IShellInitializer`.
- Green baselines:
  - Seeder contracts: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj --filter FullyQualifiedName~AspNetCoreIdentitySeederContractTests --logger "trx;LogFileName=spec095-us4-t069-green-seeder-contracts.trx"` — `6` passed, `0` failed; SHA-256 `246e8154e193bf863bd5921a2da090bddca2871c850f5965a00efdf20c7ba507`.
  - Production seeded Groundwork highest seam: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj --filter FullyQualifiedName~Production_seeded_groundwork_host_signs_in_authorizes_survives_restart_and_locks_out --logger "trx;LogFileName=spec095-us4-t070-green-production-highest-seam.trx"` — `1` passed, `0` failed; SHA-256 `762e5af1175a1161302eb5f19e3e2fc5ba97661ca92e791cadd5b6b57d800a52`.
  - EF identity regression: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --filter "FullyQualifiedName~AspNetCoreIdentityRegistrationTests|FullyQualifiedName~AspNetCoreIdentitySignInTests" --logger "trx;LogFileName=spec095-us4-t072-t074-ef-identity-regression.trx"` — `11` passed, `0` failed; SHA-256 `0ce4c16adfcbc7ca0e4a9e1a2a6f2af16b495982b9e714845c4cb606360bb8c1`.
  - Groundwork registration/highest seam regression: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj --filter "FullyQualifiedName~AspNetCoreIdentityGroundworkRegistrationTests|FullyQualifiedName~AspNetCoreIdentityHighestSeamTests" --logger "trx;LogFileName=spec095-us4-t072-t074-groundwork-registration-highest-seam.trx"` — `11` passed, `0` failed; SHA-256 `5d9121da054a718695151e243dc884d49c0bcadebac12b925a62d772a2f8d8ec`.
  - Post-membership coordinator rerun: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj --filter "FullyQualifiedName~AspNetCoreIdentitySeederContractTests|FullyQualifiedName~Production_seeded_groundwork_host_signs_in_authorizes_survives_restart_and_locks_out" --logger "trx;LogFileName=spec095-us4-t072-t074-seeder-rerun.trx"` — `7` passed, `0` failed; SHA-256 `2fa8e62acb9f6175c83973299dbe1d37580b58dc7d6e64fb26b7192419be2c96`.
  - Post-membership EF regression rerun: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --filter "FullyQualifiedName~AspNetCoreIdentityRegistrationTests|FullyQualifiedName~AspNetCoreIdentitySignInTests" --logger "trx;LogFileName=spec095-us4-t072-t074-ef-identity-rerun.trx"` — `11` passed, `0` failed; SHA-256 `127e3ed2450ab2615131566a62054d717dc9da3f708009d30d212d1244670538`.

### US4 T075-T076 Performance Handoff And Temporary Oracle Green Baseline

- Workload contract:
  - Definition: `specs/094-harden-groundwork-stores/workloads/iam-secrets.json`.
  - Handoff link: `specs/094-harden-groundwork-stores/contracts/performance-handoff.md`.
  - Workload: `iam-normalized-lookup-update` v1.0.0.
  - Scenario: `identity-authority-baseline`.
  - Input fingerprint: `5d60763e3cbebda467e8a8375411a1b2fc0b51d8329222628fc548bc72e7ea35`.
  - Public result digest: `719708f5192bc589a05bd319468b55e62c25801b9c45b6a02c5ec4770b4a9c49`.
  - Required providers/routes: SQLite, SQL Server, PostgreSQL, MongoDB; normalized user name, normalized email, normalized role, user roles, role users.
- Temporary EF oracle:
  - Implemented only as frozen conformance-test metadata (`temporary-ef-oracle`), with `MutatesEfSurface == false`.
  - The conformance test assembly has no project reference to `Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore`.
- Green baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj --filter "FullyQualifiedName~AspNetCoreIdentityPerformanceWorkloadTests|FullyQualifiedName~AspNetCoreIdentityTemporaryEfOracleTests" --logger "trx;LogFileName=spec095-us4-t075-t076-performance-workload-green.trx"`.
  - Result: `6` passed, `0` failed.
  - TRX SHA-256: `fcb4008545b1812a30a33558a49835a638fe7d5dfbfabe491bb2cc0b5d19c4a8`.
  - JSON validation: `jq empty specs/094-harden-groundwork-stores/workloads/iam-secrets.json`.

### US4 T077 Groundwork Identity Documentation Baseline

- Updated:
  - `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/README.md`
  - `src/Elsa/Foundation/Identity/Persistence/Groundwork/EXTENSION_POINTS.md`
  - `EXTENSION_POINTS.md`
- Documented:
  - explicit `FoundationIdentityAspNetCoreIdentityGroundwork` feature selection;
  - prohibition on dual EF + Groundwork ASP.NET Core Identity authorities;
  - explicit schema CLI use of `GroundworkAllFeaturesWithIdentityDeploymentSchema`;
  - supported provider topology, including MongoDB transaction-capable topology;
  - unsupported capabilities: runtime schema auto-apply, unbounded scans, standalone MongoDB for multi-document Identity guarantees, incomplete seeding, and production password logging.
- Evidence check:
  - Command: `rg -n "FoundationIdentityAspNetCoreIdentityGroundwork|GroundworkAllFeaturesWithIdentityDeploymentSchema|MongoDB.*transaction|Unsupported capabilities|runtime schema auto-apply|scoped" src/Elsa/Foundation/Identity/Persistence/Groundwork/EXTENSION_POINTS.md src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/README.md EXTENSION_POINTS.md`.
  - Result: expected feature/schema/topology/unsupported-capability documentation was found.

### US4 T078 Opt-In Composition And Dual-Authority Guard Baseline

- Added direct composition tests in `tests/Elsa/Foundation/Identity/Tests/Api/EnabledShellCompositionTests.cs`.
- Proved:
  - `FoundationIdentityAspNetCoreIdentityGroundwork` composes opt-in without resolving `ApplicationIdentityDbContext`;
  - EF-then-Groundwork and Groundwork-then-EF ASP.NET Core Identity registrations both fail with a persistence-authority conflict;
  - default `src/Apps/Elsa.Server/shells.json` keeps EF/OpenIddict selected and does not select `FoundationIdentityAspNetCoreIdentityGroundwork`.
- Green baseline:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --filter FullyQualifiedName~EnabledShellCompositionTests --logger "trx;LogFileName=spec095-us4-t078-enabled-shell-composition.trx"`.
  - Result: `6` passed, `0` failed.
  - TRX SHA-256: `b96be9ecc97bfa800a7a0c8e557b6ae671d5f2d0d1a788a8f120c9bef2bdca59`.
  - Post-architecture-ratchet cleanup rerun: `6` passed, `0` failed (`spec095-us4-t078-enabled-shell-composition-rerun.trx`, SHA-256 `794b1033255ed338b97caa1e310c61c3c57915ee3f0de05f9af93aaa022815d5`).

### US4 T079 Operational Evidence Gate

- Two-instance seeding race:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj --filter FullyQualifiedName~Two_instance_groundwork_seeding_converges_for_100_races_without_logging_the_password --logger "trx;LogFileName=spec095-us4-t079-seed-race-100.trx"`.
  - Result: `1` passed, `0` failed; the test performs `100` consecutive two-provider concurrent-seeding races against one shared Identity store.
  - Proven state per run: exactly one seeded user, one seeded role, one seeded tenant membership, required wildcard/catalog grants, active membership, admin role convergence, and zero captured credential values in logs.
  - TRX SHA-256: `ba116999954c0ac0c8faff3d2c518b2ccea36e88cab5b0af7a5620c402ce1c80`.
- Highest seam:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj --filter FullyQualifiedName~AspNetCoreIdentityHighestSeamTests --logger "trx;LogFileName=spec095-us4-t079-highest-seam.trx"`.
  - Result: `6` passed, `0` failed.
  - TRX SHA-256: `fe4ac622fc311581d06cfde1411d40c147221f88d0d90bcbb5b102e04ac63ac8`.
- Deployment schema CLI:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj --filter FullyQualifiedName~AspNetCoreIdentitySchemaCliTests --logger "trx;LogFileName=spec095-us4-t079-schema-cli.trx"`.
  - Result: `4` passed, `0` failed.
  - TRX SHA-256: `30ad7b62bd3df70255273d5ea4bcbcf8be2e4ed7f62ef410fdc1e1f4add40b46`.
- Four-provider correctness, native-plan, and performance handoff workload:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj --filter "FullyQualifiedName~AspNetCoreIdentitySqliteProviderTests|FullyQualifiedName~AspNetCoreIdentitySqlServerProviderTests|FullyQualifiedName~AspNetCoreIdentityPostgreSqlProviderTests|FullyQualifiedName~AspNetCoreIdentityMongoDbProviderTests|FullyQualifiedName~AspNetCoreIdentityNativePlanTests|FullyQualifiedName~AspNetCoreIdentityPerformanceWorkloadTests" --logger "trx;LogFileName=spec095-us4-t079-provider-workload.trx"`.
  - Result: `10` passed, `0` failed.
  - T080 remediation rerun after replacing the placeholder workload digest with the executed fixed-dataset digest: `11` passed, `0` failed (`spec095-us4-t080-provider-workload-remediation.trx`, SHA-256 `1ca7b3e6701b412bb079d0b66d7d773dd66f603fdac8b4b88b2d31c3f899f99b`).
  - Public workload result digest: `719708f5192bc589a05bd319468b55e62c25801b9c45b6a02c5ec4770b4a9c49`.
  - Input fingerprint: `5d60763e3cbebda467e8a8375411a1b2fc0b51d8329222628fc548bc72e7ea35`.
  - Handoff evidence: `specs/094-harden-groundwork-stores/workloads/iam-secrets.json` and `specs/094-harden-groundwork-stores/contracts/performance-handoff.md`.
  - TRX SHA-256: `1dc529107b201c84deb990fd897f1fa1a94fe8b2a38bd11ee358ca072dd8d0f4`.
- Frozen temporary EF oracle:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj --filter FullyQualifiedName~AspNetCoreIdentityTemporaryEfOracleTests --logger "trx;LogFileName=spec095-us4-t079-frozen-ef-oracle.trx"`.
  - Result: `2` passed, `0` failed.
  - Pre/post equality: the frozen EF oracle remains metadata-only, reports `MutatesEfSurface == false`, and exposes the same public digest `719708f5192bc589a05bd319468b55e62c25801b9c45b6a02c5ec4770b4a9c49` as the Groundwork workload.
  - TRX SHA-256: `3ab2019bf3fd8d96161488f1f89b3bd3fe67847a5dc719e63524e444c31f87e2`.
- Architecture ratchets:
  - Command: `/usr/local/share/dotnet/dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity" --logger "trx;LogFileName=spec095-us4-t079-architecture-ratchets-rerun.trx"`.
  - Result: `65` passed, `0` failed.
  - TRX SHA-256: `572757b73d504005d2598d38e2b655d930492529e7162592b5b8e82d61dbdcf9`.
- Objective mapping:
  - #644 correctness: complete for the ASP.NET Core Identity Groundwork authority path, including operational seeding, highest-seam host behavior, explicit deployment schema, and provider-matrix correctness.
  - #646 handoff: performance workload and EF-oracle equality are ready for timing comparison; this task does not claim the timing verdict.
  - #647 zero-EF deletion: still blocked by later host switch/OpenIddict/EF-family removal tasks; this task preserves EF as explicit, frozen, and not coenabled with Groundwork.

### US4 T080 Exact-HEAD Review Verdict

- Review fixed point: `66cf438143679fc123c0a9da7602802f03d93bca`.
- Reviewed HEAD before remediation: `dc8dbba9`.
- Independent review findings:
  - Documentation duplication: the Groundwork provider README repeated host/deployment policy that belongs in extension-point catalogs. Remediated by reducing the README to package-local registration guidance plus canonical links.
  - Seed convergence: role, user projection, and tenant membership convergence used unconditional IAM saves. Remediated with revision-aware create-only/CAS convergence and bounded retry failure.
  - #646 workload: the public workload digest was placeholder metadata. Remediated by executing a deterministic fixed dataset through the Groundwork Identity store seam and publishing the computed digest `719708f5192bc589a05bd319468b55e62c25801b9c45b6a02c5ec4770b4a9c49`.
  - EF oracle freeze: EF seeding delegated to the new coordinator. Remediated by restoring the pre-US4 EF account seeding algorithm while keeping only the moved `IdentitySeedOptions` type shared.
- Remediation evidence:
  - Workload/oracle focused rerun: `/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj --filter "FullyQualifiedName~AspNetCoreIdentityPerformanceWorkloadTests|FullyQualifiedName~AspNetCoreIdentityTemporaryEfOracleTests" --logger "trx;LogFileName=spec095-us4-t080-workload-remediation-final.trx"` — `7` passed, `0` failed; SHA-256 `cf00cd25dbf5a754b71d397a82fc66a2a5eba09941a2b616044aef329056041f`.
  - Seed CAS rerun: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj --filter FullyQualifiedName~Two_instance_groundwork_seeding_converges_for_100_races_without_logging_the_password --logger "trx;LogFileName=spec095-us4-t080-seed-cas-remediation.trx"` — `1` passed, `0` failed; SHA-256 `9bfd0852fe211af2d1b97c8d7f13645b93daf7ecb7448dc88eb09092892a2bcb`.
  - EF/composition regression: `/usr/local/share/dotnet/dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --filter "FullyQualifiedName~AspNetCoreIdentityRegistrationTests|FullyQualifiedName~AspNetCoreIdentitySignInTests|FullyQualifiedName~EnabledShellCompositionTests" --logger "trx;LogFileName=spec095-us4-t080-ef-composition-remediation.trx"` — `17` passed, `0` failed; SHA-256 `81f3b508bf736d76a2078e77e125b65c44d2cf2a1d3d67b4209d8d031490b5de`.
  - Architecture ratchets: `/usr/local/share/dotnet/dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity" --logger "trx;LogFileName=spec095-us4-t080-architecture-remediation.trx"` — `65` passed, `0` failed; SHA-256 `dc3639c5354ca668f18c7f114d667303b50e3e4f559b20692c3b224e545e2420`.
  - Provider workload rerun: `/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj --filter "FullyQualifiedName~AspNetCoreIdentitySqliteProviderTests|FullyQualifiedName~AspNetCoreIdentitySqlServerProviderTests|FullyQualifiedName~AspNetCoreIdentityPostgreSqlProviderTests|FullyQualifiedName~AspNetCoreIdentityMongoDbProviderTests|FullyQualifiedName~AspNetCoreIdentityNativePlanTests|FullyQualifiedName~AspNetCoreIdentityPerformanceWorkloadTests" --logger "trx;LogFileName=spec095-us4-t080-provider-workload-remediation.trx"` — `11` passed, `0` failed; SHA-256 `1ca7b3e6701b412bb079d0b66d7d773dd66f603fdac8b4b88b2d31c3f899f99b`.
- Verdict: T080 accepted after remediation. Deployment-owned schema remains explicit, seed concurrency/security is conditional-write based, highest-seam/EF composition remains green, EF remains separately selectable and not coenabled with Groundwork, and #646 has an executed correctness workload handoff. Timing remains out of scope.

## 2. Prove The Red Contracts First

Before production store implementation, run the new project and capture failures for the intended missing behavior:

```bash
dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj \
  --filter 'FullyQualifiedName~Registration|FullyQualifiedName~Authority|FullyQualifiedName~Tenant|FullyQualifiedName~Concurrency|FullyQualifiedName~Relationship|FullyQualifiedName~Seeder'
```

Required red reasons:

- no Groundwork framework user/role store;
- EF plus Groundwork authority coactivates instead of failing;
- normalized lookup is global rather than tenant-bound;
- legacy user/role/external documents are a second authority;
- write paths are unconditional upserts;
- link/delete transitions cannot prove one atomic outcome;
- concurrent seeding can split user/role state.

Infrastructure/setup failures are not accepted as the red baseline.

## 3. Run Unit And Registration Gates

```bash
dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj
dotnet test tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj
```

The direct suite must cover every advertised Identity interface, every store/coordinator branch, infrastructure-exception translation, cancellation, defaults, duplicate classification, stale revision, uncertain commit, and DI lifetime/registration path. It must also prove queryable, passkey, and protected-user interfaces do not resolve.

## 4. Run Architecture And Authority Gates

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  --filter 'FullyQualifiedName~GroundworkPersistence|FullyQualifiedName~EfCoreSurface|FullyQualifiedName~Identity'
```

Expected outcomes:

- one #644 authority for user, role, and external login;
- no Groundwork dependency in Identity Abstractions;
- no new EF source/package/project/test edge;
- no unconditional unified-provider Identity replacement;
- all logic-bearing services scoped unless a documented static-value/lifecycle exception applies;
- no load-all/client evaluation;
- exact behavioral baseline identities retained.

## 5. Run SQLite Contract And Highest-Seam Scenarios

```bash
dotnet test tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj \
  --filter 'Category=Sqlite|Category=HighestSeam'
```

The shared scenario must cover:

1. complete user/role capability surface;
2. same normalized identities across tenants;
3. duplicate and stale races from independent clients;
4. claim/login/role/token/membership atomicity and delete-with-dependents;
5. lockout concurrency;
6. ambiguous-email failure and unique-email reservation;
7. two-instance seeding and secret-safe logs;
8. login -> cookie -> claims -> protected endpoint;
9. dispose/reopen;
10. child-process restart.

## 6. Run The Four-Provider Matrix

Start the reusable provider collections once and reset between cases. Do not create one container per test.

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --filter 'Category=AspNetCoreIdentityProvider'
```

Required providers:

- file-backed SQLite;
- SQL Server Testcontainer;
- PostgreSQL Testcontainer;
- MongoDB replica set with two independently constructed clients.

Standalone MongoDB is a negative admission case. Every passing provider records an identical sanitized result digest plus native plan evidence for normalized username, normalized email, normalized role, external login, users-for-claim, and users-in-role.

## 7. Validate The Deployment Schema Contract

Build the public parameterless deployment source that matches the explicitly selected Groundwork Identity feature. Identity is intentionally absent from the provider substrate schema and included by the T065 Identity deployment-schema variant. Then run:

```bash
dotnet groundwork validate \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesWithIdentityDeploymentSchema \
  --provider <sqlite|sqlserver|postgresql|mongodb> \
  --offline \
  --output json

dotnet groundwork plan \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesWithIdentityDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork status \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesWithIdentityDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork apply \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesWithIdentityDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --safe \
  --output json
```

For MongoDB, include `--database <name>` if the replica-set URI has no database path. `validate`, `plan`, and `status` are read-only. Runtime admission must report the same target fingerprint and must fail on pending/drifted schema without applying it.

## 8. Produce The #646 Correctness Handoff

Run the `iam-normalized-lookup-update` workload through the public manager/store seam on the fixed dataset and capture:

- the mechanically derived input fingerprint for one canonical user, 16 noise users, one role, and one user-role link;
- the exact observable query/mutation sequence and result digest;
- the actual Groundwork provider identity derived from its validated provider descriptor;
- real provider-native evidence for all five required routes, captured against exactly 100,000 physical records with one materialized candidate;
- mandatory SQLite execution and the same entry path for SQL Server, PostgreSQL, and MongoDB under `ELSA_RUN_IDENTITY_NATIVE_PLAN_PROVIDERS=1`;
- a frozen, explicitly non-executed EF contract baseline; #646 owns real same-provider EF execution and equality;
- no timing in this work unit.

The accepted `preview.60` exact-candidate generation establishes the #644 external-authority handoff for spec 094's
`iam-user`, `iam-role`, `iam-external-identity`, and `iam-tenant-membership` rows. Do not copy these omnibus artifacts
into spec 094's per-obligation provider-evidence arrays; those remain empty until their own catalog-bound evidence
exists. External-authority rows advance only to the state justified by #644; performance and final readiness remain
incomplete until #646/#647.

## 9. Full Candidate Validation

```bash
dotnet build Elsa.Server.slnx -c Release
dotnet test Elsa.Server.slnx -c Release --no-build
dotnet pack Elsa.Server.slnx -c Release --no-build
git diff --check
```

Also run the Groundwork fast/provider CI commands from spec 094 and inspect dependency graphs for new EF or Groundwork-to-core leaks.

### Historical T083 Preview.56 Candidate Validation

- Upstream dependency:
  - Groundwork PR `valence-works/groundwork#86` merged as `093cc124ce5d021fa750b7c0a156a7c6c5bedf3a`.
  - Publish workflow run `29486875084` completed successfully and produced aligned Groundwork packages/tool `0.0.1-preview.56`.
  - The repository-local tool restored as `Groundwork.Tool 0.0.1-preview.56`; the package/tool coverage ledger and four Identity evidence artifacts were historically reported as reconciled to the same version. T084 later withdrew their active acceptance linkage.
- Build and packaging:
  - Serial Release build: `0` errors, `1` existing `NU1510` warning; `38.49s` wall time.
  - Release pack: completed successfully in `30.42s`; existing readme/prerelease-dependency warnings remain outside this unit.
  - `dotnet format analyzers ... --verify-no-changes` and `git diff --check`: passed. A whole-solution whitespace-only check still reports inherited formatting debt outside this candidate and is not used as the analyzer gate.
- Identity and architecture suites:
  - Foundation Identity Persistence Groundwork: `37/37` passed (`31.77s` wall including build).
  - ASP.NET Core Identity Groundwork: `65/65` passed (`105.16s` wall including build).
  - Foundation Identity: `123/123` passed (`44.33s` wall including build).
  - Architecture: `201/201` passed (`62.52s` wall); the Groundwork coverage-ledger subset was `51/51`.
  - Provider/shell schema-selection remediation: `42/42` passed across real CShell activation, UnifiedHost, PostgreSQL UnifiedHost, MongoDB registration, and SQL Server registration.
  - Composition boundary after relocating the six-family helper to `ReferenceComposition`: `44/44` passed.
- Provider, schema, and restart evidence:
  - Complete conformance suite on preview.56: `52/52` passed in `172.27s` wall. The TRX SHA-256 is `a2cab2e1b2bb3d92816788cebfda1d36a64d00faf344577547681a2b9a4a4556`.
  - Explicit four-provider Identity/restart matrix: `4/4` passed in `67.54s` wall; SQLite, SQL Server, PostgreSQL, and MongoDB each exercised the provider-native scenario and restart probe.
  - Groundwork schema CLI plus Identity schema CLI: `11/11` passed in `10.56s` wall.
  - Final serial full-solution run: `4,491/4,491` passed across `63` projects in `694.92s` wall. An earlier parallel run exposed one unrelated lease-renewal timing failure; its complete owning project passed `920/920`, the exact test passed three consecutive isolated reruns, and it passed in the final serial solution run.
- Generated maps:
  - Architecture reference, feature dependency, extension point, package/project/test maps, and the generated-map freshness manifest were regenerated after that project-boundary change. This was not a Groundwork storage-manifest map.
  - `docs/reports/maps-v2-findings.md` reports no missing extension catalogs and no new runtime/design direct-reference signal; the provider-leaf family references collapsed into the existing `ReferenceComposition` bridge as intended.

### T084 Independent-Audit Correction And T085 Entry Gate

The exact T083 commands, counts, durations, and hashes above remain historical execution provenance. T084 invalidated the following acceptance claims derived from those green runs:

- **FR-013 / SC-003**: the four provider tests were separate narrow scenarios. They did not execute one provider-independent tenancy/concurrency/atomicity/cancellation/failure-window/lifecycle catalog, calculate `AspNetCoreIdentityScenarioResult`, or compare complete fresh/reopen/process-restart digests.
- **FR-018 / SC-001**: Groundwork highest-seam coverage did not include token exchange, real logout, or positive refresh, and the complete advertised capability catalog did not run through framework managers.
- **FR-020 / SC-007**: `AspNetCoreIdentityPerformanceWorkload` executed an in-memory Groundwork store. `AspNetCoreIdentityTemporaryEfOracle` returned fixed expected metadata and was not a live EF execution. It is therefore a contract snapshot, not EF-equivalence evidence; #646 owns the live comparison before timing.
- **SC-004**: three in-memory tenant tests did not cover every physical load/query/write/delete/relationship/unit-of-work path on four providers.
- **SC-005**: the native-route test substituted `100,000` as `TotalCount` over a small in-memory dataset. Provider plans covered normalized user name only, and three declared routes were not runtime-observed.
- **SC-009**: existing ratchets only partially cover the required intentional-reintroduction cases; exact-path load-all and unsupported-capability gates remain open.
- **SC-010**: Identity schema CLI/runtime parity and read-only checks were SQLite-only and did not independently hash provider state before and after operations.
- **Durable evidence**: `evidence/providers/matrix.md` was the only checked-in spec-095 provider artifact despite the historical four-artifact wording.

T084 remediation replaces the in-memory/fixed-metadata handoff described above. The parameterless workload path no
longer exists: `AspNetCoreIdentityPerformanceWorkload` requires a validated physical-provider target and consumes
native plan evidence from the current physical provider-driver route-plan path. The five route records must identify the selected
provider, prove both predicates, preserve the ratified finite limits, report exactly 100,000 physical records, and
materialize exactly one candidate. The v1.1.0 input fingerprint is
`5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9`; its observable result digest is
`32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc`. `AspNetCoreIdentityEfContractBaseline`
is metadata-only and cannot execute EF. These are workload-contract values, not current four-provider acceptance or EF-equality evidence; #646 still owns live EF comparison and every timing claim.

The first bounded T085 slice replaced the four copied provider scenario bodies with one executable objective catalog and shared physical runner. At that intermediate point its exact-set denominator covered tenancy, duplicate-create concurrency, pre-cancellation, successful relationship cleanup after delete, close/reopen, process-restart, and advertised-capability objectives. Atomicity and failure-window objectives were still deferred, and the slice did not close the remaining complete FR-013 catalog, 100,000-record/native-plan, highest-seam HTTP, complete cross-tenant, architecture-gate, performance, or schema-parity gaps. This paragraph is intermediate provenance; later remediation superseded that denominator.

Historical pre-candidate evidence for that first bounded T085 slice:

- Exact-set catalog contract: `AspNetCoreIdentityProviderAcceptanceCatalogTests` — `3` passed, `0` failed.
- Shared physical runner matrix: `AspNetCoreIdentitySqliteProviderTests`, `AspNetCoreIdentitySqlServerProviderTests`, `AspNetCoreIdentityPostgreSqlProviderTests`, and `AspNetCoreIdentityMongoDbProviderTests` — `4` passed, `0` failed in `7m27s`.
- Matrix command: `dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~AspNetCoreIdentitySqliteProviderTests|FullyQualifiedName~AspNetCoreIdentitySqlServerProviderTests|FullyQualifiedName~AspNetCoreIdentityPostgreSqlProviderTests|FullyQualifiedName~AspNetCoreIdentityMongoDbProviderTests"`.
- The runner retains `512` plan-smoke noise users from the historical provider scenarios. That setup is not described or counted as 100,000-record evidence.
- This local run has no durable provider artifacts or exact candidate commit yet and must be repeated during T083.

### Preview.60 Exact-Head Acceptance

The current implementation candidate targets Groundwork `0.0.1-preview.60` and Identity manifest `1.0.4`. Groundwork PR #88 supplies the generic version-aware codec contract and PR #89 supplies provider-native ordered bounded-query explanations; Elsa-specific policies and concrete upcasters remain behind the Elsa marker in Elsa provider packages. The shared provider catalog now requires 25 objectives, covers all 15 advertised framework capabilities, and has no deferred objective. The implementation also contains deterministic physical failure injection, lost-acknowledgement mutation-receipt reconciliation, bounded expired-receipt cleanup, real 100,000-record native-route paths for the exact 10-route production denominator, highest-seam HTTP acceptance, and four-provider schema-parity/read-only machinery.

Preview.59 and earlier provider and SC-010 runs are retained as historical regression evidence because the dependency advanced again before ratification. No preview.55-preview.59 run is a final exact-head artifact for this candidate. The preview.60 run retained:

- one sanitized provider artifact per SQLite, SQL Server, PostgreSQL, and MongoDB replica-set entry point;
- the exact candidate SHA, package/tool version, topology, current composition/target fingerprint, and complete 25-objective lifecycle digest;
- native plans for all 10 required live routes at exactly 100,000 physical records with one materialized candidate;
- schema CLI/runtime parity and independent read-only before/after hashes; and
- re-derived v1.1 workload hashes. If they remain `5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9` and `32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc`, retain them as Groundwork workload-contract evidence only.

The clean evidence candidate is commit `1aed4f5989b9aed0ddb9837a61597d4cb584fbaa`, tree
`00f5f518c79429dcd1e175ca71e38e719004dc65`. The all-provider generator passed `1/1` in `2m48s`
and installed immutable generation
`4c541bf48f087c5073dd4f39a88bdce542651e2e6453d9e3d060c951e93a1f9f`. `current.json` is the sole
reader entry point and has SHA-256
`ea0aaa6f922489148827702a0020896feeb3d1c741ec6ba956b9d6d2cb048e7c`; it is atomically replaced
only after the complete generation directory is installed. Fault-injection tests passed at all three
publication boundaries.

| Provider | Native classification | Artifact SHA-256 |
| --- | --- | --- |
| SQLite | `index-search` | `5aebbebce5d329a91c783c9301ea4ef8a30eccbb52d07d4e95172d62e08cf431` |
| SQL Server | `index-seek` | `dd02dbdcb21d9b471787de5bb908b8c2fcefe2c8fc3826fb9852a7c909050ac4` |
| PostgreSQL | `indexed` | `896275d2497e73ab7aad07d99ce5b991d11dfe2caf861a0400f98b807b328f9d` |
| MongoDB | `index-scan` | `7d72bd632825dd6178f563379cb66756daf2382c1886cb51e04b6294eab878b6` |

Each bundle records the exact `25`-objective catalog, all `15` advertised capabilities, external
process confirmation, `10` native routes, `6` physical tables, `100,000` rows per table, and `7`
schema receipts. All four reproduce workload input fingerprint
`5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9` and result digest
`32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc`. The checked-in semantic
and Draft 2020-12 schema validator passed `1/1` in `505ms` on the committed evidence head. A forbidden-property scan found no raw
native plans, runtime invocation fingerprints, route values, storage scopes, candidate content,
connection strings, or process identifiers.

Generation command:

```bash
ELSA_GENERATE_IDENTITY_PROVIDER_EVIDENCE=1 \
  dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  -c Release \
  --filter FullyQualifiedName~Generate_all_preview60_provider_artifacts_only_after_the_complete_matrix_passes
```

Checked-in validation command:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  -c Release --no-build --nologo \
  --filter FullyQualifiedName~Checked_in_provider_artifacts_are_complete_sanitized_and_share_one_tested_code_candidate
```

Original T083 full validation ran against candidate `26a79a88dbe96fa0647b21f4305aca146c76f191`; the
Linux Unicode compatibility remediation produced a preview.59 successor candidate, and the mainline
preview.60 merge produced accepted successor candidate
`1aed4f5989b9aed0ddb9837a61597d4cb584fbaa` after repeating every affected gate:

- `dotnet format analyzers Elsa.Server.slnx --verify-no-changes --no-restore --severity warn --verbosity minimal`: passed with no changes.
- Release solution build: `0` errors and `81` warnings on the full rebuild; the warnings are existing Groundwork obsolete-API diagnostics.
- Full serial Release solution test: `64` test assemblies, `5,135` passed, `7` expected opt-in/environment-gated skips, `0` failed, `5,142` total in approximately `11m`.
- Release pack rerun: passed in `41s` with `0` errors and the one expected non-packable `Elsa.Server.csproj` warning.
- Architecture suite: `225/225` passed; EF surface ratchet: `25/25` passed.
- Groundwork Identity suite: `133/133` passed; mixed Identity integration suite: `132/132` passed; Identity persistence Groundwork suite: `43/43` passed; router suite: `3/3` passed.
- Conformance suite before installing checked-in artifacts: `74` passed and `7` expected opt-in skips in `4m27s`.
- Unicode hotfix verification: Linux .NET 10 container `14/14` in `168ms`, Identity persistence `45/45`, Groundwork Identity `133/133` in `1m33s`, architecture `225/225` in `1m17s`, and focused analyzers passed; independent review found no blocker.
- Publication fault matrix: `3/3` passed; successor all-provider evidence publisher: `1/1` passed in `2m48s`; checked-in semantic/schema validator: `1/1` passed on the final docs/evidence worktree.
- `git diff --check` and the precise forbidden-property scan: passed.

The independent audit found six evidence-integrity blockers: inert JSON Schema validation,
self-reported route denominators, loose provider identity/topology claims, loose schema-receipt
chains, an insufficient candidate-SHA guard, and non-atomic publication. T085 remediated all six.
The second independent review found no remaining blocker, including at all three injected
publication failure boundaries.

The frozen EF source-tree baseline proves that the separately selectable EF implementation did not change. `AspNetCoreIdentityEfContractBaseline` remains non-executed; #646 must execute EF and establish equality before any timing comparison.

## 10. Landing Evidence

Model B draft PR [#694](https://github.com/elsa-workflows/elsa-foundation/pull/694) targets `main` from
`codex/095-groundwork-aspnetcore-identity`. Its description links #644/#629, records the immutable
four-provider generation and local validation, and keeps #646, #643, and #647 explicit as remaining
program gates.

Before promoting the Model B draft PR:

- independent review every FR/SC and test-objective row against exact HEAD;
- record focused/full/provider/CLI counts and durations;
- attach four-provider sanitized digests and native plans;
- record that the frozen EF implementation tree was unchanged and never coenabled, and that the checked-in EF contract baseline was not executed;
- verify required GitHub checks;
- merge only the reviewed candidate and confirm remote `main` contains it.

Completion of #644 does not complete the zero-EF program. #646 must accept the performance verdict, #643 must replace OpenIddict EF, and #647 must then switch hosts and delete the frozen EF family.
