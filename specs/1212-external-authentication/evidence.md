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

## Current-main integration and locked restore (4 November)

Hosted PR shard pr-1 at `c516c030c` failed restore with NU1004: its merged tree includes main's new `Elsa.Persistence.EntityFramework` → `Elsa.Persistence.Core` dependency, absent from the branch-new artifact module's lock file. This was a real merged-graph omission, not a test timeout or a waived check. Main `2a2b6578159dea38d75974d49e9cdb478d857853` has successful CI run 41861953427 and no open main-is-red CI issue at inspection.

Merged that main into the owned worktree without conflicts. Normal project-graph evaluation refreshed exactly the new artifact EF module's lock file with the project dependency; all other incoming source/lock updates are main's existing history. Then the actual CI graph script passes **locked restore** for Release and isolated Debug. No CI lock setting is weakened and no dependency version is upgraded for this fix. Integrated Release Identity passes **764/764** and complete architecture **616/616**, zero skips. Integrated Release shared migration checks pass **508/508**, zero skips; generated solution-filter verification passes.

Full #3579/#2848 delivery stays active and incomplete. This updates the branch baseline; it is not a merge of this draft feature into main or resulting-main acceptance. UI/SDK and the remaining session/administration/deployment/review/artifact/consumer gates stay required.

## Studio connection management and actual composed-browser checkpoint (4 November)

T022 adds optional `ExternalAuthenticationStudio` under `Extensions/Identity/studio/`, registered in the Studio solution/default host with backend-capability discovery. The public SDK/UI module implements source-aware list/search, disabled draft/editor, metadata-only secret reference selection/manual fallback, installed provisioning policies/existing-role delegation, revisioned assessment/confirmation/lifecycle commands, metadata tests, real full-page preview/return and explicit enable/promotion. Deployment sources and unknown configuration schemas stay read-only. Preparing a full deployment override saves an ordinary disabled same-key draft; only explicit promotion changes the effective source. Dirty/retained-tab/navigation/backend-response behavior is covered at component scope. CSS uses public `--studio-*` tokens. The React-independent SDK is separately emitted with declarations inside the module's static assets.

Real browser integration exposed an actual mismatch: a token minted through the administrator's bearer transport did not validate in the browser's cookie-only preview form (400). Preview preparation now explicitly authenticates the same administrator cookie for antiforgery issuance, returns actual configured form/header names, and rejects a different bearer/cookie typed identity. Actual HTTP regressions exercise the real form, preserve administrator cookie/IAM rows and pin discovery/current read-only/complete-configuration metadata. OIDC draft validation also refuses credentials embedded in an issuer URI. Bound provider failure/stale configuration returns a closed failure category to the currently allowed protected Studio destination; unbound state, changed callback configuration and revoked return-origin policy cannot redirect. Six HTTP cases cover those failure/replay/isolation boundaries. Frontend return validation uses a same-tab/backend nonce, closed category allowlist and one-use local metadata, strips query handles/failures before reads, and leaves enablement blocked after failure. It stores no returned identity or permission data.

The new non-packable `tools/identity/ExternalAuthentication.Probe` composes actual backend APIs, managed OIDC, normal cookie/OpenIddict, native SQLite IAM/configuration/artifact/vendor-token stores and owned Data Protection/signing keys with two controlled real Kestrel OIDC providers. The browser runner owns ephemeral Vite/backend/Chromium processes and its temporary stores, validates an instance marker, never forwards cookies/authorization through the GET-only same-loopback-origin asset proxy, retains redacted evidence and stops only its own processes. See its README for ownership/reproduction details.

Actual Chromium journey passed: local administrator login → new disabled `studio-company` connection with JIT Reader and a secret reference → metadata/JWKS test → actual provider preview → same saved draft → explicit enable → ordinary external reader sign-in in a separate browser context. Before/after preview: **users 1→1, links 0→0, first-party token rows 2→2**, administrator canonical identity unchanged; actual provider code-exchange requests 0→1. The ordinary external user has saved role `reader`, permission `identity/users:read`, no wildcard grant, and a distinct canonical identity. The administrator remains signed in. Result handle is removed from the address bar before retrieval. At 390px viewport the page has no horizontal overflow. Axe WCAG 2 A/AA and 2.1 AA scan reports **zero violations** with actual Studio Classic light styles. Desktop/narrow screenshots were inspected. Latest receipt at this checkpoint: local owned `elsa-3579-browser-alftuq/browser-receipt.json`, alongside `preview-light.png` and `connection-narrow.png`; no credentials or trace are published.

Corrections are not waived checks. Initial proxy fallback excluded filename paths; explicit asset fallback fixed the real 404. The first accessibility run found two primary-button contrast failures in the old generic test-fixture palette (4.09:1). The qualification entry now loads actual Studio Classic light host styles; no production button/token change or scan exclusion was used. That corrected actual host palette passes. The first added preview-failure tests referenced an incorrect options property name; corrected to the existing `AllowedReturnUrlOrigins`. A new README link had one too many parent traversals; the catalog guard caught it, the target was fixed, focused links pass 2/2 and complete architecture then passes. Integrator policy names that coincide with JavaScript object properties are rendered literally; error/failure lookup accepts only own allowlisted keys.

Validation at implementation scope:

- Complete Release Identity **774/774**, zero skips. `--list-tests` lists 774. Initial expanded run before closed preview-failure cases passed 768/768. A security mutation removing only the bearer/cookie subject comparison makes the mixed-administrator regression fail (**200 instead of 401**); source restored in `finally`, followed by restored complete Release Identity **774/774**, zero skips (50 seconds reported duration).
- Complete Release architecture **616/616**, zero skips, after actual Release and isolated-Debug CI graph restores; **locked** restore also passes for both configurations. `--list-tests` lists 601 entries: two nonserializable MemberData theories expand from two entries into 17 runtime cases (11 capability registrations and 6 collectible API-explorer metadata cases), explaining all fifteen additional executed cases. The focused two-theory run passes 17/17. The API's new Capabilities.Core reference is reflected in its, Identity-test and architecture-test locks; the new probe has a lock file. No unrelated package upgrade or lock waiver.
- Module TypeScript check/build and **25/25** SDK/component cases pass, including draft/complete-override, test/preview/enable, failure return, read-only, dirty/conflict/backend races, secret redaction and custom policy names. ESLint, full workspace CSS tokens and module-layout checks pass. Latest module JS is approximately 36 kB (11.4 kB gzip); CSS approximately 3.9 kB (1.1 kB gzip).
- Affected `Studio.Web` .NET host build and owned probe build pass with **zero warnings/errors**. Generated solution-filter check passes without filter changes.
- Local module NuGet pack succeeds; inspected archive includes feature assembly, module JS/CSS, standalone SDK JS/declarations. An isolated temporary consumer imports the extracted SDK without React/workspace/source dependencies and exercises custom protocol/antiforgery names, revisioned command transport and cookie-bound preview submission. This is the new Studio artifact boundary only; all-backend packed/public-consumer acceptance remains T031.

This completes the T022 implementation checkpoint only. The probe uses a **controlled HTTP provider, native SQLite and the mounted product component**, not the full Studio host or an external IdP product. It does not establish separate Studio/cross-site cookie topology, server-database, restart or separate-process/shared-node acceptance. Durable selected-session/access/refresh status and revocation (T023), link/session APIs/events and cross-IAM last-path mutations (T024), their Studio surfaces (T025), full acceptance/deployment matrix (T026–T029), all operator/package/public-consumer checks, independent exact-head review/required CI/merge and resulting-main proof remain required. The full goal stays active and unachieved; #2777/#2778/#2779 are not closed by these bounded supporting operations. Existing broad server-graph audit/style warnings remain unchanged.

## Durable canonical sessions and live token status (4 November)

T023 implements `IAuthenticationSessionStore` in the existing optional managed EF module. Base Core defines the
server-owned session record and committed create/read/bounded owner-page/revisioned revoke contract; the optional
store protects immutable tenant/user/provider/exact-link/account-version/time metadata under shared Data Protection
keys, uses random 256-bit IDs/revisions and tenant/user/expiry indexes, and returns explicit revoked/already-revoked/
missing/conflict outcomes. It stores no upstream token, client secret, password or raw framework security stamp.
Create/revoke reject caller-owned EF/ambient transactions. Provider failure stays a failure, never acknowledged
success; retry can still revoke. Pages are capped at 100 and per-create cleanup at 64 expired current-schema rows
of its own tenant. The additive four-provider `DurableSessions` migrations preserve the existing Initial history
and protected flow content; `IdentityExternalAuthentication` advances 1.0.0→1.1.0 with a content-preserving upcaster.

Ordinary local and external canonical sign-in now create a durable record before cookie issuance when this authority
is installed. The cookie and trusted token-exchange request carry its internal ID; access JWTs and protected refresh
payloads retain it. Refresh rotation and access-token expiry are bounded by the session's absolute expiry. Default
lifetime is eight hours, configurable in ASP.NET options/the shell feature from one minute to thirty days. Invalid
lifetime refuses managed start/callback before protocol/admission. The canonical authority explicitly declares
`SupportsDurableSessions`; its default capability/read/validation methods refuse unsupported implementations.

Every first-party bearer invokes current session validation. The existing canonical authority owns a fresh tenant
scope derived from the signed typed subject before resource-scope selection, and reads live framework user,
security-stamp hash, IAM status/membership, lockout/second-factor eligibility and exact qualified-link owner. The
OpenIddict behavior acquires no Workflows/Secrets/managed-EF dependency. Cookie validation checks the same record
and live authority. Canonical credentials without the binding refuse when the managed store is installed; an
already bound credential refuses if its authority is removed. Existing token-only/non-user direct bearer
compositions retain their typed external subject behavior. Both OIDC input filtering and shared normalization strip
incoming session-ID claims; real bearer regressions include an uppercase forged claim.

IAM user status transitions rotate the existing security stamp inside the same authority transaction, through both
SetStatus and whole-record saves. Disable→enable cannot resurrect old cookies/access/refresh even with no request
during the disabled interval. Newly saved Core IAM users receive an epoch. Older/programmatically seeded users with
no epoch initialize it only after verified admission at sign-in; eligibility and preview remain read-only. Actual
HTTP verified-email matching tests cover both cases, preserve the existing Elsa user/roles and create no password.

Native SQLite tests prove protected reopen/shared keys, exact tenant/user paging, private provenance protection,
selected revisioned/idempotent revocation, a deliberately synchronized two-node CAS race (one acknowledgment),
provider-fault retry, wrong-key/payload/index/schema refusal, transaction rejection and incremental upgrade preserving
a preceding one-time flow. Actual HTTP tests prove one session's cookie/access/rotated refresh become unauthorized
while another session of the **same** user and an unrelated provider/user survive. Live disable/delete, membership,
stamp, enabled lockout, second-factor and unlink cases reject issued access/refresh/cookies. Cross-user/tenant/kind
session attachment and missing bindings refuse before any token row is created. A controlled clock proves absolute
expiry and that refresh cannot extend it. Revocation is driven from a separately constructed node sharing native
stores/keys; this is not a separate-process or server-database claim.

Validation/corrections:

- Complete restored Release Identity **803/803**, zero skips; `--list-tests` lists **803**. The 29 added cases cover
  ten native session persistence cases and nineteen actual HTTP session/matching/ownership/lifetime cases; existing
  sign-in/preview/recovery/management/manual-role/isolation regressions remain in that full run.
- Complete Release architecture **616/616**, IAM EF **206/206**, ASP.NET Core Identity EF **59/59**, shared migration
  guards **508/508**, zero skips. Generated solution-filter check passes. Core public API tracking includes the new
  contracts/capabilities/bindings. There is no project-reference/package/lock change in this checkpoint.
- Mutation removing only live session validation from the bearer handler makes the selected-session regression
  fail **200 instead of 401**. Source restored in `finally`; restored complete Identity passes 803/803. Native store
  fault injection separately proves an unacknowledged revoke remains retryable. No guard/test waiver.
- Earlier diagnostic runs caught missing Core API declarations/imports, a wrongly assumed qualified-link method
  name, the test's lockout-disabled fixture and a nested C# variable collision. Corrected the owning API usage and
  fixture setup; those failed runs are not counted as passes. Scope ownership stays in the existing canonical
  adapter rather than introducing a workflow dependency into OpenIddict behavior.
- The owned probe builds and its actual Chromium configure/test/preview/enable/ordinary-reader journey passes with
  **users 1→1, links 0→0, token rows 2→2, durable sessions 1→1** through preview and the administrator unchanged.
  Reader access remains `identity/users:read`, no wildcard. Classic-light axe scan has zero WCAG 2 A/AA + 2.1 AA
  violations; 390px has no horizontal overflow. Local owned receipt: `elsa-3579-browser-vZJPnp/browser-receipt.json`.
  Runner ESLint passes. The probe is still the mounted component/controlled HTTP provider/native SQLite boundary.

T023 is complete at implementation scope. T024 still requires authorized link/session inspection/mutation APIs,
redacted notifications and atomic recovery coordination with IAM/link/session mutations; T025 requires those Studio
surfaces. Full Studio host/external IdP product/cross-site/server-database/separate-process/restart/two-node matrix,
all package/public-consumer checks, independent exact-head reviews/current hosted gates/merge and resulting-main
qualification remain required. This checkpoint does not close #3579/#2848 or advance general identity administration
#2777/#2778/#2779. The full goal remains active and unachieved. Existing broad-graph audit/style warnings are unchanged;
new generated migration files have the same namespace-style warnings as existing provider-generated migrations.

## Studio host discovery gate correction (4 November)

Hosted UI-head CI 41880630016 at `2e78bf007` failed the complete Studio .NET suite: **709 passed, 2 failed**,
zero skips. The existing default-shell and shipped-feature discovery tests both reported
`ExternalAuthenticationStudio` as undiscoverable. The module was shipped/referenced and enabled, but the Web
host's explicit `WithStudioFeatureAssemblies` distribution list omitted its assembly. A .NET build alone had
not proved that runtime discovery boundary. Added the module to that existing list; no test/guard/configuration
exclusion was introduced.

Restored real CShells discovery now lists the feature and passes **3/3** discovery cases. After building all Studio
frontend packages with workspace concurrency limited to two, the **complete Studio .NET project passes 711/711**,
zero skips. This corrects the actual host-registration omission; it does not turn the mounted component's browser
evidence into a full Studio browser/deployment acceptance claim. Hosted checks for the pushed correction remain
pending; no superseded failed run was retried. Full feature acceptance/review/merge/resulting-main remain required.

## Link/session API and notification checkpoint (4 November)

Added four dedicated default permission keys: `identity/sessions:read`, `identity/sessions:manage`,
`identity/links:read` and `identity/links:manage`. All four new administration routes also require
`identity/users:read`; handlers recheck current canonical eligibility, saved grants and applicable ownership
capabilities. The existing non-interactive credential permission does not grant session or link administration.
The API README documents the exact routes, safe projections, bounded paging and revisioned request bodies.

Session inspection projects only the requested tenant/user's safe session metadata and live state. It omits
account security versions and upstream subjects/tokens, and refuses an overbound or wrong-scope replacement
result. Selected-session revoke preserves the user's other sessions, uses the durable expected revision and
keeps storage failure at 503/`revoked:false` so retry remains possible. Another user's session is not a valid target.
Actual HTTP tests repeat cookie, access and refresh attempts after revocation and verify sibling survival.

Link attachment targets an existing user and a configured unarchived connection. The server tests provider
metadata to obtain the issuer, rejects caller-supplied issuer/unknown fields, rechecks source material and current
user/catalog revisions after remote work, and uses the existing atomic admission authority without creating a
user or rebinding another owner. Saved role assignments remain unchanged. Role, direct-permission, membership,
deployment delegation and ownership boundaries apply before a credential can be attached to that account.
Real provider sign-in then resolves the manually linked existing user with its saved Reader role; attachment
itself neither exchanges an authorization code nor issues a session/token.

Provider mutations, sign-in/admission and preview outcomes, attachment and selected-session revocation emit
redacted existing `IAuthEventSink` notifications. Closed action/result values and canonical IDs replace upstream
subjects/profile data, settings, secrets, tokens and raw exception messages. Unbound or failed callbacks and
stale command refusals emit closed denials too. An installed sink throwing does not undo a committed mutation.
The host still owns sinks; no audit database was introduced.

Validation: **831/831** complete Release Identity cases, zero skips; discovery also lists **831**. The 28 new
cases cover the administration boundaries, selected revocation/retry/fault behavior, safe notifications and
attachment conflict/ownership/delegation/source checks. Full Release architecture passes **616/616**, zero skips,
with new sources staged before enumeration. Generated filters and whitespace checks pass. Logs:
`/tmp/elsa-3579-access-complete-identity.log`, `/tmp/elsa-3579-access-list-tests.log`,
`/tmp/elsa-3579-access-architecture.log`, `/tmp/elsa-3579-access-maps.log`.

Mutation proof: removing only target-role delegation from attachment makes the actual role-boundary regression
fail (`canAttach:true` instead of false); the other three boundary cases pass. Original source was restored in
`finally`. The restored complete Identity run is retained in `/tmp/elsa-3579-access-restored-identity.log`;
the expected failed control is `/tmp/elsa-3579-access-delegation-mutation.log`.

Hosted CI **41887842254** at `04f5d50d6` passed the Studio test/build stages but failed the subsequent complete
NuGet inventory check: the new `Elsa.Studio.Identity.ExternalAuthentication` project was absent from the current
package inventory. Added it as a post-consolidation package without changing the frozen consolidation manifest.
Also removed dependency suppression from that module: its packed public references must include Studio Core
and CShells rather than depending on a consuming application to provide them implicitly. The existing verifier
now checks that dependency metadata too; no package, guard or failure was excluded.

The existing verifier self-test passes. A fresh actual Release solution pack using the built frontend payloads
and isolated output `/tmp/elsa-3579-studio-nuget-access-20261104-1` passes exact **26-package** inventory/version,
MIT/icon, dependency and static-payload-byte verification, including the new module and its standalone SDK.
Report: `/tmp/elsa-3579-access-studio-archive-verification.json`. Version: `4.0.0-extauth.20261104.1`.
This proves the archive checkpoint; it does not replace the remaining public-consumer/runtime qualification.

T024 remains unchecked: qualified unlink and atomic cross-IAM final-login coordination are still required.
The API exposes no unprotected removal action. T025 Studio link/session surfaces and T026–T031 actual full
Studio/external IdP product/cross-site/server-database/restart/separate-process/two-node, all consumer/artifact,
independent exact-head review/current required CI/merge and resulting-main proofs remain required. The PR stays
draft, #3579/#2848 stay open and the complete delivery goal remains active and unachieved. This checkpoint does
not close general identity-administration issues #2777/#2778/#2779.

## Account-version and standalone image corrections (4 November)

An actual HTTP regression reproduced credential resurrection: move a qualified login from user A to B and back
before using A's original cookie; A's old cookie was still authenticated. The Identity relationship coordinator
now rotates both affected root account versions in the same transaction as an actual ownership change. Earlier
cookies/access/refresh stay invalid when the login returns. A fresh normal sign-in still resolves the intended
existing user with its saved Reader role; an unrelated provider user's cookie/access/refresh remain live.
Successful same-owner metadata updates and refused stale rebinds preserve the current account versions.
Native SQLite tests cover both qualified and legacy login namespaces.

A second actual HTTP regression proved that the older protected recovery observation also revived after that
round trip. Recovery recording now requires the actual freshly issued committed session for the same canonical
user, connection record, issuer and subject. Canonical sign-in returns a server-only session ID; both that result
metadata and the provenance binding are excluded from JSON serialization/deserialization. The observation keeps
the opaque account version and original session/provider identifiers only inside its existing protected envelope.
Recovery revalidates the account/link binding within the proof's own window. Rebind-away/back, disable→enable
and security-stamp changes cannot revive old evidence; fresh normal sign-in can produce current evidence.
Missing/fabricated/another-user session IDs cannot record evidence, and a revoked session cannot renew it.
Ending an individual session still leaves historical credential evidence bounded by its original proof window;
it does not imply the credential can no longer complete a fresh sign-in. Older observations without bindings do
not qualify and need fresh normal administrative sign-in. No new database table, migration or audit store.

Before/after controls: `/tmp/elsa-3579-rebind-before.log` fails with authenticated instead of anonymous after
the ownership round trip; `/tmp/elsa-3579-recovery-epoch-before.log` fails with old evidence still usable. The first
after-run exposed a fixture assertion using `unauthenticated` instead of the API's existing `anonymous` status;
corrected that expected wire value. An intermediate compile failed on a provider-ID helper owned by the API;
the managed implementation now retains the actual issued provider ID instead of importing or duplicating that
adapter helper. No test, guard, dependency boundary or failure was waived.

Final Release checks: Identity **837/837**, discovery **837**; IAM EF **207/207**; ASP.NET Identity EF **59/59**;
architecture **616/616**, zero skips. Generated filters and whitespace pass. Current logs:
`/tmp/elsa-3579-epoch-final-identity.log`, `/tmp/elsa-3579-epoch-list-tests.log`,
`/tmp/elsa-3579-epoch-full-iam.log`, `/tmp/elsa-3579-epoch-full-aspnet-ef.log`,
`/tmp/elsa-3579-epoch-architecture.log`, `/tmp/elsa-3579-epoch-maps.log`.

Hosted CI **41894199102** for preceding PR head `c99eac0bd` passed complete Studio archive and actual public
consumer qualification, then failed the standalone Docker image: the Dockerfile did not copy `Extensions/Identity/studio`.
Added that production source directory alongside the existing optional-module copy list. The actual local image
now builds with the committed Dockerfile plus this correction, under one-CPU/four-GiB build limits, without
publishing. Tag: `codex-elsa-studio:3579-c99-epoch-20261104`; log `/tmp/elsa-3579-epoch-studio-docker.log`.
An owned non-root runtime container proves the feature assembly exists and serves host HTML, module JS and
standalone SDK JS/declarations with HTTP 200. Container stopped after verification; receipt
`/tmp/elsa-3579-epoch-studio-docker-http.json`. This is image/HTTP distribution proof, not a full SSO browser journey.

Downloaded CI artifact **13357035547**, `studio-package-qualification-41894199102-1`, to
`/tmp/elsa-3579-c99-studio-package-qualification/`. Its complete aggregate report is passed: 26 Studio packages plus
9 support candidates, including the new module and its actual reference-distribution DLL. Actual normal/restored
API, hosting, composition, reuse and pruning observations pass; expected omission/vendor/empty/runtime/pruning
controls have their intended failed outcomes. Frontend invocations in consumers are zero. Package version is
`4.0.0-studio-ci.41894199102.1`; source is CI's synthetic merge `cac677cdd14a475678f0e5f49447d079e6e32e99`,
which includes PR head `c99eac0bd`. New module archive SHA-256 is
`9eece937b05ee620eb34bc031180ae711a5fbbb95e19cba9c3515ed45e315dac`.
These inspected reports strengthen the preceding-head Studio artifact evidence; final exact-head/all backend
package/public-consumer gates remain required. That CI run remained active in a browser shard at the last poll;
the completed Docker failure was inspected directly, not treated as a successful run or retried.

T024 remains unchecked for qualified unlink and atomic cross-IAM final-login coordination. T025 Studio link/session
surfaces, the full Studio/external IdP product/cross-site/server-DB/restart/separate-process/two-node matrix,
independent exact-head review/current required CI/merge and resulting-main qualification remain required.
The complete #3579/#2848 goal remains active and unachieved; PR #3806 remains draft.

## Coordinated IAM/provider persistence operation (4 November)

Added the required storage operation for T024: `IamRevisionGuard`, optional read-only guard capture on user/role
revision stores, and `IRevisionAwareProviderConfigurationStore.SaveWithIdentityRevisionGuardsAsync`. Unsupported
implementations default to no guards/no coordination. The selected EF store advertises support only for owned,
same-provider/same-connection-string IAM/provider targets. Its original two-argument constructor remains available.

The existing `EfSharedTransaction` now also offers an explicitly serializable owner. The new path refuses supplied
connections and runtime retargeting before opening a connection, then verifies fresh contexts preserve the target.
Its original default-isolation path remains unchanged. In one owned physical transaction the provider store verifies
opaque root snapshots, conditionally advances their metadata revisions, then CAS-saves the global configuration.
It preserves account epochs, permissions and readable payload schema. Unknown future schemas fail with the existing
typed skew exception. Caller transactions, ambient transactions and unsaved caller changes are refused.

Numeric versions alone are insufficient: real delete/recreate tests produce the same user/role numeric revision.
User guards also bind the account instance; role guards bind the definition contents. They use existing framed
exact-UTF-16 hashing. A recreated account or changed role cannot satisfy an older snapshot. Serializable snapshot
reads prevent a further delete/recreate between validation and conditional update. No new table, column, migration,
lock journal, lease or distributed transaction was added. Split targets/custom authorities require proven atomic
coordination rather than an optimistic fallback.

Eighteen native SQLite cases prove all-or-nothing commit, user/role/catalog/tenant conflicts, metadata preservation,
independent-writer races, recreated instances, old-readable/future schema, caller-state/target refusals, provider save
failure rollback and a controlled lost commit acknowledgement. The last occurs after a real commit, verifies all
rows committed together, and requires `EfCommitOutcomeUnknownException` even when the inner error looks like a
concurrency exception. It cannot falsely report a plain conflict or success. An actual Kestrel/managed-host case
proves default DI selects coordination and guard-only metadata advances preserve the administrator cookie, access
and refresh credentials.

Guard-removal mutation: bypassing only the guard loop makes **six** of seven focused cases fail, including stale
IAM/tenant and recreated-instance acceptance plus missing root revision advancement. The ordinary stale-catalog
case still refuses. Source restored in `finally`; complete restored suites pass. Control:
`/tmp/elsa-3579-coordinated-guards-mutation.log`. Intermediate test corrections were record/set value comparison,
the existing typed schema-skew outcome, and missing test imports; no production guard or test was waived.

Current Release validation: Identity **838/838** (discovery **838**), IAM EF **225/225**, ASP.NET Identity EF **59/59**,
architecture **616/616**, zero skips in those suites. Shared persistence **725 passed, 1 skipped**: the existing
`EfSqliteSerialOpenStressTests.Concurrent_opens_of_one_file_never_share_a_pooled_connection` explicitly requires
`ELSA_SQLITE_POOL_STRESS=1`; that opt-in stress workload was not run. Both existing Design shared-transaction caller
regressions pass **2/2**. Generated filters and whitespace checks pass. Logs:
`/tmp/elsa-3579-coordinated-final-identity.log`, `/tmp/elsa-3579-coordinated-list-tests.log`,
`/tmp/elsa-3579-coordinated-final-iam.log`, `/tmp/elsa-3579-coordinated-aspnet-ef.log`,
`/tmp/elsa-3579-coordinated-final-architecture.log`, `/tmp/elsa-3579-coordinated-shared-ef.log`,
`/tmp/elsa-3579-coordinated-existing-shared-callers.log`, `/tmp/elsa-3579-coordinated-maps.log`.

This checkpoint completes the persistence operation, not the managed mutation integration. Existing catalog
retirement still needs revision-guard capture around real editor authorization, role delegation and recovery checks,
and must route its write through this operation. Qualified unlink also needs exact link-revision session/proof binding,
monotonic link/owner counters through remove/recreate, and atomic removal/catalog CAS. The implementation sequence
and counter invariants are recorded in `research.md`. **T024 remains unchecked**; there is no removal endpoint yet.
T025 and all actual full Studio/external-product/cross-site/server-DB/restart/separate-process/two-node qualification,
remaining exact-head package/consumer gates, independent review/required CI/merge/resulting-main proof remain required.
The complete goal remains active and unachieved; this storage proof does not qualify server engines or close the feature.


### Qualified link instance binding and native removal

Canonical external sessions and protected recovery observations now bind the exact qualified link revision. The native
removal operation compares the opaque owner guard and exact link revision in its own serializable transaction, removes
the registry child and row together, and advances the owning counter beyond the removed instance. Subsequent qualified
links start above that counter. The account security version and other provider links are preserved. Both IAM and
authentication-state schema families advance to 1.2.0 through additive semantic migrations and content-preserving
upcasters; initial histories and physical models remain intact.

Actual controlled-provider HTTP tests exercise removal/recreation with identical record timestamps: the removed
instance's old cookie, access token, refresh token and recovery observation fail; another linked provider for the same
user retains its credentials and proof, and a fresh sign-in can establish new evidence. A mutation bypassing the exact
link-revision comparison makes both HTTP cases fail; source was restored afterward. Native ownership, stale user/link
revisions, exact issuer, historical counter, receipt replay and injected-save-fault cases preserve atomicity.

Current native IAM tests pass **232/232**, and shared module migration tests pass **508/508**. Final Identity and
architecture requalification are recorded below when complete. These checks cover native SQLite and controlled HTTP
providers; they do not claim the full deployment matrix. **T024 remains open**: the removal endpoint is unavailable
until the final-login and authority snapshot checks are coordinated with its commit.


### Managed catalog writes coordinate their actual IAM checks

The default writer now captures opaque actor/current-role/provisioning-role guards around saved-grant, delegation
and canonical eligibility checks. Local and external recovery return the exact selected identity/role guards after
rechecking live account/framework/membership/link/protocol/admission conditions. The writer merges consistent
snapshots and sends them only as server-owned, JSON-excluded catalog metadata. Administrative persistence uses the
owned serializable IAM/provider transaction plus the full-catalog CAS. Split/unsupported coordination refuses writes
and appears as read-only host management in API/Studio with closed guidance. Trusted nonadministrative catalog
seeding retains its separate unguarded store operation.

Seven actual managed-host cases force independent actor/recovery-user/membership/framework-lockout/role/qualified-link
or proposed provisioning-role changes after final checks and before save. Every stale administrative catalog save
refuses without changing catalog revision or enabled paths. Bypassing the coordinated storage branch makes **7/7**
cases fail; production source was restored before the full run. Existing real concurrent retirement still permits
only one save; the loser may detect either the IAM snapshot change before save or the catalog conflict at commit.
The HTTP unsupported-authority case proves read-only metadata and a 503 command response with no attempted save.

Current full Identity suite: **848/848 passed**, zero skips, Debug. SDK/component cases: **26/26 passed**;
frontend type checking, Vite/standalone SDK build and ESLint pass. The initial frontend typecheck/lint commands used
the wrong workspace/base directory; corrected workspace/root invocations pass without changing package or lint
configuration. An architecture run during active source edits correctly refused a stale compiled assembly; final
architecture qualification follows after source freeze. Native IAM **232/232** and shared migrations **508/508**
remain green from this link-binding stage. Hosted CI **41907993152** passed for preceding head `ba98c2a1b66c2d14688972775a2126821e231c56`;
new-head hosted evidence is still required.

**T024 remains unchecked** for guarded attachment and prospective unlink with catalog/link/IAM atomicity. T025
Studio link/session screens and the full actual Studio/external-product/deployment/server-engine/restart/process/node
acceptance, final consumer qualification, independent review/merge and resulting-main proof remain outstanding.

Final frozen-source architecture suite: **616/616 passed**, zero skips. The earlier stale-assembly refusal is
resolved by rebuilding after the source freeze; no architecture rule or permission boundary was waived.

Final ASP.NET Identity EF suite: **59/59 passed**, zero skips.
Generated solution-filter check and staged/worktree whitespace checks pass. No project references or dependency
versions changed in this increment.


### Guarded link attachment and native link/catalog mutation boundary

The existing attachment endpoint now captures editor/target account and saved-role roots around live canonical
eligibility, current permissions, membership, ownership and delegation. It passes that snapshot to the selected IAM
backend's optional `IExternalIdentityAdministrationStore`. Existing-user attachment, all guarded root advances and
(in database-managed mode) the unchanged full provider-catalog CAS commit in one owned serializable transaction.
The same native coordinator supports conditional qualified removal while preserving its original target guard until
the relationship writer advances the counter. Sibling links and account epochs remain unchanged. Shared IAM guard
comparison/advancement is reused by catalog and link coordination. The selected backend owns the optional coordinator;
a later unowned replacement is refused by startup validation. No base host is required to install the optional store.

Four actual HTTP races change editor role/status/membership or target role after the final delegation snapshot.
Every attachment returns conflict without storing a link or advancing catalog revision. Bypassing native guard
comparison makes **4/4** fail; source was restored before the full run. The deployment-only HTTP fixture now omits
provider-configuration persistence entirely, and attachment followed by provider sign-in succeeds as the saved target
user without that service. Existing source/user/issuer/delegation/ownership/rebind refusal cases still pass.

Native commit/rollback cases cover both modes, stale user/role/catalog snapshots, preservation of sibling credentials,
provider save failure after link mutation, and acknowledgement loss after the physical commit. The latter remains a
typed unknown outcome and all participating rows commit together; it is not converted to a false conflict. The API
returns a redacted unavailable envelope on storage exceptions rather than claiming `attached:false` for an uncertain
commit. Caller transaction/pending-change, split-target, unprivileged-global and cross-tenant refusals are also checked.

Full Identity **853/853 passed**, Debug, zero skips. Final native/architecture/ASP gates are recorded below. An initial
native fixture supplied positional login fields in the wrong order; that fixture was corrected before the passing
full native run. No production guard was weakened. Only the owning Identity EF test assembly receives friend access
for the internal coordinator; production types remain internal by default.

**T024 remains open** for prospective per-user and global recovery and the administrative removal endpoint. T025
Studio links/sessions, complete actual provider/product/deployment/server-engine/restart/process/node acceptance,
current consumers/hosted CI, independent review/merge and resulting-main qualification remain outstanding.

Final native IAM suite: **250/250 passed**, Release, zero skips. This includes all original provider coordination
regressions after extracting the common IAM guard comparison/advancement implementation, the two unknown-commit
cases, and the five new caller/scope boundary refusals.
Final frozen-source architecture suite: **616/616 passed**, Debug, zero skips. The friend grant remains within the
owning Identity domain; no public implementation type or architecture exception was introduced.
Final ASP.NET Identity EF suite: **59/59 passed**, Debug, zero skips. Generated solution-filter and staged/worktree
whitespace checks pass. No project references or dependency versions changed, and no frontend code changed in this
increment. New-head hosted/consumer evidence remains pending.


### Prospective unlink and protected normal-session evidence

Normal sign-in now stores source material revision, opaque credential binding and trusted callback in the existing
protected session payload. The fields survive native restart/paging and do not appear in session/link projections,
tokens or notifications. Creation refuses partial/unbound provenance; older records missing it require fresh normal
sign-in before they can prove a remaining external credential. No new table or provider dependency is introduced.

`IExternalLinkRecovery` proves both the target's remaining local password or real normal-sign-in credential and a
current administrative recovery identity. It excludes the removed exact key, including provider casing aliases,
checks current material/secret/callback/account/link/admission eligibility, and captures all selected IAM roots. Its
bounds are `RecoveryScanLimit` users, at most `min(100, RecoveryScanLimit)` retained sessions per user and thirty seconds.
The observation window is an upper age bound; physically cleaned-up records cannot supply evidence. Ended sessions
can supply historical credential evidence but their actual cookie/access/refresh remain ended. Private configuration
or preview alone never supplies that evidence.

The new declared POST `/users/{userId}/links/remove` enforces the same dedicated current grants, ownership/delegation,
closed JSON, antiforgery and 64-KiB body boundary. Exact owner/issuer/subject and expected user/link/catalog revisions
are checked. Recovery guards commit with the native removal and unchanged catalog CAS in database-managed mode;
deployment-only mode uses IAM without provider persistence. Inspection exposes `canRemove` as action availability;
the prospective check may still refuse. Confirmed removal preserves saved roles, epochs, sibling links and unrelated
credentials. Storage exceptions return redacted unavailable envelopes, with no false `removed:false` claim when the
physical commit's acknowledgement is lost. Current state must be refreshed before retry.

**47/47** focused unlink/session cases pass: both modes and ended evidence, final user/global path refusal,
configured-only links, stale secret/source/callback/window/account/link bindings, revisions/exact owner/issuer/subject,
legacy/unknown fields, CSRF/current grants and affordances, old provenance/fresh sign-in, post-check IAM/catalog races,
rollback, acknowledgement loss and retry, plus protected native session persistence. Two independently constructed
runtime nodes share the owned SQLite stores/keys and cannot both remove their remaining links; retry refuses the
last link. This is same-process native-node proof, not the pending full host/process/server-engine matrix.

Recovery bypass makes **7/7** last-user/global-path cases fail. Native IAM guard bypass makes **3/5** post-check races
fail; the other two retain independent exact-owner/catalog protection. Both production files were restored before
full qualification. Initial fixture assertions used the generic error envelope's wrong property, and an expiry case
also expired the acting administrator; the corrected case expires only the evidence window. No production guard or
assertion was weakened to accept an unsafe removal.

Full Identity **890/890 passed**, Debug; native IAM **250/250 passed**, Release; ASP.NET Identity EF **59/59 passed**,
Debug. Zero skips in these suites. Final architecture/maps/whitespace results follow below. T025 Studio links/sessions
and complete actual Studio/external-product/deployment/server-engine/restart/process/node/consumer/review/merge/main
qualification remain outstanding.

Final frozen-source architecture: **616/616 passed**, Debug, zero skips. Generated solution-filter and staged/worktree
whitespace checks pass. No project references, package versions or frontend source changed in this increment.
T024 is now checked for its backend/API/native coordination scope. Full host/process/server-engine/Studio and
consumer/review/merge/main qualification remain explicitly required by the unchecked following tasks.

## Studio user-access checkpoint (4 November)

The optional module adds Security → User access with its own user-read permission. A bounded tenant-local
directory does not require provider permission; metadata reports independent current link/session read grants,
ownership capabilities, installed administration services and the configured protocol path. Protected SDK
identity commands discover their antiforgery route through that directory rather than reading the provider catalog.
The separate connection editor retains its provider-management boundary.

The page inspects exact/legacy identity links and retained sessions, attaches a configured provider's exact subject,
removes a qualified link with prospective recovery checks, and revokes one selected session. Changes use confirmation
and actual optimistic revisions; the UI follows server capabilities and per-item flags. Read-only ownership, legacy
links, absent modules, denied panels, loading/empty/error states and current-session reauthentication have explicit
surfaces. Uncertain/conflicting writes preserve the draft and require refresh. Backend changes reject late answers;
current permission loss clears displayed identities. Navigation/close probes and real React Activity suspension
preserve unattached drafts.

**42/42** SDK/component tests pass. Module/standalone SDK build, typecheck, ESLint, repository CSS and module-layout
checks pass. An independent Node consumer imports the emitted SDK without React and exercises user-read protocol
discovery, exact subjects/revisions and the configured CSRF header. This is emitted-output proof, not final packed
consumer acceptance. Studio host unit tests **1055/1055** pass. Full Identity **895/895** and frozen-source architecture **616/616** pass, Debug, zero skips.
Generated solution-filter and whitespace checks pass. Existing broad architecture/style warnings remain visible;
there are no project/dependency or persistence changes in this checkpoint.

The actual Chromium journey mounts both real components against the composed Kestrel backend, native SQLite and
two controlled HTTP OIDC providers. It creates/tests/previews/enables a connection with existing Reader provisioning,
verifies preview leaves the administrator identity and user/link/token/session counts unchanged, and signs in the
ordinary reader. Through User access it refuses removal of that reader's last path, attaches the second provider,
completes an actual normal sign-in through it, removes the first link and preserves saved Reader membership.
The ordinary reader can search users but cannot inspect links/sessions or attach identities. Administrator-selected
revocation refreshes the displayed session to revoked, rejects its cookie with a login challenge and makes access
and refresh credentials return 401. The first browser assertion incorrectly followed the cookie challenge to a 200
login page; explicit redirect observation plus the anonymous session endpoint now checks the actual behavior.
The final run also waits for refreshed session state before accessibility scans/screenshots.

Both components have zero WCAG 2 A/AA and 2.1 AA axe violations under Classic light; 390px layout has no horizontal
overflow. The refreshed wide/narrow screenshots were visually inspected. Owned artifacts/receipt:
`/var/folders/7q/m1ycqfk94mq21vm_lzl0ch0c0000gn/T/elsa-3579-browser-Ks68Qf/`.
Logs: `/tmp/elsa-3579-user-access-{tests,types,eslint,build,css,layout,browser,sdk-consumer,identity,architecture,maps}.log`.

T025 is checked for its implementation/component scope. The actual external IdP product, full Studio host,
representative themes/keyboard/retained-host tabs, cross-site topology, server-database, restart/separate-process/shared
nodes, final packages/public consumers/current hosted CI, independent review/merge and resulting-main proof remain
required. Preceding-head CI **41925468861** at `339cff968` was still queued when checked; this checkpoint does not
claim a current hosted result. The full goal remains active and unachieved; #2848/#3579/#2771 stay open.

## Actual provider and reference-host harness checkpoint (5 November)

T026 now has executable owned runners for the controlled HTTP provider, pinned Keycloak 26.8.1, the actual Studio
application and the actual Workbench application. Commands, ownership constraints and profile boundaries live in
the [probe README](../../tools/identity/ExternalAuthentication.Probe/README.md). The Keycloak image is pinned to
`sha256:3f3c5f8e03c614774b4bd9173b88391fefa4153bfc010d8012d59ad825377332`; its two independent instances each use
one CPU/1 GiB, disposable imported accounts/clients and exact callback allowlists. Their subject IDs are deliberately
equal, and their users have an upstream administrator membership. All credentials, signing keys, databases and ports
belong to these invocations. No existing host/store is opened. Teardown verifies container ownership and stops only
owned children; redacted receipts and local screenshots remain available.

The Keycloak mounted-component journey passes, and the combined **Keycloak + actual full Studio + native SQLite**
journey also passes. It uses product capability/authentication discovery, the public SDK transport and real dialogs,
without route mocks. Configure/test/preview/enable, ordinary Reader sign-in, exact attachment, final-user-path
refusal, actual sign-in through the alternative provider, safe removal, denied Reader panels and selected
cookie/access/refresh revocation all pass. Saved Elsa Reader membership remains authoritative; the upstream
administrator membership grants no Elsa permissions. The full Studio Reader also has API-capability read permission
for the ordinary host discovery protocol, independently of link/session permissions.

The actual host's ordinary redirect adapter requests a normal administrator token pair on API calls/reload.
Full Studio therefore changes token rows from **36 to 60** across preview, accounted for by exactly **12** observed
ordinary token requests, all with the unchanged administrator subject. No extra credential rows appear. User/link/
normal-session counts remain **1/0/1**, and the administrator cookie still identifies the same user. The preview
protocol and mounted-component profile retain their strict zero token-row change. This distinguishes ordinary
existing-administrator exchanges from preview-issued credentials instead of claiming zero full-application exchanges.

The actual workspace preserves an unattached subject while switching away and back through retained tabs, then
discards it explicitly. Keyboard theme switching and settled light/dark WCAG 2 A/AA and 2.1 AA axe scans pass with
zero violations. The 390px layout has no horizontal overflow, and its final session action remains reachable by
scrolling. Wide/dark/narrow top and narrow session screenshots were inspected. Artifact scrolling follows the real
host's internal content panel rather than taking an oversized blank document screenshot.

Full-host testing found and fixed the User access heading level and inappropriate use of the danger fill token for
small action/error text. The failing action contrast was **4.33:1**; using the public danger foreground token passes
in both modes. The initial dark scan sampled the host's intentional color transitions; the runner now waits for
finite animations to finish before scanning. Locator corrections distinguish module headings/alerts from host chrome.
The combined receipt is retained at
`/var/folders/7q/m1ycqfk94mq21vm_lzl0ch0c0000gn/T/elsa-3579-keycloak-K5AX1T/keycloak-receipt.json`.
This final repeat also separates the outer Keycloak-runner log from the nested Studio/browser logs.
The controlled-provider full Studio receipt is at
`/var/folders/7q/m1ycqfk94mq21vm_lzl0ch0c0000gn/T/elsa-3579-full-studio-SgWn5d/studio-receipt.json`.

Reference Workbench had not registered the optional managed-authentication assemblies. Its explicit project and
shell-assembly registrations now permit deployment configuration to activate them, without changing default feature
enablement. Actual Workbench shell activation, capability advertisement, normal local login, managed catalog, bounded
user metadata and durable session inspection pass against owned native SQLite IAM/token/artifact/Data Protection
stores. Receipt:
`/var/folders/7q/m1ycqfk94mq21vm_lzl0ch0c0000gn/T/elsa-3579-workbench-qhKP58/workbench-receipt.json`.
Initial fixture failures used the wrong catalog collection name and a random password that did not always satisfy
the normal password policy; both were fixed in the runner without weakening product checks.

Studio host and probe builds pass with zero warnings/errors. Workbench builds with zero errors and existing broad
audit/style warnings. SDK/component **42/42**, typecheck, ESLint and CSS checks pass after the heading fix; the unchanged
Studio host suite previously passed **1055/1055**. The unchanged Identity libraries/API previously passed **895/895**;
discovered and executed counts match. Architecture **616/616** passes with zero skips after graph restore and staged,
frozen C# sources. Its list contains **601** definitions: two runtime-enumerated theories expand from one definition
each to **6** and **11** results, accounting for all **616** executed cases. Server locked restore passes after updating
the Workbench-dependent Modularity lock files. The Workbench solution filter was regenerated for its five additional
projects; the subsequent generated-filter check passes.

T027–T031 remain open for the complete admission/source/UI/deployment matrix, restart/separate-process/shared
server-database nodes, remaining operator documentation and final packaged/public consumers/current required CI.
Independent exact-head review, merge and resulting-main proof remain required. This is an implementation and
qualification checkpoint, not completion of #3579/#2848/#2771 or the active goal.

## Restart and shared-process-store qualification (5 November)

`qualify-nodes.mjs` starts independent Kestrel backend processes, a separate real signed/PKCE OIDC provider process
and an owned loopback routing proxy. Backend processes share only their configured stores, filesystem Data Protection
ring/application name and owner-readable signing key. Node routing keeps the trusted public callback URL stable;
the proxy never manufactures a backend domain response. Technical HTTP clients use independent cookie jars; browser
cookie policy and actual Studio/product behavior are covered by the separate profiles above.

| Engine and mode | Result | Owned root suffix under the system temporary directory |
|---|---|---|
| Native SQLite, database-managed | Passed | `elsa-3579-nodes-sqlite-managed-eIkD2c` |
| Native SQLite, deployment-only | Passed | `elsa-3579-nodes-sqlite-baseline-of5QIL` |
| PostgreSQL 16.15, database-managed | Passed | `elsa-3579-nodes-postgres-managed-K2vAqM` |
| PostgreSQL 16.15, deployment-only | Passed | `elsa-3579-nodes-postgres-baseline-sfaRJi` |

Each root contains `node-receipt.json`, per-process logs and private fixture configuration. PostgreSQL uses pinned
`postgres@sha256:0cefaabd882e42bcb2a2f3dc56d3ce4770560ba62fd7f197f1a2e008fd5731ab`, with the running server version
queried and checked. One CPU/768 MiB and a fresh invocation-specific database user/container isolate each run.
IAM/catalog/artifacts use one private database; the explicit vendor token context uses a second database. Settings
files hold credentials only in the owned root; the probe rejects PostgreSQL outside its loopback fixture namespace.
SQLite qualification is same-machine only and creates no multi-machine SQLite claim.

All four profiles pass start-on-A/complete-on-B after A stops, same-node process restart and unexpired completion,
simultaneous callbacks on A/B with exactly one session/user/link admission, and replay refusal. Equal opaque subjects
from the two provider issuers create distinct Elsa users. Both nodes validate the shared cookie and first-party
bearer. Selected-session revocation rejects independent retained-cookie snapshots, access and refresh credentials
on both nodes; an unrelated provider/user's cookie, bearer and refresh family survive.

Both providers then perform actual normal sign-ins for one explicitly linked account. Concurrent guarded unlink
requests on separate nodes commit exactly one removal; a refreshed retry refuses the final remaining path.
Removed-link cookies/bearers fail on both nodes while credentials through the retained link remain usable. Saved
Elsa Reader memberships remain unchanged. Managed mode also races two same-revision catalog writes with exactly
one winner and observes a disabled explicit override, archive, restore and removal from the other process.
Source changes reject pending flows without changing user/link/session/token counts. A fresh prospective upstream
subject avoids accidental existing-user conflicts in that check. Restoring prior material cannot revive rejected state.

Deployment-only mode installs **no provider-configuration store**, refuses catalog mutation, and uses a restarted
node to observe changed deployment material. Its catalog CAS/lifecycle receipts are null (inapplicable), while IAM,
artifact/session and token persistence remain real. Managed profiles start five processes; baseline profiles start
seven because they additionally roll deployment settings forward and back. Contexts/processes stop before private
custom-format dumps freeze both PostgreSQL databases. Teardown verifies ownership and removes only the container
and its anonymous volumes. SQLite databases and shared keys remain in their owned roots.

The initial runner copied its process wrapper, causing duplicate log-descriptor cleanup; it was corrected to retain
one owning wrapper and remaining owned processes were explicitly stopped. A baseline assertion also incorrectly
treated actor permission as deployment write policy: current manage permission may be true while database management
is disabled. The corrected profile checks those independently and actually proves writes are refused. Neither
correction changed product authorization or persistence behavior.

After extending the upstream fixture with an authorization-time subject snapshot, full Identity **895/895** passes,
zero skips. Probe build passes with zero warnings/errors. Final architecture/maps/whitespace results follow below.
Commands and boundaries are in the [probe README](../../tools/identity/ExternalAuthentication.Probe/README.md).
Logs: `/tmp/elsa-3579-nodes-{sqlite,postgres}-{managed,baseline}-proof.log`,
`/tmp/elsa-3579-node-fixture-identity.log`, `/tmp/elsa-3579-node-probe-build.log`.

T028 is complete for these claimed process/store modes. Complete admission/source/permission/ownership/missing-module
UI acceptance, remaining operator/package/public-consumer/current CI gates, independent exact-head review, merge and
resulting-main proof remain open. These four profiles do not establish universal cross-site or additional server-engine
claims, and do not close #3579/#2848/#2771 or the active goal.

Final frozen-source architecture **616/616** passes, zero skips; generated-filter and staged/worktree whitespace
checks pass. No production library/API behavior changed in this process-qualification increment.

## Full Studio source lifecycle and representative modes (5 November)

T027 and T029 combine the actual Keycloak/reference-host and separate-process receipts above with an extended
controlled-provider full Studio journey. Its owned root is
`/var/folders/7q/m1ycqfk94mq21vm_lzl0ch0c0000gn/T/elsa-3579-full-studio-YhaxYZ`.
`studio-receipt.json` records actual editor changes, disable/archive/restore/removal, complete disabled same-key
override creation, real test/preview and explicit promotion. The ordinary draft leaves the deployment baseline
effective; the disabled explicit override shadows it. Archive/removal reveals the baseline and restore reinstates
the override. No backend restart occurs. The same run preserves the administrator and saved Reader roles, refuses
the final user-link removal, proves the second provider through normal sign-in, safely removes the first link,
refuses reader panels, preserves retained drafts and invalidates selected cookie/access/refresh credentials.

Preview leaves users/links/normal sessions at 1/0/1. As in the earlier full Studio receipt, its normal redirect
adapter makes exactly twelve observed ordinary administrator token-pair exchanges (24 token rows), with the
administrator subject unchanged; preview adds no other credentials. The override preview independently accounts
for the same twelve ordinary exchanges. Isolated protocol/mounted profiles still prove strict zero token-row
change. Preview, provider errors, role delegation, rejection/JIT/verified matching/conflicts, disabled users,
secret rotation/bindings and cross-administrator/purpose/replay isolation remain covered by the actual owned
HTTP and native tests described above, rather than being inferred from screenshots.

Seven further full reference Studio profiles each pass against a fresh actual backend and native SQLite stores:

| Profile | Observed boundary | Owned root suffix under the system temporary directory |
|---|---|---|
| Configuration-only | Deployment-owned connection fields and creation disabled | `elsa-3579-full-studio-configuration-71GVmy` |
| Provider reader | Read-only catalog; link/session panels independently denied | `elsa-3579-full-studio-provider-reader-OMT0kl` |
| Denied provider access | Provider permission guidance; no writable controls or link/session access | `elsa-3579-full-studio-denied-mnhhKl` |
| External-owned | Links read-only and session revocation disabled | `elsa-3579-full-studio-external-owned-DKbUd6` |
| Hybrid | Links read-only; application-authority session revocation available | `elsa-3579-full-studio-hybrid-rnDIlc` |
| Missing managed API | Capability omitted, API returns 404, host unavailable-module view | `elsa-3579-full-studio-missing-api-DVisZH` |
| Missing durable sessions | Local sign-in works; session administration explains unavailable capability | `elsa-3579-full-studio-missing-sessions-HoivOP` |

Each root contains `studio-receipt.json`, browser/backend logs and light/dark/narrow screenshots. All seven
receipts require no external-management writes, zero page errors, zero settled light/dark WCAG 2 A/AA and 2.1 AA
axe violations, keyboard colour-mode switching and no document overflow at 390 pixels. Representative external-owned
dark, missing-API narrow and source-lifecycle screenshots were visually inspected. These are representative UI
profiles, not claims about every possible engine/topology combination. The browser uses real SDK transports,
capability discovery, normal local authentication, saved role permissions and product routing; no domain responses
are mocked. Missing-module profiles omit the actual API/store registration.

The UI metadata check exposed `ownershipMode` returning a record debug string instead of the enum name. The
existing bounded user-directory HTTP regression fails before changing the projection to `ownership.Mode.ToString()`
and passes after it (`/tmp/elsa-3579-ui-ownership-{red,green}.log`). Full current Identity **895/895** then passes,
zero skips (`/tmp/elsa-3579-ui-modes-identity.log`). Probe build passes with zero warnings/errors; browser script
ESLint and Node syntax checks pass. Architecture/generated-filter results follow below.

Operator/module docs now describe implemented administration and schema 1.2.0 exact-link session binding, with
fresh sign-in required for older external credentials lacking it. They route to owned commands and bounded profile
evidence. Existing direct-bearer composition remains separately documented. T030 is complete; T031 and SC-009
still require final distributions/public consumers/current required CI, exact-head independent review, merge and
resulting-main qualification. No issue/program or goal completion is claimed at this checkpoint.


## Current-main integration and allocation correction (5 November)

Pre-integration frozen-source architecture passes **616/616**, zero skips; generated-filter and whitespace
checks pass (`/tmp/elsa-3579-ui-modes-{architecture,maps}.log`). The branch then merges current main at
`074259b35`, including the Parcel Room login design and composed Studio acceptance policy. The merge resolves
cleanly and preserves managed provider chooser rendering. Affected Identity/login and actual composed journeys
must be rerun against the integrated candidate; preceding receipts are not substituted for that proof.

Main already owns `1205-studio-ux-acceptance`, exposing a numbering collision in this branch. After checking
main/archive paths and all open PR file inventories (including paginated PR #1192), the highest allocated number
is 1211. This feature is renumbered to `1212-external-authentication`, with current inbound paths and its local
Spec Kit selection updated. The work and #3579/#2848 scope are unchanged. Historical commit links retain their
original paths. Current receipts now bind clean source/head SHAs and refuse source changes during a run.


The affected integrated login/presentation/user-directory subset passes **67/67**, zero skips, including the new
Parcel Room presentation expectations (`/tmp/elsa-3579-integrated-login.log`). Composed UX tooling **10/10** and
Studio aggregate-gate controls **3/3** pass. Browser ESLint and whitespace checks pass after correcting the new
chooser screenshot's owned-directory variable. CI now runs the full managed Studio journey and all seven actual
mode profiles after the standard composed regression suite, retaining only screenshots and source-bound receipts
in `external-auth-ux-<tested SHA>-<attempt>`. Owned configurations, credentials, databases and keys are excluded.
Independent visual acceptance remains incomplete until the reviewer opens current-head evidence; no author-written
receipt is presented as independent approval.


## Independent review round 1 and corrections (5 November)

Three independent author subagents reviewed exact head `3261576cce3bcceaacfe1426bfd7dc8fe3b268b8` against main
`074259b35`, without exchanging outputs. Correctness found no actionable defect. Standards and acceptance found
six corrections, with an additional standards adjudication requiring public API tracking in all new packages.
The acceptance reviewer opened all eight current-head screenshots plus representative historical modes. UX
acceptance is **failed**, and current downloadable visual evidence is **incomplete** while hosted CI is queued;
no author or agent claim substitutes for the pending refreshed visual review.

Accepted fixes in the following candidate:

- The API and Studio shell features are inheritable and expose virtual `ConfigureServices`. A real separate NuGet
  consumer attempting the override against the preceding archive fails with **CS0506** before the correction
  (`/tmp/elsa-3579-review-r1-feature-red.log`). The packed consumer now includes that derivation for the next pass.
- `AuthenticationStoreException` provides the public artifact/session failure boundary with operation, tenant,
  record and original `InnerException`. Cancellation and schema-version refusals propagate unchanged; duplicate
  and CAS results retain their meaning. A wrapped failure never acknowledges a mutation or implies rollback.
  Wrong-key protected payload failures retain their original cryptographic cause. Seven actual native EF
  read/write/consume failure cases fail before wrapping and pass after it, including unchanged cancellation and
  retry safety. The complete focused artifact/session set passes **22/22**, zero skips. Logs:
  `/tmp/elsa-3579-review-r1-store-{red,green}.log`.
- All five newly introduced packable implementation/Studio modules now enable PublicApiAnalyzers and carry
  baselines. Core already had tracking. Studio wires the analyzer explicitly because its property imports differ
  from the backend. Generated migration protected overrides are tracked too; the analyzer caught twelve missing
  generated members before they were added. No analyzer or generated-source waiver was added. Probe locked
  build and Studio module build pass with **zero warnings/errors**.
- The connection list now reports configured enablement without claiming current sign-in availability. Selection
  and refresh read the current detail/readiness assessment, including for provider readers, separately from the
  prior observed provider test. Known unavailable/unchecked readiness keeps preview/enable unavailable. The
  provider-reader actual-host profile now uses an unavailable secret and checks that assessment without granting
  test or write permission; its refreshed run is pending.
- Refreshing user access reconciles the selected account's current metadata and clears a disappeared selection.
  Refreshed 401/403/404 catalog access clears protected configuration, drafts and observations. Five new UI cases
  fail against the prior behavior. The full SDK/component set now passes **47/47**, with typecheck, ESLint and
  module/standalone SDK build passing (`/tmp/elsa-3579-review-r1-ui-{red,green,types,eslint,build}.log`). Async tests
  wait for the current readiness/dirty state before checking available actions and navigation guards.
- The composed runner explicitly scrolls and captures provisioning, callbacks, preview results and actions in
  light/dark desktop/narrow layouts, plus confirmations. It checks keyboard field traversal and confirmation
  cancellation/focus restoration. These executable corrections have syntax/lint proof; their actual composed
  rerun and new screenshots remain pending. Outer-document full-page images were insufficient for those panels.

The preceding clean-source public package run passes **23** actual production archives and a separate exact-version
NuGet consumer with **zero project references**, at local commit `c67407fdd`:
`/private/var/folders/7q/m1ycqfk94mq21vm_lzl0ch0c0000gn/T/elsa-3579-packages-9ihgqi39/report.json`.
The first invocation correctly stopped on the runner's macOS symlink-normalization bug; the root is now resolved
before containment checks. This preceding package result is not final proof of the review corrections.

T029 is reopened for the accepted UX corrections. New broad local Workbench/Studio/profile runs are queued behind
the immediate demo/User Tasks integration delivery paths under overnight coordination. Focused single-project
checks and review preparation continue. Package/current required CI, refreshed independent exact-head review,
merge and resulting-main qualification remain required. GitGuardian historical incident **39613973** still flags
a fixed policy prefix plus 24 runtime-random bytes in an earlier fixture commit; current source removes the prefix.
Check **126971711386** exposes no requested actions and the session has no GitGuardian incident-management
capability. Its supported workspace false-positive disposition remains an external merge blocker. No history,
check or branch-protection bypass has been used.


After the corrections, the complete affected Identity project passes **909/909**, zero skips, using its freshly
built test assembly (`/tmp/elsa-3579-review-r1-identity.log`, TRX `elsa-3579-review-r1-identity.trx`). This includes
the integrated main login changes and seven new persistence-boundary cases. Studio module and owned probe locked
builds pass with zero warnings/errors; SDK/component **47/47**, typecheck, ESLint, build, Node syntax and archive
verifier self-test pass. CI workflow/document contract and evidence-size checks pass. New full composed/profile
captures and final packed inheritance consumer remain explicitly pending; these focused results do not establish
UX acceptance or final delivery.


## Independent review round 2 follow-up (5 November)

At exact published head `086899d10fb4324995b9085b579638e01a15b788`, independent correctness and standards reviews
find no actionable issues. Standards confirms the inheritance, exception-boundary and API-tracking corrections.
The refreshed actual package run passes all **23** production candidates and its separate exact-version consumer,
including the derived API feature that failed with CS0506 before the fix. Receipt:
`/private/var/folders/7q/m1ycqfk94mq21vm_lzl0ch0c0000gn/T/elsa-3579-packages-een3p80f/report.json`.
This is backend registration/public-contract proof; the final full Studio archive/distribution and consumer gate
remain separate.

Acceptance source review finds two further P2 cases: a successful test did not replace the earlier missing-secret
readiness, and absence from a filtered/paged user result silently discarded the selected user's attachment draft.
The correction reconciles readiness only from a matching non-stale test, invalidating earlier detail requests.
Refresh now confirms before clearing an out-of-result selection with a draft. Cancelling preserves it and requires
finding/refreshing that user before mutation; absence from one page is not presented as deletion. Refresh also
invalidates old panel requests so they cannot reopen actions after cancellation. Five new cases cover repaired/stale
tests, filtering, pagination and explicit discard for an absent account. Four fail against the prior behavior;
the stale-observation case is a preserved guard. The full SDK/component set passes **52/52** after these fixes
(`/tmp/elsa-3579-review-r2-ui-{red,green}.log`). Backend C# is unchanged from the **909/909** and packed-consumer
checkpoint. Fresh composed captures/downloadable evidence and visual completion remain pending in the runtime
queue; UX acceptance is **incomplete**, not passed. A fresh independent exact-head round is still required.


## Current UI proof and cancellation correction (5 November)

Under the owner's UI/demo priority, fresh composed login/provider-editor/user-access proof resumes without new
backend scope or unrelated persistence/topology matrices. The current reference Studio builds with zero warnings
or errors. The rebuilt probe succeeds with generated migration/nullability and existing style/audit warnings;
those warnings are retained rather than suppressed.

The first new full Studio attempt at `15f07d439` reaches the lower editor panels and passes desktop dark axe,
then correctly fails because the runner tries to select hidden desktop colour controls after narrowing the
viewport. Owned root `elsa-3579-full-studio-5p0fgZ` retains that failed evidence. The runner now selects colour
mode through the actual desktop control before resizing, with no DOM theme override.

The next committed attempt at `466e6e317` exposes a real cancellation defect: after cancelling Enable sign-in,
its re-enabled action button does not regain keyboard focus. The asynchronous assessment disables and blurs the
opener before the shared dialog captures focus. Owned root `elsa-3579-full-studio-7UQL7c` retains the failed
Playwright focus assertion and screenshots. A local hook now restores the captured opener after re-enablement,
only for the same backend/context and a connected element. Connection changes, provider preview and user-access
link/session cancellations use it; confirmation still performs no mutation when cancelled.

Two component regressions fail before this correction. The full SDK/component suite then passes **54/54**, with
TypeScript, ESLint and module/SDK build passing (`/tmp/elsa-3579-ui-cancel-focus-{red,green,types,eslint,build}.log`).
Backend C# and the provisioning-policy-only decision are unchanged. Fresh composed execution and independent
visual acceptance are still required; the failed attempts have not been reclassified as passing.

## Independent review round 3 and settled confirmations (5 November)

At `cb373530d`, the complete composed Studio journey and all seven representative UI profiles pass, with clean
source-bound receipts. Correctness and standards reviewers find no actionable issue. The acceptance reviewer
opens 54 current screenshots and all eight receipts, and finds two actionable presentation/evidence defects:
long generated usernames overflow the shared confirmation surface, and several screenshots capture its opening
animation or the unfinished removal UI. These passing runtime assertions therefore do not establish visual
acceptance. Downloadable current-head evidence is also still required.

Correction `2dbdcca6a` gives the shared dialog body a shrinkable minimum width and wraps long message/title
tokens. Four actual-browser regressions with generated usernames, long issuers and opaque subjects fail before
the fix and pass afterward, covering 1440px/390px and light/dark. The host's **1,055/1,055** component tests,
browser-spec TypeScript, affected ESLint, host asset build and reference host build pass; the latter reports
zero warnings/errors. Logs: `/tmp/elsa-3579-dialog-{wrap-red,wrap-green,host-unit,types,eslint,host-assets,host-build}.log`.

The composed runner now waits for finite dialog animations and fonts, checks horizontal dialog/message/action
bounds before capturing all 19 confirmations, and waits for the completed lifecycle notice, enabled Refresh,
empty selection and two baseline connection rows before its final capture. A fresh clean-source full Studio
journey passes at `2dbdcca6a` in owned root `elsa-3579-full-studio-Yl0XEU`. All light/dark axe checks remain clear;
saved Reader roles, guarded attachment/removal, selected cookie/access/refresh revocation, retained draft and
explicit override lifecycle pass. Preview preserves user/link/session counts; its administrator's 13 ordinary
token exchanges account for the 26 extra token records. The author opens the corrected long-username removal,
preview and completed-lifecycle screenshots and confirms the clipping/transient captures are gone.

The next exact-head independent round and downloadable composed evidence remain open. Hosted CI has not
started its queued jobs. The approved narrow exception for historical GitGuardian incident 39613973 /
occurrence 310433699 is recorded in [PR comment 6647956479](https://github.com/elsa-workflows/elsa-foundation/pull/3806#issuecomment-6647956479).
The `2dbdcca6a` scanner check still fails for that same sole occurrence; its current fixture is byte-identical
to removal commit `abfef8dc9` (SHA-256 `bab4f00c03c25742d715784d4dc4590f6aad30034f1b3bf04b04041ff31b38df`).
No scanner success or protection bypass is claimed. Required checks, all other findings, final review,
distribution/consumer evidence, merge and resulting-main qualification remain mandatory.

The same composed journey passes again after evidence-only commit `6ecf3261b`, in owned root
`elsa-3579-full-studio-6fjE50`. At that head, four actual module/support archives and independent NuGet C# and
React-independent extracted-SDK consumers also pass, with zero project references or .NET-consumer frontend
tool invocations (`/tmp/elsa-3579-final-rcl-consumer.log`). This is the module closure, not the complete Studio
distribution. The final source-bound profiles and independent review are retained on the PR without further
evidence-only source changes while validation is pending.

## Final local qualification and ready-PR prerequisite (5 November)

At clean source `463eb332bf09e4cb55a4a1f8029db4f31ace37aa`, the complete fresh Studio distribution passes:
26 Studio archives, nine support archives and all 16 precise consumer/composition/lifecycle observations,
including expected failure controls and successful restored outcomes. The final batch follows the full
frontend rebuild and checks all 3,666 generated inputs after execution, with zero mismatches. An earlier
passing batch is intermediate evidence because 89 cached Weaver-only inputs changed on that rebuild;
shared hosting and external-authentication inputs were identical. The exact final module archive also
passes independent C# feature-inheritance and extracted React-independent SDK consumers, with zero project
references or .NET-consumer frontend invocations. The generated solution-filter graph check passes.

The standard composed Studio gate passes 8/8, with zero skipped, unexpected or flaky cases. The independent
acceptance reviewer opens all 56 canonical standard screenshots and compares them with the 70 previously
inspected custom screenshots. No additional actionable UI/source findings remain; the local reference-capture
gap is resolved. All three round-four lenses find no actionable source/UI issues at that exact head. These
are local results; formal UX acceptance still requires downloaded and inspected current Actions evidence.
The bounded reports and reviewer receipt are retained in
[round-four addendum 6648625814](https://github.com/elsa-workflows/elsa-foundation/pull/3806#issuecomment-6648625814).

Hosted CI subsequently starts: its generated-map check is green, and other checks are running or queued.
Main prerequisite `aa2712cc3d1757b3ae11b58de927a4538d0982dc` (#3867) is integrated so a ready transition
reports all required contexts together in one run. Its changes are CI routing/tests and contributor notes;
the authentication, Studio and probe production trees are unchanged from `463eb332b`. The final exact head,
checks and downloadable evidence are recorded on the PR after this integration.

The spec's `Implemented` marker is prospective in the implementation PR, as the lifecycle policy requires:
it becomes true on merge. Its terminal process checklist is removed. The still-open gate and resulting-main
tasks are not marked complete in advance. [Resulting-main qualification](resulting-main-qualification.md)
defines the post-merge proof; actual results remain pending until the protected merge occurs.
