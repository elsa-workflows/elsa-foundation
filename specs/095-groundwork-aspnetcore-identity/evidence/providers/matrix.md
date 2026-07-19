# ASP.NET Core Identity Groundwork provider evidence

Current accepted evidence target: Groundwork `0.0.1-preview.60`, Identity manifest `1.0.4`.

Spec task: T066.

## Preview.60 accepted evidence generation

The accepted provider evidence was generated from clean committed candidate
`211ab4816b41c153d795c93fc3f72c872ecb4875` (tree
`35b19f8b4f37dc4e80dffae8046aa7fb59b306e1`). Groundwork packages, provider assemblies, and the
repository-local tool are all `0.0.1-preview.60`. The immutable generation identifier is
`1d58175a573d490644823b9242c53fdaf6dd95e8f8378c8db686d8c06f176ba7`; consumers resolve it only
through `current.json`, whose SHA-256 is
`c01b23038a57d99b82d8446f66065a450dc7eeb0df50ccb5ea351b2826653c1b`.

The all-provider publisher ran sequentially and published `current.json` only after all four
provider bundles passed validation:

```bash
ELSA_GENERATE_IDENTITY_PROVIDER_EVIDENCE=1 \
  dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  -c Release \
  --filter FullyQualifiedName~Generate_all_preview60_provider_artifacts_only_after_the_complete_matrix_passes
```

Result: `1/1` passed in `8m16s`. The checked-in artifact validator then passed `1/1`:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  -c Release --no-build --nologo \
  --filter FullyQualifiedName~Checked_in_provider_artifacts_are_complete_sanitized_and_share_one_tested_code_candidate
```

| Provider | Identity | Topology | Native classification | Artifact SHA-256 |
| --- | --- | --- | --- | --- |
| SQLite | `groundwork-sqlite` | `file-backed-distinct-connections` | `index-search` | `69672c32edda5b31f77777df897680786b83d0b3985cdfdca205a42f80809681` |
| SQL Server | `groundwork-sqlserver` | `real-sqlserver-container` | `index-seek` | `f491e0953acf44effba6073d85c061415ecdfe60e5444c6a518fa9a45fbcace5` |
| PostgreSQL | `groundwork-postgresql` | `real-postgresql-container` | `indexed` | `3b1cb4ef937513b41122dc49bba9b7170c11921292cf403c93f75119a55ac4ef` |
| MongoDB | `groundwork-mongodb` | `transaction-capable-replica-set` | `index-scan` | `09a94218b4f270cf7412f29d0db77715e1b6f66eca0afd5eab53fc53fabbe51a` |

Every artifact records the exact closed catalog of `25` objectives and all `15` advertised
framework capabilities, confirms external-process restart evidence, explains all `10` production
routes across `6` physical tables at `100,000` physical rows per table, and contains the exact `7`
schema-tool receipts. All four share workload input fingerprint
`5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9` and result digest
`32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc`.

The bundles retain only allowlisted metadata and SHA-256 digests of native plans. A precise scan
confirmed that no raw native plan, runtime invocation fingerprint, route value, storage scope,
candidate content, connection string, or process identifier was persisted. Publication fault tests
passed at the staging-written, generation-installed, and pre-pointer-swap boundaries; an interrupted
generation therefore cannot become the current accepted generation.

## T084 acceptance-status correction

The T066/T083 commands, package versions, test names, timings, and TRX hashes below remain useful historical provenance. The independent T084 audit invalidated the stronger acceptance conclusions previously drawn from them:

- the four provider entry points were separate, narrow scenarios rather than one provider-independent FR-013 contract suite;
- they did not calculate or compare the canonical public scenario digest shown below;
- the checked-in evidence directory contained this matrix only, not four durable provider artifacts;
- provider-native plans covered normalized user-name lookup only;
- the T062 `100,000` value was a substituted result count over a small in-memory dataset, not 100,000 physical provider records;
- three declared routes were not exercised by the runtime bounded-route test; and
- schema CLI/runtime fingerprint and independent read-only evidence was SQLite-only.

Consequently the preview.56 matrix below is historical execution provenance, not current proof of FR-013, SC-003, SC-005, SC-007, or SC-010. The remediation candidate implements a 25-objective exact-set catalog covering all 15 advertised framework capabilities with no deferred objectives, real 100,000-record native-route paths, highest-seam acceptance, mutation-receipt failure/cleanup coverage, and four-provider schema-parity/read-only machinery. T083-T085 replaced the historical conclusions with the preview.60 exact-candidate provider-specific sanitized generation recorded above.

Preview.59 and earlier provider and SC-010 runs are historical regression evidence because the dependency advanced again before ratification. Elsa retains the generic codec boundary from PR #88 and provider-native ordered bounded-query explanations from PR #89 while keeping Elsa policies and concrete upcasters behind the Elsa marker. Do not rewrite a historical run to preview.60 or carry forward its manifest fingerprint; the current fingerprint must be calculated from the exact preview.60 candidate.

The provider matrix was reverified on `codex/095-groundwork-aspnetcore-identity` after the preview.56 schema-tool remediation. The local execution provenance is the full conformance TRX; raw TRX files remain ignored because they contain machine-specific paths. This matrix is the only checked-in spec-095 provider evidence file at the T084 audit point:

- Test run: `artifacts/test-results/spec095-t083/conformance-preview56/spec095-t083-conformance-preview56.trx`
- SHA-256: `a2cab2e1b2bb3d92816788cebfda1d36a64d00faf344577547681a2b9a4a4556`
- Result: 52 passed, 0 failed
- Command:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build --no-restore -p:WarningsNotAsErrors=NU1603 \
  --logger "trx;LogFileName=spec095-t083-conformance-preview56.trx" \
  --results-directory artifacts/test-results/spec095-t083/conformance-preview56
```

## Historical intended public scenario digest

The tests used the following intended public Identity authority scenario metadata, but the four provider entry points did not execute the complete catalog or calculate this digest from their observations:

- Scenario: `identity-authority-baseline`
- Canonical public result digest: `719708f5192bc589a05bd319468b55e62c25801b9c45b6a02c5ec4770b4a9c49`
- Canonical input fingerprint: `5d60763e3cbebda467e8a8375411a1b2fc0b51d8329222628fc548bc72e7ea35`
- Public observations:
  - `tenant-scope-count=3`
  - `user-count=5`
  - `role-count=3`
  - `claim-count=3`
  - `login-count=2`
  - `token-count=3`
  - `membership-count=2`
  - `unicode-case-count=12`
  - `primary-user-id=user-ada`
  - `primary-role-id=role-administrators`

The digest definition is provider-independent and excludes tenant-specific, connection-specific, credential, process, and container values. The historical matrix did not prove that each provider preserved this externally observable result.

## Provider collection matrix

| Provider | Provider identity | Groundwork package | Topology | Container/image evidence | Scenario test | Native route evidence | Restart evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SQLite | `groundwork-sqlite` | `Groundwork.Sqlite` `0.0.1-preview.56` | `file-backed-distinct-connections`; persistent storage, independent clients, multi-document transactions, external process restart | File-backed SQLite database under a temporary driver directory | `File_backed_sqlite_physical_identity_scenario_survives_reopen_uses_native_plan_and_process_restart` passed in `00:00:12.4287804` | Native plan contains `table=identity_users` and `USING INDEX` | Child-process normalized lookup and duplicate-create probes completed with positive child process ids |
| SQL Server | `groundwork-sqlserver` | `Groundwork.SqlServer` `0.0.1-preview.56` | `real-sqlserver-container`; persistent storage, independent clients, multi-document transactions, external process restart | `mcr.microsoft.com/mssql/server:2022-CU21-ubuntu-22.04`; Testcontainers `4.12.0` | `SqlServer_physical_identity_scenario_uses_native_plan_survives_restart_and_rejects_duplicate_race` passed in `00:02:22.9198204` | Native plan contains `provider=sqlserver`, an index seek/scan, and declared compound key bytes within the manifest budget | Child-process normalized lookup and duplicate-create probes ran outside the parent process |
| PostgreSQL | `groundwork-postgresql` | `Groundwork.PostgreSql` `0.0.1-preview.56` | `real-postgresql-container`; persistent storage, independent clients, multi-document transactions, external process restart | `postgres:17.6-alpine3.22`; Testcontainers `4.12.0` | `PostgreSql_physical_identity_scenario_uses_native_plan_survives_restart_and_rejects_duplicate_race` passed in `00:01:04.2275830` | Native plan contains `provider=postgresql` and `Index` | Child-process normalized lookup and duplicate-create probes ran outside the parent process |
| MongoDB | `groundwork-mongodb` | `Groundwork.MongoDb` `0.0.1-preview.56` | `transaction-capable-replica-set`; persistent storage, independent clients, multi-document transactions, transaction-capable Mongo topology, external process restart | MongoDB replica-set driver topology; Testcontainers `4.12.0`; standalone probe retained as rejection evidence | `MongoDb_replica_set_physical_identity_scenario_uses_winning_plan_survives_restart_and_rejects_standalone` passed in `00:02:23.2318456` | Winning plan contains `provider=mongodb` and `IXSCAN` | Child-process normalized lookup and duplicate-create probes ran outside the parent process; standalone topology rejected with `observed-topology=standalone` and `outcome=rejected` |

## Historical T062 structural native-route verdict

T066 retained the T062 reruns as structural route evidence. T084 found that they are not physical 100,000-record provider evidence:

- Conformance TRX: `tests/Elsa/Persistence/Groundwork/Conformance/Tests/TestResults/spec095-us3-t062-conformance-rerun.trx`
- SHA-256: `df4719012561a863d7a49ef8720d8fb33e12177951146df5d34d3f8edf2bb75a`
- Result: 3 passed, 0 failed
- Identity Groundwork suite TRX: `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/TestResults/spec095-us3-t062-identity-groundwork-rerun.trx`
- SHA-256: `bad28ac0d7cf540b352730a4c44f0ce65fac5d60144512b872cc162d195910a0`
- Result: 57 passed, 0 failed

Historical reported dataset size: 100,000. The test substituted this value as `TotalCount` over a small in-memory target dataset; it did not persist 100,000 records.

The manifest test asserted every declared route to:

- use a declared physical bounded query;
- bind storage scope as the leading predicate;
- bind the scale-bearing field as the next predicate;
- project the predicate field into the physical table;
- use `BoundedQueryExecutionClass.ScaleBearing`;
- issue a finite `Take` before materialization;
- declare the ingredients intended to avoid unbounded scans and post-materialization tenant filtering.

The runtime test observed finite calls for only a subset; `find-user-by-login`, `list-user-tokens`, and `find-tenant-membership` were declared but not runtime-observed. No conclusion about provider-native execution at 100,000 physical records is retained.

Required native routes:

| Document kind | Query identity | Predicate field | Runtime bound |
| --- | --- | --- | --- |
| `identityUser` | `find-user-by-normalized-name` | `normalizedUserNameKey` | `Take=1` |
| `identityUser` | `find-user-by-normalized-email` | `normalizedEmailKey` | `Take=2` |
| `identityRole` | `find-role-by-normalized-name` | `normalizedRoleNameKey` | `Take=1` |
| `identityRole` | `list-roles-by-tenant` | `tenantId` | `Take=512` |
| `identityExternalLogin` | `find-user-by-login` | `loginKey` | physical route declared |
| `identityUserClaim` | `list-user-claims` | `userId` | `Take=512` |
| `identityUserClaim` | `find-users-by-claim` | `claimKey` | `Take=512` |
| `identityRoleClaim` | `list-role-claims` | `roleId` | `Take=512` |
| `identityUserRole` | `list-user-roles` | `userId` | `Take=512` |
| `identityUserRole` | `list-role-users` | `roleId` | `Take=512` |
| `identityExternalLogin` | `list-user-logins` | `userId` | `Take=512` |
| `identityUserToken` | `list-user-tokens` | `userId` | physical route declared |
| `identityTenantMembership` | `find-tenant-membership` | `membershipKey` | physical route declared |

## Matrix test names retained in TRX

- `AspNetCoreIdentitySqliteProviderTests.File_backed_sqlite_physical_identity_scenario_survives_reopen_uses_native_plan_and_process_restart`
- `AspNetCoreIdentitySqlServerProviderTests.SqlServer_physical_identity_scenario_uses_native_plan_survives_restart_and_rejects_duplicate_race`
- `AspNetCoreIdentityPostgreSqlProviderTests.PostgreSql_physical_identity_scenario_uses_native_plan_survives_restart_and_rejects_duplicate_race`
- `AspNetCoreIdentityMongoDbProviderTests.MongoDb_replica_set_physical_identity_scenario_uses_winning_plan_survives_restart_and_rejects_standalone`
- `AspNetCoreIdentityNativePlanTests.Identity_manifest_declares_physical_bounded_routes_for_every_normalized_and_relationship_route`
- `AspNetCoreIdentityNativePlanTests.Identity_stores_issue_finite_bounded_queries_for_native_routes_at_acceptance_scale`
