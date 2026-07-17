# Research: Groundwork ASP.NET Core Identity

## R1 — One Authority, Two Consumption Shapes

**Decision**: Evolve `Elsa.Foundation.Identity.Persistence.Groundwork` into the sole Groundwork authority for user, role, external-login, and tenant-membership state. Add `Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork` as the ASP.NET Core Identity adapter over those same documents. Elsa IAM ports and framework managers never persist parallel copies.

**Rationale**: The current default host is split: the EF feature registers framework stores first, then unified Groundwork registration replaces only Elsa IAM stores. The admin user therefore lands in EF while the role and Elsa projections land in Groundwork. The existing Foundation Identity Groundwork package already owns the correct domain/provider boundary; the new adapter package isolates the ASP.NET Core dependency without duplicating storage.

**Alternatives considered**:

- Put all framework stores in the existing Foundation Identity Groundwork package: rejected because it forces ASP.NET Core dependencies on IAM-only consumers.
- Put a second complete authority model in the new ASP.NET Core Groundwork package: rejected because Elsa and framework identities would diverge again.
- Keep EF as the framework authority and Groundwork only for Elsa projections: rejected by ADR 0042 and because cross-database atomic consistency is impossible.

## R2 — Explicit Feature Selection, Never Activation-Order Replacement

**Decision**: Unified provider registration supplies the selected Groundwork substrate and schema composition but stops unconditionally replacing Foundation Identity services. `FoundationIdentityAspNetCoreIdentityGroundwork` explicitly registers framework stores, Elsa adapters, the authority manifest, and seeding. Composition validation rejects Groundwork plus EF Identity and duplicate store implementations before public resolution.

**Rationale**: The current configured order makes last-registration-wins a correctness bug. Explicit selection turns identity persistence into a replacement contract with a diagnosable conflict.

**Alternatives considered**:

- Depend on JSON feature ordering: rejected as fragile and constitutionally forbidden silent DI behavior.
- Make every unified provider automatically depend on the Groundwork Identity feature: rejected because runtime/design-only hosts must not be forced to activate Identity.
- Remove EF immediately: rejected because #646 still needs the frozen oracle.

## R3 — Physical Entity Tables With Scalar Link Documents

**Decision**: Use physical entity tables for the seven query-bearing authority kinds and dedicated document-type tables for primary-ID-only user tokens, tenant memberships, and name/email reservations. Keep every query-bearing index scalar and explicitly bounded. The current twelve-unit manifest is version `1.0.4`, with exactly 10 live application routes plus one bounded expiry-maintenance route; deterministic external-login subject, token, membership, and reservation reads use primary IDs rather than redundant secondary routes.

**Rationale (historical planning baseline)**: Groundwork preview.55 supported typed compound physical fields, unique indexes, range/equality predicates, ordering, paging, Count/Any/First, four-provider physical materialization, and scoped identity when this decision was made. It did not provide generic multi-value indexes. Separate link documents satisfy role/claim/login/token lookup directions and remain portable across relational and document providers. The current dependency and capability verdict use preview.60 below.

**Alternatives considered**:

- Embed every relationship in user/role documents and require multi-value indexes: rejected because generic Groundwork multi-value indexes do not exist.
- Keep all records in one shared document table: rejected for the selected hot normalized lookups and because the program requires evidence for physicalized lanes.
- Add a Groundwork multi-value API before #644: rejected because scalar link documents solve the concrete Identity workload; OpenIddict will reassess its separate need.

## R4 — CAS-Protected Child Registries For Atomic Delete And Mutation

**Decision**: User and role authority documents retain deterministic child-ID registries. Every relationship mutation stages the link document plus affected owner registries in one Groundwork unit of work with expected envelope versions. Delete loads dependents by registered IDs inside the same unit of work and deletes them atomically.

**Rationale**: Groundwork UoW supports load-by-ID, conditional save/delete, and cross-unit transactions on all four accepted providers, but it does not run bounded queries inside a transaction. Registries provide a transaction-safe dependent set without adding a new upstream primitive.

**Alternatives considered**:

- Query dependents before opening the transaction: rejected because a concurrent link can appear after the query and become orphaned.
- Add transactional bounded queries/cascades upstream: reserved only if registries prove unworkable; no current blocker justifies expanding Groundwork.
- Embed all relationships: rejected by R3 query requirements.

## R5 — Ambient Tenant Binding Before Manager Lookup

**Decision**: `AspNetCoreIdentitySignInService` binds the effective tenant to the scoped provider-neutral persistence access context before `UserManager` lookup. Every Groundwork store acquires an ordinary session from that immutable scope and validates any tenant carried on the entity. Cross-tenant administrative/seeding paths use an explicit privileged scoped purpose.

**Rationale**: ASP.NET Core Identity lookup methods do not carry tenant IDs. The current global lookup followed by `TenantId` comparison leaks the boundary and cannot support the same username in two tenants. The existing `Elsa.Persistence.Core` accessor/binder is the approved provider-neutral seam.

**Alternatives considered**:

- Encode tenant into normalized names: rejected because it corrupts framework-visible normalization and repeats the old role-name workaround.
- Query globally then post-filter: rejected as a security and scale violation.
- Add Groundwork concepts to Identity abstractions: rejected by the core boundary.

## R6 — Envelope Version Is Concurrency Authority

**Decision**: On load, encode the Groundwork document envelope version as an opaque public `ConcurrencyStamp`. On update/delete, decode it as `ExpectedVersion`; rotate it after success and update the caller object. Relationship operations that advance owner versions also refresh the in-memory user's stamp so the manager's subsequent update uses the successor version.

**Rationale**: An application-generated stamp alone cannot perform provider-atomic CAS. Groundwork already supplies monotonic version evidence and precise create-only/stale outcomes.

**Alternatives considered**:

- Store and compare a GUID stamp only in the JSON body: rejected as read-check-write.
- Unconditional upsert followed by stamp rotation: rejected because stale updates can overwrite successors.
- Expose numeric Groundwork versions directly: rejected because provider mechanics should remain opaque to framework consumers.

## R7 — Deterministic Identity And Conflict Mapping

**Decision**: Use create-only physical uniqueness for tenant-normalized usernames/roles and deterministic scoped IDs for login, token, and user-role links. Preflight bounded lookups provide friendly Identity errors; a race-triggered generic Groundwork uniqueness conflict is reconciled by exact reload and translated to `DuplicateUserName`, `DuplicateRoleName`, login conflict, or concurrency failure without blind retry.

**Rationale**: Groundwork intentionally reports a generic concurrency result for native unique violations. Re-reading exact evidence is sufficient to classify the domain conflict safely.

**Alternatives considered**:

- Depend on provider exception text/index names: rejected as non-portable infrastructure leakage.
- Preflight only: rejected because it cannot close races.
- Change Groundwork result metadata first: useful ergonomic follow-up, not a blocker.

## R8 — Non-Unique Email Fails Closed When Ambiguous

**Decision**: Normalized email lookup requests at most two tenant-scoped matches. Zero returns not found, one returns that user, and multiple matches produce no sign-in candidate. When `RequireUniqueEmail` is enabled, a conditional email-reservation document enforces race-safe tenant-local uniqueness.

**Rationale**: The current host defaults to non-unique email but also permits email login. Returning an arbitrary user for a duplicated email risks authenticating the wrong account; deterministic generic failure is safe and preserves username login.

**Alternatives considered**:

- Always make email unique: rejected because it changes the existing Identity option contract.
- Return the first ordered match: rejected as unsafe ambiguity.
- Rely on an optional unique physical index: rejected because the physical manifest must remain deterministic while the host option can vary.

## R9 — Advertise Only Complete Identity Capabilities

**Decision**: Implement user CRUD, password, security stamp, email, lockout, phone, two-factor, login, claim, role, authentication token, authenticator key, and recovery-code interfaces; implement role CRUD and role claims. Do not implement `IQueryableUserStore`, `IQueryableRoleStore`, `.NET 10` passkeys, or the protected-user marker.

**Rationale**: `UserManager` and `RoleManager` discover optional behavior from implemented interfaces. Advertising a capability that scans, client-evaluates, or throws later is false readiness. The required non-queryable capabilities fit the scalar document model.

**Alternatives considered**:

- Match every interface exposed by EF: rejected because queryable and passkey capabilities are unused and violate bounded-query policy.
- Omit phone/two-factor/authenticator/recovery behavior: rejected because the current full EF store advertises these supported default-provider capabilities and Groundwork can implement them completely.

## R10 — Provider-Neutral, Concurrent Seeder

**Decision**: Move seed options and the account/role coordinator out of the EF namespace. Remove schema migration from the seeder; runtime readiness validates only. Seed role, user, grants, membership, and links with create-only/CAS operations, reload an exact winner on conflict, and expose the same instance through `IHostedService` and `IShellInitializer` with secret-safe logging.

**Rationale**: Schema application belongs to Groundwork Tool. Find-then-create is not concurrent-start idempotency. The rest of the current seeder—configuration validation, wildcard/catalog grants, dual lifecycle, password policy, and log behavior—is provider-independent and valuable.

**Alternatives considered**:

- Keep one provider-specific seeder per implementation: rejected as duplicated domain behavior.
- Auto-apply Groundwork schema in the seeder: rejected by the ratified deployment-owned schema boundary.
- Ignore duplicate startup failures: rejected because concurrent hosts are normal and must converge.

## R11 — Reuse The Four-Provider Driver, Generalize Only Its Physical Seam

**Decision**: Reuse the spec 094 SQLite, SQL Server, PostgreSQL, and MongoDB replica-set drivers. Generalize their physical-manifest/open-client and process-probe entry points just enough to run the Identity manifest and scenario protocol. Share one container per provider collection and reset between cases.

**Rationale**: The drivers already prove independent clients, reset, topology admission, disposal/reopen, and sanitized evidence. Creating an Identity-specific container harness would duplicate expensive infrastructure and repeat the one-container-per-test performance mistake.

**Alternatives considered**:

- Use only in-memory Groundwork tests: rejected as no durability or provider evidence.
- Create new Identity provider drivers: rejected as needless duplication.
- Reuse only a generic save/load child process: rejected because process restart must prove Identity lookup/uniqueness behavior.

## R12 — Land Correctness Before Timing And Final Deletion

**Decision**: #644 lands the Groundwork implementation, four-provider result digests, native plans, and correctness workload while the EF implementation remains unchanged and separately selectable. #646 times identical workloads and selects the accepted physical shape. #647 switches reference hosts and removes every EF artifact.

**Rationale**: Timing before semantic equivalence is invalid, and deleting the oracle before #646 makes the agreed comparison impossible. Simultaneous activation is still forbidden and fixed in #644.

**Alternatives considered**:

- Delete EF in #644: rejected because it blocks #646.
- Maintain EF indefinitely as an option: rejected by the zero-EF completion condition.
- Add new EF tests/migrations to improve the oracle: rejected by the shrink-only ratchet.

### T084 evidence-status correction

R12's ownership decision remains ratified. At the T084 audit point, the branch had not yet produced the evidence the decision requires: the four provider entry points did not run one complete shared catalog or calculate lifecycle result digests; the native-plan acceptance dataset was simulated in memory; and the temporary EF "oracle" returned fixed metadata rather than executing EF. Therefore the historical T066-T083 runs do not prove four-provider semantic equivalence, physical 100,000-record native-plan acceptance, or live EF equality. T085 introduced the first exact-set shared acceptance catalog and the later bounded slices supplied the physical dataset, complete native plans, highest seams, and four-provider schema evidence. #646 continues to own live EF/Groundwork comparison and timing, and #647 continues to own deletion.

The accepted successor candidate `1aed4f5989b9aed0ddb9837a61597d4cb584fbaa` defines 25 required provider-independent objectives, all 15 advertised framework capabilities, and no deferred objectives. It also contains real 100,000-record provider-native route-plan paths, mutation-receipt cleanup, highest-seam acceptance, and four-provider schema-parity machinery. T083-T085 retain the matching preview.60 provider generation `4c541bf48f087c5073dd4f39a88bdce542651e2e6453d9e3d060c951e93a1f9f`, which restores the #644 Groundwork provider/correctness gate. Preview.56-preview.59 executions remain historical regression evidence; live EF equality and every timing claim remain open for #646.

## R13 — Preserve Objectives, Not EF Mechanics

**Decision**: Keep every current EF Identity test through this unit. Extract its still-valid objective into provider-neutral/Groundwork contract tests. The legacy `User_Save_Is_An_Upsert` objective remains present until an architect-approved row records its replacement by create-only plus expected-version behavior; #647 performs the deletion.

**Rationale**: The constitution allows fixture and project movement but requires explicit approval before test removal. Keeping the oracle tests avoids pretending approval and gives #646 stable parity evidence.

The EF implementation is frozen by behavior, schema, dependencies, objective set, and a deterministic source-tree fingerprint. Provider-neutral seed/workload inputs or result types may move outside the EF namespace only without changing that reviewed tree. The checked-in `AspNetCoreIdentityEfContractBaseline` describes expected observable inputs/results but does not execute EF or establish equality; #646 owns that execution. Revision-aware Elsa IAM semantics are introduced additively: Groundwork consumes the new capability while in-memory and EF implementations retain their existing contract through a compatibility path and gain no new behavior.

**Alternatives considered**:

- Delete EF tests as soon as Groundwork tests exist: rejected by the golden refactor rule and #646 dependency.
- Preserve unconditional upsert in the new store: rejected because it contradicts ratified concurrency semantics.

## Capability Verdict

Groundwork `0.0.1-preview.60` has no hard upstream blocker for this design. It supplies scoped physical identity, typed compound unique indexes, bounded queries, create-only/CAS save and delete, relational transactions, MongoDB snapshot/majority transactions, four-provider schema materialization, truthful MongoDB replica-set admission, explicit schema-tool manifest-type selection, the generic version-aware codec contract delivered by Groundwork PR #88, provider-native ordered query explanations delivered by Groundwork PR #89, and preview.60 schema admission/apply behavior. Elsa-specific codec policies and concrete upcasters remain behind the Elsa marker in Elsa provider packages; Groundwork does not acquire Elsa domain knowledge, and core contracts remain Groundwork-free. The implementation must respect SQL Server's finite index-key budget and each provider's parameter/identifier limits. A future Groundwork improvement could attach violated-index identity to unique conflicts, but exact reconciliation is sufficient for #644.

## Primary Evidence

- `docs/reports/identity-openiddict-groundwork-contract-inventory.md`
- `specs/094-harden-groundwork-stores/{plan.md,research.md,contracts/,coverage-ledger.json}`
- `src/Elsa/Foundation/Identity/AspNetCoreIdentity/`
- `src/Elsa/Foundation/Identity/Persistence/Groundwork/`
- `src/Elsa/Persistence/Groundwork/Scoping/`
- `tests/Elsa/Persistence/Groundwork/Testing/`
- Historical: Groundwork source commit `093cc124ce5d021fa750b7c0a156a7c6c5bedf3a` released as `0.0.1-preview.56`; this includes the explicit manifest-type loader remediation from Groundwork PR #86.
- Current dependency: Groundwork `0.0.1-preview.60`, including the generic version-aware codec contract from Groundwork PR #88, provider-native ordered query explanations from Groundwork PR #89, and preview.60 schema admission/apply behavior. The release commit must be recorded from the published package provenance rather than inferred here.
