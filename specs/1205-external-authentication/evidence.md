# Delivery evidence

Date: 2026-11-04. Starting Foundation main: `73040cd7b1368818820d2a60902443a9f8a82c7f`. Own worktree/branch: `external-auth-3579/elsa-foundation`, `agent/codex/3579-external-auth`.

## State

Incomplete delivery. Draft PR #3806 implements identity isolation, exact links/atomic admission, managed catalog/protocol/flow components and backend HTTP cookie/token/preview integration. Studio, cross-IAM/link mutation integration, durable selected-session/status enforcement, browser/external-product/server-database/separate-process deployment qualification, review/merge and resulting-main proof remain open. The considered owner answer selects provisioning policy only: assign saved existing Elsa roles at user creation and omit recurring external group-to-role mappings for now. Provisioning policies assign saved existing Elsa roles; upstream roles/permissions have no automatic authority.

Claims: #3579 comment 6627512912; prerequisite #2848 comment 6627557183. No competing claim/assignment/open PR found before this checkpoint. Owner authorization is recorded on #3579 comment 6627537005 and linked on #2771; #2771's Phase 2 description now names this scoped advancement without activating other phases. Baseline main CI 41790684918 passed; no open main-red CI issue was found at startup.

## Identity-isolation implementation

- Typed `TokenSubject`: tenant/user or tenant/provider/validated-issuer/opaque-subject.
- Domain-separated length-framed hash for token storage/revocation. External first-party JWT `sub` is scoped too; canonical users keep their Elsa user ID and explicit canonical user claim.
- Issuance, refresh and logout share typed scope. Malformed/legacy/mismatched refresh payloads are refused.
- Canonical projection replaces upstream NameIdentifier/sub/tenant rather than appending identifiers.
- External-bearer normalization strips forged origin/key claims and stamps validated provider/issuer.
- First-party bearer validation requires its signed full-scope key. This deliberately refuses old unscoped access tokens after upgrade, avoiding live old tokens that scoped logout cannot revoke.
- External subjects equal to local user IDs cannot rotate that local user's cookie stamp.
- Updated tracked public API and callers. This is a pre-GA source-breaking token contract change; no compatibility overload accepts a bare revocation string.

## Completed local runs

1. Focused Identity suites: **passed 128/128**, no skips (before later payload/validation additions).
2. Complete Identity suite: **passed 572/572**, then **577/577** after malformed/legacy payload coverage, no skips.
3. Focused token/access-scope validation: **passed 47/47**, no skips, including cryptographically valid tokens with missing/wrong scope keys.
4. Bite-proof mutation: temporarily replace only `TokenSubjectKey.Create` with `subject.SubjectId`. The scope-isolation and real HTTP logout/refresh regressions **both fail** because a different identity loses access. The exact scoped source was restored in `finally`; restored complete suite **passed 579/579**, no skips. This proves the tests detect the collision rather than only exercising token creation. Final complete run after adding explicit untrusted/multiple-identity resolution, scoped access-token validation and canonical-user cross-tenant isolation: **passed 581/581**, zero failed/skipped, 8 seconds reported test duration.

Commands (run through the normal build-slot PATH):

```sh
dotnet test Essentials/tests/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore --verbosity minimal
dotnet test Essentials/tests/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore --filter 'FullyQualifiedName~Scoped_revocation_preserves_equal_subjects|FullyQualifiedName~Http_logout_of_external_subject' --verbosity minimal
```

The complete suite includes cookie/token exchange/logout behavior, real JWT-handler normalization, security and API registration tests. It is not an actual interactive provider sign-in journey. Initial fresh-worktree `--no-restore` failed for missing assets; restore fixed it. Intermediate compilation failures from the new contract's import/API baseline were corrected before passing runs; they are not counted as passed tests. Existing unrelated runtime API analyzer warnings remain visible in build output.

## Outstanding acceptance

Identity/admission, catalog discovery and runtime recovery adapters, protocol/preview/session/API/Studio work and actual qualification remain open, as do review/merge/current required CI/packed consumers/resulting-main proof. User disable/delete and durable selected-session enforcement must integrate the same typed identity; the existing low-level status write alone does not deliver that acceptance. #2848 and #3579 remain open.


## Connection ownership/catalog checkpoint

Implemented in the optional `Identity/ExternalAuthentication` Core/feature pair: detached live selection,
explicit full override lifecycle, whole-catalog provider-configuration CAS, host-tenant management and role
boundaries, separate configuration readiness/recovery eligibility, conservative final-path protection,
non-mutating change assessment and startup dependency checks. Base Identity.Core acquires no Secrets dependency.
The catalog uses the existing provider-configuration schema; no new EF provider authority or schema is added.

Fifteen focused tests passed without skips. The native SQLite tests reopen the store after provider disposal,
read updates from another independent service provider, prove stale-write conflict, preserve caller tenant scope,
and synchronize two independently composed writers in the same process after their reads. Only one disable wins; a fresh retry cannot remove
the remaining path. Protocol/recovery eligibility is a controlled collaborator in these component tests, not a
real provider/user/session proof. Readiness/eligible recovery adapters remain to be implemented and qualified.

`dotnet run --project tools/maps/Elsa.Maps.Generator -- check` passed: committed filters still describe the graph.
The complete Identity suite passed **596/596**, zero failed/skipped. Explicit-shadow mutation (allowing any
same-key stored record to shadow) fails the lifecycle regression. Removing the final-path rejection makes
**five** protection tests fail, including the shared-SQLite writer retry. Both exact sources were restored;
the complete 596-test run is the restored result. Architecture-project restore also passed after adding the
optional feature reference; its full architecture suite has not yet run on this checkpoint.

Commands:

```sh
dotnet test Essentials/tests/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore --verbosity minimal
dotnet restore Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --verbosity minimal
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Logs: `/tmp/elsa-3579-auto-shadow-final.log`, `/tmp/elsa-3579-recovery-final.log`,
`/tmp/elsa-3579-catalog-final.log`, `/tmp/elsa-3579-architecture-restore.log`,
`/tmp/elsa-3579-maps-updated-graph.log`. These are component results, not actual two-server qualification.

The source assessment exposes complete proposed effective state without saving. A stale confirmation rejects
at catalog revision checking. Source/presentation/settings/secret/policy transitions never merge fields.
This assessment is distinct from the pending real interactive sign-in preview.

Draft PR: https://github.com/elsa-workflows/elsa-foundation/pull/3806 (Codex app, retained as draft). Startup GitHub
publication returned errors, including an HTTP 502 after creation; authoritative read confirmed PR #3806 rather
than retrying into a duplicate. No merge/release/deployment action was taken.


## Qualified external-link checkpoint

`ExternalIdentityKey` carries tenant/logical connection and exact validated issuer/subject. Existing
issuer-less links stay in a separate legacy namespace; qualified lookups never fall back. EF keys and unique
subject lookup hashes are domain-separated and length-framed without normalizing issuer or subject. Shared
user registries, revision-controlled rebind and aggregate deletion handle qualified links. The in-memory
store also refuses an unconditional attempt to steal another user's link. `IUserManager` accepts an explicit
issuer without changing saved user roles.

All four IAM provider histories retain their original Initial migrations and add the nullable issuer column.
IAM schema 1.1.0 reads existing 1.0.0 content through an identity upcaster; a committed UserEntity fixture pair
and the standard three upcaster proofs exercise the real SQLite user-store read path. Native SQLite verifies
migration down to the original schema and back with existing legacy users/links, qualified-link restart,
legacy framework isolation, revision rebind and aggregate deletion. Ten new Identity cases pass (including
three fixture proofs). An intentionally case-folded issuer/subject hash makes both focused SQLite isolation
regressions fail; exact sources were restored and the full suite passed **606/606**, no failures/skips.
IAM EF tests passed **206/206**. Offline provider tests passed **9/9**, including migration snapshot/model
consistency and SQL generation for SQL Server, PostgreSQL and MySQL. This does not claim native execution on
those three servers, atomic new-user admission, actual provider sign-in or multi-node runtime qualification.

Logs: `/tmp/elsa-3579-exact-final.log`, `/tmp/elsa-3579-exact-case-mutant.log`,
`/tmp/elsa-3579-iam-tests.log`, `/tmp/elsa-3579-provider-model-updated.log`.
The first fixture attempt used ordinary strings instead of the IAM codec's UTF-16/base64 set representation;
that fixture failure was corrected before the passing proof run. Intermediate public-API/test compilation
errors were corrected and are not counted as passed runs.

Publication remains separate from local verification: normal app-authenticated pushes of the catalog commit
have returned GitHub server errors; authoritative remote reads still showed the earlier draft head at the
last check. No alternate credentials or branch-rule bypass was used.


## Atomic admission checkpoint

Added optional `IExternalIdentityStore.AdmitAsync` with a typed policy-assessed request. The EF implementation
uses the existing user/relationship authority mutations inside one outer transaction and receipt; it does not
introduce another IAM store. Child user-create receipt identity is scoped to that outer invocation. New external
users have a fresh security stamp and no local password. Existing-user linking checks the active user snapshot
and preserves saved roles. The in-memory authority commits the equivalent operation under its shared lock.

Five new tests passed. Two tasks start together against independent SQLite contexts sharing one owned database;
both return one canonical owner and only one user, link and name reservation persist. Missing default roles,
pre-existing user IDs, stale matched-user snapshots and disabled users refuse. Injected failure at the link insert
rolls back the already-flushed new user, reservation and child receipts. Removing the outer transaction makes
that regression fail with an orphan user; restoring it yields **611/611** passing Identity tests, zero skips.

The initial concurrency fixture tried to synchronize reads inside SQLite's writer transaction and timed out;
it was corrected to synchronize request starts before transaction entry. This is component concurrency evidence,
not two-server actual-provider qualification. Provisioning policy assessment, configured matching and the broker
still have to call this mutation through the authorized admission path; no new runtime sign-in claim is made.
Logs: `/tmp/elsa-3579-admission-focused-fixed.log`, `/tmp/elsa-3579-admission-atomic-mutant.log`,
`/tmp/elsa-3579-admission-final.log`. The IAM regression rerun passed **206/206**, no failures/skips
(`/tmp/elsa-3579-admission-iam.log`).

The earlier GitHub server push failures cleared on a later normal app-authenticated push: draft PR #3806
was verified at `436ee191b`. This supersedes the earlier publication limitation; no credentials or branch
rules were changed. Atomic admission is a later local checkpoint until its own push is verified.


## Provisioning policy and canonical projection checkpoint

Implemented pure `IExternalAdmissionService.AssessAsync` and separate actual admission with effective source,
enablement and material rechecks. Unknown identities are rejected by default. Explicit `jit` selects existing
Elsa default roles; explicit `match`/`verified-email` requires unique verified boolean evidence and an eligible
Foundation-managed target. Integrators register a uniquely named read-only `IExternalProvisioningPolicy` selecting
existing users or a subset of declared role targets. Host/editor role boundaries also constrain matching against
privileged users. Upstream role/permission claims never project automatically. The mapped-role choice remains open.

Admission uses a fresh owned tenant scope without rebinding the caller. Qualified/atomic authority capabilities
are explicit; read-only authorities can resolve existing links without promising JIT. Matching passes a frozen
tenant-membership snapshot into the atomic store to close changes that do not alter UserRecord fields. Both EF
and in-memory authorities check that snapshot. The old principal factory's automatic creation and raw-claim import
were retired: its pre-GA context is now canonical `(TenantId, UserId, Provider?)`, and it refuses unavailable users.

Final complete Identity run passed **636/636**, no failures/skips. New component cases cover pure preview (zero
writes), JIT and repeated sign-in, equal email at two providers, verified-email evidence/ambiguity/eligibility,
ownership/capability denial, custom policy selection and role escalation, matching privilege bounds, membership
races, disabled/stale connections, proof scope, owned scopes and inactive linked identities. A real SQLite composed
service verifies preview without user creation, actual atomic admission, dispose/reopen and canonical permission
projection. This is not an actual external-provider or HTTP cookie/token/session witness.

Three deliberate mutations each fail their focused regression: making reject fall through to JIT, removing the
new-user role-subset boundary, and ignoring the EF membership snapshot. Exact sources were restored before the
complete passing run. The early compile attempts caught new public API tracking and a test scope-property typo;
those were fixed and not counted as passed tests. All remaining protocol/session/API/Studio/deployment and merge/
resulting-main requirements remain open. No existing host/store was changed.

Logs: `/tmp/elsa-3579-policy-capabilities-final.log`, `/tmp/elsa-3579-policy-integration.log`,
`/tmp/elsa-3579-policy-mutant-default-jit.log`, `/tmp/elsa-3579-policy-mutant-role-boundary.log`,
`/tmp/elsa-3579-policy-mutant-membership.log`. The affected IAM EF suite passed **206/206**, no failures/skips
(`/tmp/elsa-3579-policy-iam.log`). The solution-filter check passed (`/tmp/elsa-3579-policy-maps.log`).


## Managed OIDC protocol checkpoint

Added optional `FoundationIdentityExternalOidc` and named protocol contracts without adding managed dependencies
to the direct-bearer module. Local settings/secret checks, bounded discovery/JWKS tests, code/S256 challenge,
client-secret basic/post or public client redemption, and signature/issuer/audience/lifetime/nonce validation are
implemented. Secret references resolve at point of use in an owned tenant scope; a protected-flow fingerprint
pins both metadata generation and actual value. Upstream tokens/error descriptions are not retained/returned.
Endpoint origin, HTTPS, redirects, JSON size/depth and total deadlines are constrained; Development-only loopback
has activation and point-of-use guards. Unknown-kid rotation retries trusted keys once without re-redeeming a code.

The owned controlled Kestrel provider fixture uses actual HTTP, registered client authentication, single-use
PKCE codes and RSA-signed ID tokens. **32** new protocol cases pass. They cover basic/post/public modes, replay,
wrong verifier, signing-key rotation (including rotation between JWKS and token response), issuer/audience/nonce/
signature/expiry/authorized-party/type/duplicate/malformed evidence, secret rotation and unversioned value changes,
response issuer, unsupported metadata, remote origin/redirect constraints, body/deadline limits and environment guards.
Restored complete Identity suite passed **668/668**, zero failures/skips (9 seconds reported test duration).

Nonce-binding mutation accepts the wrong-nonce response and fails its regression; the same deliberate mutant's
invalid lookup also fails the missing-nonce error-path assertion. Ignoring exact issuer validation and ignoring
secret-generation binding each fail their focused regression. Source restoration preceded the final passing run.
The initial protocol run failed because the fixture's token method selected RequestDelegate and discarded its
IResult; an explicit route-handler cast fixed the fixture. Those failed fixture runs are not counted as passes.

Logs: `/tmp/elsa-3579-managed-oidc-focused-fixed.log`, `/tmp/elsa-3579-managed-oidc-expanded.log`,
`/tmp/elsa-3579-oidc-mutant-nonce.log`, `/tmp/elsa-3579-oidc-mutant-issuer.log`,
`/tmp/elsa-3579-oidc-mutant-secret.log`, `/tmp/elsa-3579-managed-oidc-rotation-final.log`.
Architecture restore passed after adding the optional module reference; the solution-filter graph check passed.
Restore reported existing NU1903 for `System.Security.Cryptography.Pkcs` 9.0.13 (GHSA-846f-f764-76mx) in the broad
architecture graph. The pre-change lock already contains that exact version and the new managed-OIDC module's
lock does not contain Pkcs; no unrelated dependency was upgraded here. Logs:
`/tmp/elsa-3579-managed-oidc-architecture-restore.log`, `/tmp/elsa-3579-managed-oidc-maps.log`.

Limitations: protocol contribution only. Global connection readiness/recovery still needs the session/broker/IAM
adapters, and no actual Elsa callback/browser, interactive preview, cookie/access/refresh session, durable restart,
two-node, external-product, Studio, required review/merge or resulting-main acceptance is claimed here.


## Durable flow and preview artifact checkpoint

T018 now has an optional `Identity.ExternalAuthentication` EF module, separate from IAM/provider configuration.
Its `IdentityExternalAuthentication` schema family starts at `1.0.0`, with new migration histories/snapshots for
SQLite, SQL Server, PostgreSQL and MySQL. No existing Initial migration/history was regenerated. The narrowly
filtered generation command was:

```sh
ELSA_EF_CONFIGURATION=Debug bash tools/ef/generate-module-migrations.sh 'ExternalAuthentication(Sqlite|SqlServer|PostgreSql|MySql)DbContext'
```

The store requires scoped tenant access, hashed ID/bindings, bounded lifetime/payload, readable schema and an
independently committed operation. Wrong tenant/purpose/browser/administrator and expired artifacts cannot
consume the legitimate row. Conditional versioned deletion acknowledges one consumer. EF/ambient caller
transactions are refused. Bounded cleanup deletes at most 64 expired current-schema rows of its own tenant,
preserving foreign tenants and future-schema rows. The module is explicitly admitted to the Identity EF graph
with only the provider-neutral four-package EF/Relational closure, and enrolled in the shared migration,
content, history and ordinal comparison catalogs.

The managed flow service generates cryptographic state/nonce/verifier; derives exact callback URLs only from
trusted host configuration (including path base); validates return destinations at begin and callback; protects
state with the host's shared Data Protection keys/application name; pins source record/material/issuer/secret;
and consumes before code exchange. Missing authority and non-durable production authority refuse. Normal
completion delegates to existing atomic IAM admission and returns server-only user/provenance for the future
session adapter. Preview authorizes its initiating canonical administrator at every step, can select disabled or
shadowed unarchived drafts, runs only pure assessment, and returns a separately protected one-use bounded
result. Observation material changes produce a stale flag. Raw upstream roles do not grant Elsa permissions.

Tests use owned SQLite files and owned Data Protection key directories, plus the existing actual HTTP OIDC
fixture (registered exact callbacks, single-use code, PKCE/client authentication, signed tokens). One service
provider starts a flow and is disposed; an independent provider sharing the database/key ring completes it;
another restart rejects replay without another token request. A synchronized two-consumer test proves both read
the row before competing to delete it. A disabled non-effective stored draft previews across nodes, returns the
prospective existing Elsa role permissions, and leaves zero users/links. Result access is administrator/browser/
purpose-bound, expires, is single-use and detects material changes. Stored flow and preview payloads expose no
raw handles, browser bindings, nonce/verifier/credential binding or tested identity/profile/permission values.
Wrong keys/application discriminator and altered protected payload fail closed before exchange. Secret rotation,
source override, material/enable changes, expiry, changed callback/return policy and provider errors deny without
provisioning. These are service-provider/restart component proofs, not separate-process or browser/cookie proof.

Results:

- First focused flow/store/protocol run: 53 passed, four failed. Fixtures used a configured deployment record ID
  rather than the derived `deployment:company`, simulated a source transition by changing that ignored ID, and
  expected the wrong schema-skew exception type. Corrected fixture inputs; the actual source transition now
  promotes a persisted explicit override. Restored focused run: **57/57 passed**, zero skipped.
- Extended full Identity run after key-ring/application and missing/non-durable authority cases and restored
  mutation: **696/696 passed**, zero failures/skips, 12 seconds reported test duration.
- Conditional-consume mutation: keep the delete but remove the requirement that it affected exactly one row.
  The deliberately synchronized two-consumer regression fails with **two acknowledged artifacts**. Exact source
  restored in `finally` before the 696-test passing run.
- Initial focused architecture run: 34 passed, 24 failed because this worktree lacked the guard's separately
  evaluated Debug restore assets. Ran `tools/architecture/restore-ci-project-graph.sh` for Release/isolated Debug.
  Restored EF dependency/schema-stamp checks: **58/58 passed**, zero skipped. Core implementation-shape guard:
  **1/1 passed**, zero skipped.
- First shared migration/model/content/history/ordinal run: 59 passed, six failed because the new collating module
  was absent from the migration assertion's separate decision list. Added the explicit module and its five hash/
  version binding columns plus the non-collated protected payload. Restored run: **65/65 passed**, zero skipped.
  This includes four-provider model/snapshot agreement and native SQLite installation of every enrolled module
  into one shared fresh database with separate histories. SQL Server/PostgreSQL/MySQL checks here are offline
  model/migration checks; no new native-engine sign-in claim is made.
- Generated solution-filter check passed. Existing broad-graph dependency audit warnings (`Pkcs` 9.0.13 and
  `MySqlConnector` 2.3.5) remain visible; no unrelated package upgrade was performed. Generated Initial files
  retain EF's ordinary namespace style warnings.

```sh
dotnet test Essentials/tests/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore --verbosity quiet
bash tools/architecture/restore-ci-project-graph.sh --verbosity quiet
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore --filter 'FullyQualifiedName~EfSchemaVersionStampGuardTests|FullyQualifiedName~EfCoreDependencyGuardTests' --logger 'console;verbosity=normal'
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Core_projects_contain_no_implementation_shaped_types' --verbosity quiet
dotnet test Essentials/tests/Persistence/EntityFrameworkCore/Migrations/Tests/Elsa.Persistence.EntityFrameworkCore.Migrations.Tests.csproj --no-restore --filter 'FullyQualifiedName~ModuleMigrationTests|FullyQualifiedName~ModuleHistoryTableTests|FullyQualifiedName~EfSchemaContentDeclarationTests|FullyQualifiedName~OrdinalCollationMigrationTests|FullyQualifiedName~OrdinalKeyComparerTests' --logger 'console;verbosity=normal'
dotnet run --project tools/maps/Elsa.Maps.Generator --no-restore -- check
```

T019's actual interactive preview/cookie witness and T013/T020/T023's ordinary session issuance/enforcement are
still incomplete, as are management/Studio, native server-database/separate-process deployment, independent
review/required CI/merge and resulting-main qualification. The considered recurring-role-mapping decision is
still pending; withdrawn selection and elapsed time are not authorization. No end-to-end completion is claimed.


## Backend HTTP sign-in, live discovery and preview checkpoint

Implemented optional `FoundationIdentityExternalAuthenticationApi` and the Core canonical-user sign-in/live
provider-source seams. The ASP.NET Core Identity substrate supplies the ordinary cookie issuer without gaining
managed-authentication/Secrets dependencies. The selected framework principal factory/security stamp and
current user/membership/sign-in/lockout policy are used; the new external adapter refuses an account whose
required local second factor cannot be completed by this protocol. No raw upstream grants reach the cookie.
The normal token exchange now exposes the refresh token it already creates; exchange/refresh responses explicitly prohibit credential caching and the actual HTTP clients assert that boundary.

Protocol/form/correlation and endpoint security contracts are documented in the [owning API module](../../Essentials/src/Foundation/Identity/ExternalAuthentication/Api/README.md).
The backend theme/custom renderer includes local and live external choices, escapes names, emphasizes a
preferred choice without redirecting automatically, and retains local recovery when the optional catalog fails.
Disabling the password method hides/refuses passwords while the same cookie substrate can issue external
sessions. Live descriptor capabilities reflect Elsa's configured IAM authority and effective ownership mode;
external authentication never silently substitutes the IdP's user/role authority. Undecided recurring mappings
are not advertised. Missing protocol/artifact/session support is not advertised as a usable sign-in method.

Owned HTTP evidence is now `ManagedOidc/ExternalAuthenticationHttpTests.cs`: a real Kestrel backend under a path
base, two independently hosted actual HTTP OIDC protocol fixtures, native SQLite IAM/provider/artifact stores,
a separate native SQLite OpenIddict vendor token store, and an owned Data Protection key directory. Browser-like
HTTP clients persist real cookies; this is not a Playwright browser or an external OIDC product deployment.
The test suite proves:

- Both providers produce ordinary canonical-user cookies, access tokens and refresh tokens with saved Elsa
  roles/permissions; equal opaque upstream subjects at different issuer/provider scopes become different users.
  A reader cannot access the administrator resource. Logout revokes its access/refresh credentials and copied
  cookie stamp while the other user's cookie/access/refresh remain usable. This also works after the connection
  disappears from discovery and through the technical first-party bearer provider, matching a cookie only by
  complete canonical subject.
- Preview of a disabled non-effective draft and an enabled already-linked user preserves the administrator's
  exact cookie, existing first-party token and IAM user/link/token counts. The result exposes prospective Elsa
  roles/permissions and consumes once. Bearer-only administration cannot substitute for that administrator's
  interactive cookie. Earlier flow tests retain cross-administrator/browser/purpose/expiry coverage.
- Antiforgery, protected browser/purpose correlation, duplicate parameters and replay refuse before unauthorized
  sign-in. Simultaneous tabs have separate correlations and resolve one stable admitted owner. Unapproved return
  URLs, source disable/secret rotation/default rejection, oversized bodies and wrong content types never issue
  a normal session. Responses are no-store/no-referrer and contain only closed errors.
- Live chooser/bootstrap honors enablement/preference and HTML escaping. A real optional-catalog table outage
  leaves the local form/login/logout usable. FoundationOwned/ExternalOwned/Hybrid use their current management
  capabilities; existing linked users can sign in while prohibited unknown-user provisioning is refused.
- Cookies reject disabled/deleted users and suspended memberships. The new external cookie issuer refuses an
  existing account's local second-factor requirement rather than bypassing it. This does not establish durable
  selected-session administration or live access/refresh status enforcement for disable/delete (T023).
- The seven protocol endpoints carry their owning module and explicit security dispositions: five justified
  public protocol operations and two provider-management permission operations.

Results and correction history:

- Initial compile diagnostics for new Core API tracking/imports and test token-store type qualification were
  corrected. First HTTP fixture startup failed because its middleware needed `AddAuthorization`; corrected.
  Then the test client used an unsupported default deserializer for Core `IReadOnlySet` session fields; it now
  reads the actual wire arrays. The cache assertion now checks no-store semantics rather than exact header text
  (the cookie middleware also appends no-cache). Expanded body-size coverage uses form content so it reaches
  the size boundary rather than the framework's earlier 415 content-type rejection. These failed runs are not
  counted as passing evidence.
- Restored initial actual HTTP cases **10/10**, expanded cases **18/18**, then final complete Identity project
  **718/718**, zero failures/skips; the final repeat after credential-cache assertions reports 15 seconds test duration. The final suite includes **22** actual HTTP
  cases plus all earlier protocol/flow/admission/isolation regressions.
- Antiforgery mutation: temporarily remove just the new start's antiforgery validation. The real HTTP regression
  fails because an unprotected POST returns **302** instead of **400**. Exact API source restored in `finally`
  before the final 718-test passing run.
- Full architecture initial run: 615 passed, one failed because the new API lock file had not yet been added to
  the index. After indexing all new production files and lock file, restored full suite **616/616**, zero skipped,
  39 seconds reported test duration. This includes the source/solution/lock/EF/endpoint architecture guards.
- Preceding remote head `d412adf03` CI failed: linked `ModuleContextCatalog.cs` required the new module reference
  in the separate migration provider-test project, and the solution-folder guard required the persistence leaf
  under `ExternalAuthentication/Persistence`. Added the explicit provider-test reference/lock closure and moved
  the solution entry, without waiving either guard. The affected provider-test project now **builds with zero
  errors**, and the complete architecture suite passes. That is compile/architecture proof, not native container
  qualification. No old failed CI run was retried; the next pushed head will receive its normal checks.
- Generated solution-filter check passed. The broad restore graph's existing audit/style warnings remain recorded;
  no unrelated package upgrade was performed.

```sh
dotnet test Essentials/tests/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore --verbosity quiet
bash tools/architecture/restore-ci-project-graph.sh --verbosity quiet
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore --verbosity quiet
dotnet build Essentials/tests/Persistence/EntityFrameworkCore/Migrations/ProviderTests/Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests.csproj --no-restore --verbosity quiet
dotnet run --project tools/maps/Elsa.Maps.Generator --no-restore -- check
```

T013/T014/T019/T020 implementation checkpoints are complete at this scope. The actual runtime readiness/recovery
adapters (T016), considered role-mapping semantics, management/Studio, durable session record/status enforcement,
real-browser/external-product and supported deployment-mode proof, independent review/current required CI/merge
and resulting-main qualification remain required. The full goal remains active and unachieved.

## Provisioning-policy decision and actual recovery adapters (4 November)

The considered owner answer selects provisioning policies assigning saved existing Elsa roles, with external group-to-role mappings omitted for now. ADR 0085 D4, spec FR-020 and the managed contracts follow that choice. Removed unused RoleMapping/RoleMappings contracts, refusal placeholders and metadata. The programmable provisioning policy remains bounded by declared existing-role/deployment/editor authority. The new regression actually provisions through an integrator policy, changes the saved membership manually, adds upstream group/role/wildcard-permission claims, and proves later preview/admission uses the manual membership without another policy call or write. Existing missing-role and delegation cases remain in the complete Identity run. T003/T012 are complete at implementation scope.

Replaced unavailable readiness/recovery defaults with installed protocol/callback/artifact/session/IAM/policy/role checks and evidence-backed administrative recovery. The API records only successful normal administrative external sign-ins, in a separate versioned purpose-protected bounded envelope in existing provider configuration storage; previews and reader sign-ins do not count. Proofs pin source record/material/callback/exact issuer/subject/user/credential generation. Recovery rechecks current framework/user/membership/permission/link and live bounded metadata/JWKS; local recovery requires an actual eligible administrative password account, checking the actor first then bounded pages. Defaults: 24-hour evidence window, sixteen proofs per key, 1,000-account local scan. Inaccessible/corrupt evidence refuses destructive management rather than opening a path. Optional evidence-record failure does not fail a successful normal login. Catalog mutation still uses CAS and a thirty-second external-check deadline.

New owned real HTTP/native SQLite tests prove readiness categories without IAM mutation, password/lockout/2FA/status/membership/permission eligibility, preview/reader exclusion, protected observations readable on an independently constructed node with shared keys, wrong-key/tamper refusal, presentation-only stability, invalidation on client/secret/callback/expiry/user/membership/permission/link/discovery changes, invalid baseline refusal and safe proved-baseline reveal. Two independently composed nodes are forced to read one catalog revision before retiring different proved administrative paths: exactly one saves, the other conflicts, and a fresh last-path retry refuses. This completes T016's adapter/catalog implementation scope; it does not prove atomicity with concurrent IAM/link/session mutation (T024).

Intermediate failures were test-fixture/API tracking and actual integration omissions, not waived gates: added required RedirectUri API tracking; changed native fixture inspection to decode exact UTF-16 identity/settings columns instead of translating string prefix operators against encoded values; tampered the protected setting through the real revision-aware store to preserve its storage codec. The prior committed head's CI failed the old cookie fixture's missing canonical marker/user claim and the migration inventory/package metadata for the new module. Updated that fixture plus an unscoped-cookie refusal regression, pinned the nineteenth module/feature/history/project, and declared/packed its ef-provider metadata. No branch/guard/test waiver was added.

Validation: complete Identity **747/747**, complete architecture **616/616**, ASP.NET Core Identity EF **59/59**, zero skips; generated solution-filter graph check passes. Removing the recovery credential-generation comparison causes the secret-rotation case to fail (8/9 pass); original source restored in `finally`, then the affected provisioning/recovery subset passes **55/55**, zero skips. The migration suite initially passed 507/508 with the new module’s metadata/pack declaration missing; the corrected capability check passes 1/1 and the complete suite then passes **508/508**, zero skips. Shared-machine load reached 197 on 15 cores during broad verification; no timing failure was treated as a product defect. Full delivery stays incomplete: management API/Studio, durable session status/revocation, link/session administration/events and mutation guards, actual browser/external-product/server-database/separate-process qualification, artifact/public consumer checks, independent review/required CI/merge and resulting-main proof remain required. Recent sign-in evidence cannot guarantee the external provider's future account availability. Existing broad-graph dependency warnings are unchanged.

## Permission/revision-protected management API (4 November)

T021 adds seven real HTTP operations in the existing optional managed API module, preserving the base API dependency envelope: source/effectiveness/catalog reads, detail/readiness, prospective assessment, revision-protected commands, bounded metadata testing and bounded existing-user/role lookups. Read/write/lookup permissions are declared in endpoint metadata; current canonical eligibility and persisted actor grants are rechecked before global reads/mutations. Role delegation checks current grants and the actual saved policy for enable/restore, rather than trusting caller omissions. Deployment/archived/unsupported source metadata remains visible with the appropriate editor availability; explicit override is still separate. Catalog exposes the configured protocol path so clients can obtain antiforgery/preview routing without assuming the default.

Posts require JSON, paired antiforgery/header and a 64-KiB body bound; unknown fields (including recurring mapping configuration), unsupported editors and inline credentials refuse. Projections whitelist known OIDC fields and secret references; private protocol/recovery bindings and arbitrary future settings never enter JSON. Lookup pages are 1–100 with finite offsets/search. Metadata tests have a thirty-second overall cancellation bound, require an expected catalog revision, return only closed categories/time/material/staleness, and perform no admission/code exchange/IAM/session/token/recovery-proof mutation. Interactive preview keeps its existing separate administrator/browser/one-time protocol.

Owned Kestrel/native SQLite tests actually drive configure-assess-save-test-enable-disable-archive-restore-remove and same-key explicit override/promotion/archive/restore, including prospective source effects with no pre-confirmation save. They cover stale revisions/permissions, saved-policy/current-grant delegation despite an omitted role list and a stale wildcard cookie, CSRF/media/body/unknown-field/source/role/database policy refusals, safe projections/callbacks, bounded lookups, material change during real discovery and endpoint ownership/complete permission/API-denial metadata.

Initial HTTP execution found that permission denial for a cookie returned a redirect instead of the API status, and the old ownership test counted all newly expanded module endpoints. The management routes now use the framework's DisableCookieRedirect metadata, independently asserted; the existing protocol ownership test correctly selects its seven protocol operations. The focused run then passed 18/18 before the final saved-policy regression. The complete Identity suite passes **764/764** and complete architecture **616/616**, zero skips. A mutation replacing saved-policy lookup with request-body policy makes the stale-cookie/omitted-roles case fail (200 instead of 403); original restored in `finally`. Restored complete Identity passes **764/764**, zero skips; the generated solution-filter graph check passes. The final invalid-role-input/feature-description cleanup also passes the affected management subset **19/19**, zero skips.

This is the API implementation checkpoint, not complete delivery. Studio/SDK, durable selected-session/status/revocation, link/session administration/events and cross-IAM mutation integration, actual browser/external-product/server-database/separate-process qualification, packed/public consumers, independent review/current required CI/merge and resulting-main acceptance remain open. #2777/#2778/#2779 are not represented as completed by these bounded supporting operations.
