# Validation Guide: OpenIddict Groundwork Stores

This guide is the completion evidence sequence for Spec 106. It does not authorize EF deletion until every gate below passes on the exact reviewed head.

## Prerequisites

- .NET 10 SDK and Docker-compatible provider runtime.
- Public, mutually compatible Groundwork Core/Documents/provider/Tool packages from the exact reviewed head; record package hashes and `dotnet groundwork --version` output.
- SQLite, SQL Server, PostgreSQL, and MongoDB fixtures. Mongo scenarios requiring a multi-record unit of work run only against a replica set or sharded topology.
- Provider connection values supplied through environment/configuration, never committed or shown in process-visible command arguments.

## 1. Verify the upstream admission gate

Restore and build the new adapter package, then run focused capability probes for codec admission, physical entity definitions, schema operations, typed compound/range routes, multivalue declarations, bounded mutation count/cancellation, native mutation-plan evidence, CAS, and UoW. Record the exact provider/tool versions and outcomes in the feature evidence.

Expected: every required public capability succeeds. A missing or non-public capability blocks the feature and is linked to an upstream Groundwork work item.

Partial execution status: the exact configured package/tool family passed 6/6
focused probes. The probe applies the public parameterless OpenIddict manifest
through a real SQLite `GroundworkProviderDriver`, saves and reloads a global
token document across distinct clients, and executes the declared token-reference
route with provider-native plan evidence. The adapter test project passed 14/14
for its current codec/manifest/failure/registration scaffold, and the focused
architecture boundary passed 4/4.

T005 and T006 passed on Groundwork preview.88 on 2026-07-25. The focused
capability selection passed 17/17 in 32 seconds. It executed naming/fingerprint
transformation, exact multivalue membership and projection limits, expected-
version CAS, cross-unit UoW, bounded prune count/cancellation/native plans,
real schema CLI/readiness, and reopen behavior. The shared four-provider case
passed on SQLite, SQL Server, PostgreSQL, and transaction-capable MongoDB with
the same global OpenIddict declaration and with persistent storage, independent
clients, multi-document transactions, and external-process restart admitted.
No capability failed, so T007 required no new issue and no fallback.

The restored tool reported `Groundwork.Tool 0.0.1-preview.88`. SHA-256 package
identities used by this gate were:

| Package | SHA-256 |
|---|---|
| Groundwork.Core | `1eb002c1ee3dd2627a3f933fca356d6312db144d6b0d17bb47477e845a83eead` |
| Groundwork.DiagnosticRecords | `2f1fac115ede3c165319eb4f604b5815f6e709c0f4c32104706c863143a806f6` |
| Groundwork.Documents | `6c707f4ccba9dbd1698d0cbd3d32a33fb70a3ce6991fa1e81c68ad54c7f099a7` |
| Groundwork.MongoDb | `a085d3cafe26ab9bc98453a19ca9d3c13954453e329e485e41cb9ed5649eb859` |
| Groundwork.PostgreSql | `f0ba9d3613ed03a92b5eabb472c66a3acb7d045c334c4a0e7f4b324c765af377` |
| Groundwork.Sqlite | `ed83d7cf5740224d0a92e0a05773ba1834f6b05d69e5938ddfc491e622ce0383` |
| Groundwork.SqlServer | `b3bf54480321fae72f7467d919b060df8a541644087f4a9367adf60237947e6a` |
| Groundwork.Tool | `e602f014a0a015bd9f1a4175b417b68e9a4b18589eb8cdbf1ce5a230409b803b` |

These results certify only the public capability gate and foundational record,
codec, manifest, session, and failure seams. They do not claim that any of the
145 OpenIddict store members, production replacement registration, host
acceptance, performance verdict, or EF deletion is complete.

Root reran the focused post-integration checks from commit `230dbdb39` plus the
preview.88 assertion and solution-registration changes:

```bash
dotnet test tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.csproj -c Release --no-restore
# 28 passed

dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OpenIddictGroundworkCapabilityProbeTests
# 17 passed

dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OpenIddictPersistenceArchitectureTests
# 4 passed
```

## 2. Run direct adapter and registration tests

```bash
dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-build
```

Expected: every existing OpenIddict objective still passes through rewired fixtures; all four replacement registrations resolve; mapper/route/generic-rejection/CAS/exception branches are covered directly.

## 3. Validate schema through the deployment path

```bash
dotnet tool restore
dotnet groundwork --version
dotnet groundwork plan --output json
dotnet groundwork validate --output json
dotnet groundwork status --output json
```

Run the exact host-provided manifest invocation selected during implementation for each provider. Run authorized apply only in an isolated test deployment.

Expected: plan/validate/status do not mutate; apply records deterministic history; runtime admission blocks schema drift, capability gaps, topology gaps, and naming collisions.

## 4. Run the four-provider conformance matrix

Run the shared black-box application/authorization/scope/token suite on each provider. Include independent clients, descriptor round trips, uniqueness, named bounded queries, generic delegate rejection, CAS conflicts, 100 refresh races, dependent cleanup, prune/revoke exact counts, cancellation, all declared failure windows, close/reopen, and process restart.

Expected: identical domain outcomes; zero duplicate refresh successors; no orphaned relationships; no client-side full-collection route; provider-native query and mutation plans identify the declared physical targets.

## 5. Run production-shaped host evidence

Compose the real selected identity features and each Groundwork provider. Seed authorized access; sign in; issue and validate access/refresh tokens; prove refresh replay rejection; revoke access and refresh tokens; restart; then repeat public bearer and protected endpoint calls.

Expected: the caller workflow remains unchanged, state and schema survive restart, and revoked/redeemed/expired/unknown tokens fail closed.

## 6. Submit and consume #646 performance evidence

For token issue/lookup/refresh/revoke/prune, authorization filter/revoke, application client lookup, and scope resource lookup, submit fixed input/result digests, dataset/payload/concurrency/warm-cold declarations, provider identity, storage form, and native-plan artifacts. Run the shared benchmark program and record a pass, redesign, or blocked verdict.

Expected: no workload advances without a reproducible verdict. A blocked/redesign result keeps EF removal blocked.

## 7. Final cleanup audit

After all prior gates pass, remove EF OpenIddict source, migrations, packages, test fixtures, shell settings, host references, and ratchet entries; refresh the authorized maps and update feature/extension documentation. Then inspect all source, project, package, and resolved dependency graphs for `Microsoft.EntityFrameworkCore`.

Expected: no OpenIddict EF artifact remains; retained core identity projects remain free of concrete-provider dependencies; the final architecture guard reports a full path for a deliberate reintroduction.

## Preparation checkpoint — 2026-07-25

This checkpoint was replayed manually from a current-main worktree without
rebasing or merging the stale draft. It freezes the OpenIddict 7.5 145-member denominator,
the 54-objective EF-oracle retention baseline plus one current-main shared-host
addendum (55 retained objectives), architecture-guard source, and partial
public-API probe source. It also separates provider-neutral server/validation/token
behavior from the frozen EF oracle and creates the Groundwork adapter boundary.

The 2026-07-25 root verification passed the real SQLite capability probe 6/6,
the current adapter scaffold 14/14, and the architecture boundary 4/4. Both the
frozen EF oracle and Groundwork boundary projects built in Release with no
errors. The directly affected Identity/OpenIddict selection passed 51/51 and
the complete Identity test project passed 140/140. Root also re-derived the
store-contract denominator from the restored OpenIddict 7.5.0 XML:
Application=42, Authorization=32, Scope=28, Token=43, total 145.

These results prove only the behavior-preservation and
manifest/codec/boundary slice described above. T005 and T006 remain open.
There is no `IOpenIddict*Store` implementation, replacement registration,
session/UoW/CAS/redeem flow, four-provider matrix, unified deployment
selection, or performance evidence. The EF oracle remains load-bearing for
#646 and must not be deleted by this checkpoint.
