# Validation Guide: OpenIddict Groundwork Stores

This guide is the completion evidence sequence for Spec 106. It does not authorize EF deletion until every gate below passes on the exact reviewed head.

## Prerequisites

- .NET 10 SDK and Docker-compatible provider runtime.
- Public, mutually compatible Groundwork `0.0.1-preview.81` Core/Documents/provider/Tool packages; record package hashes and `dotnet groundwork --version` output.
- SQLite, SQL Server, PostgreSQL, and MongoDB fixtures. Mongo scenarios requiring a multi-record unit of work run only against a replica set or sharded topology.
- Provider connection values supplied through environment/configuration, never committed or shown in process-visible command arguments.

## 1. Verify the upstream admission gate

Restore and build the new adapter package, then run focused capability probes for codec admission, physical entity definitions, schema operations, typed compound/range routes, multivalue declarations, bounded mutation count/cancellation, native mutation-plan evidence, CAS, and UoW. Record the exact provider/tool versions and outcomes in the feature evidence.

Expected: every required public capability succeeds. A missing or non-public capability blocks the feature and is linked to an upstream Groundwork work item. When the package exposes a public alternative whose use changes the Elsa physical design, record an Elsa architecture blocker instead of silently selecting it.

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

## Batch 1 evidence — 2026-07-24

Batch branch base: `67efaa76b719301c16a1fc017bdc93e17e660515`.
Current remote `main` inspected during the batch:
`18e0b54968339e0d7efc9af1f3cf672b3faef7d3`.

### Frozen denominators

- OpenIddict.Abstractions 7.5.0 XML documentation reports 42 application,
  32 authorization, 28 scope, and 43 token members: 145 total.
- The retained behavior baseline contains 54 objectives: 23 direct
  OpenIddict tests, 7 development/shell tests, 9 tests that reach
  OpenIddict only through the shared token-endpoint host, 12 mixed Groundwork
  Identity/EF OpenIddict HTTP tests, and 3 provider-module tests.
- Reproduction commands and the complete identities are recorded in
  `contracts/openiddict-member-ledger.md` and
  `contracts/test-objective-ledger.md`.

### Preview.81 capability probe

Probe:
`tests/Elsa/Persistence/Groundwork/Conformance/Tests/OpenIddictGroundworkCapabilityProbeTests.cs`.

The focused API/package probe and current-main inspection establish:

- all seven Groundwork libraries on current `main` are pinned to
  `0.0.1-preview.81` (the unmerged batch branch still restores preview.80);
- the central tool manifest is still pinned to preview.80, so exact-family
  admission remains red; an isolated install verifies that
  `groundwork.tool` preview.81 is publicly restorable and reports its exact
  version;
- scalar physical entity construction, typed compound/range routes, and a
  bounded delete declaration compile against the batch branch;
- the public codec, optimistic save/CAS, UoW commit/rollback, physical
  mutation explanation, and runtime readiness surfaces are present;
- each of the four provider packages reports atomic commit, optimistic
  concurrency, equality/range query operations, and evidence claims.

Provider capability reports are static package declarations. They are not native
query/mutation-plan evidence and do not complete T006.

```text
Tool 'groundwork.tool' (version '0.0.1-preview.81') was successfully installed.
Groundwork.Tool 0.0.1-preview.81
```

### Blocking physical-form result

Inspection of the restored preview.81 package and the Groundwork
`b7a31055..c6d40b58` source delta shows no `Groundwork.Core`
physical-declaration API change from preview.80. The focused reflection probe
therefore continues to enforce that:

- `PhysicalTableDefinition.PhysicalEntityTable` has no parameter whose name
  contains `linked`;
- the shared/dedicated document factories do expose linked projection/key
  parameters.

OpenIddict requires searchable membership routes for redirect URIs, post-logout
redirect URIs, authorization scopes, and scope resources. Consequently the
original “four physical entity tables with linked multivalue relationships”
cannot be declared through preview.81.

Production manifest/store scaffolding is blocked pending review of:

1. shared/dedicated physical forms for the four logical units; or
2. four entity units plus separately declared linked membership units.

Both alternatives change the original design and require #646 physical-form
evidence. No provider-specific or client-side fallback was added.

### Focused validation

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  -c Release --filter FullyQualifiedName~OpenIddictGroundworkCapabilityProbeTests --no-restore

dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  -c Release --filter FullyQualifiedName~OpenIddictPersistenceArchitectureTests
```

Actual batch-branch result before integration with current `main`:

- capability probe: 7 passed, 2 failed, 0 skipped. Both failures are the
  deliberate preview.81 package/tool ratchets running against the preview.80
  branch base;
- architecture guard: 2 passed, 0 failed, 0 skipped.

Actual result after merging remote `main`
`18e0b54968339e0d7efc9af1f3cf672b3faef7d3`:

- capability probe: 8 passed, 1 failed, 0 skipped. The package-family ratchet
  now passes; the only failure is the deliberate central-tool ratchet because
  `.config/dotnet-tools.json` remains on preview.80.

Static provider capability reports are admission metadata only; this batch does
not claim native execution, native plans, topology evidence, or restart evidence.
After merging current `main`, the library ratchet should pass and the tool
ratchet should remain red until `.config/dotnet-tools.json` is aligned from
preview.80 to the publicly restorable preview.81. T006 remains blocked after that
alignment on the reviewed physical form and real-provider evidence.

Actual result after merging remote `main`
`78033cf1167071123cb9fe5ef38653973bd65200`:

- capability probe: 9 passed, 0 failed, 0 skipped;
- architecture guard: 2 passed, 0 failed, 0 skipped.

The package and tool family are now aligned on preview.81. T006 remains blocked
only on the reviewed physical form and real-provider evidence; this checkpoint
does not claim a completed OpenIddict adapter.

## Phase 2 scaffold and red baseline — 2026-07-24

The concrete provider and test projects restore and compile directly and occupy
their canonical collapsed OpenIddict solution folders in `Elsa.Server.slnx`.
This completes solution serialization without choosing a physical form.

```bash
dotnet test \
  tests/Elsa/Foundation/Identity/OpenIddict/Groundwork/Tests/Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.csproj \
  -c Release --no-restore --nologo --verbosity minimal
```

Actual red result: **24 failed, 0 passed, 0 skipped** after successful compilation:

- codec/version contract: 14 failures because
  `Serialization.OpenIddictGroundworkJson` does not exist;
- registration contract: 4 failures because the feature and Groundwork
  registration extension do not exist;
- failure contract: 6 failures because
  `Exceptions.OpenIddictGroundworkFailureMapper` does not exist.

The shared fixture keeps those tests compilable and fixes the expected contract
without adding client-side evaluation, generic-query fallback, a storage
manifest, store implementations, or a physical form. T006/T007A therefore
continues to block T011, T016, and every physical-form-dependent production task.
