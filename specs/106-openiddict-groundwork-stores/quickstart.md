# Validation Guide: OpenIddict Groundwork Stores

This guide is the completion evidence sequence for Spec 106. It does not authorize EF deletion until every gate below passes on the exact reviewed head.

## Prerequisites

- .NET 10 SDK and Docker-compatible provider runtime.
- Public, mutually compatible Groundwork `0.0.1-preview.76` Core/Documents/provider/Tool packages; record package hashes and `dotnet groundwork --version` output.
- SQLite, SQL Server, PostgreSQL, and MongoDB fixtures. Mongo scenarios requiring a multi-record unit of work run only against a replica set or sharded topology.
- Provider connection values supplied through environment/configuration, never committed or shown in process-visible command arguments.

## 1. Verify the upstream admission gate

Restore and build the new adapter package, then run focused capability probes for codec admission, physical entity definitions, schema operations, typed compound/range routes, multivalue declarations, bounded mutation count/cancellation, native mutation-plan evidence, CAS, and UoW. Record the exact provider/tool versions and outcomes in the feature evidence.

Expected: every required public capability succeeds. A missing or non-public capability blocks the feature and is linked to an upstream Groundwork work item.

## 2. Run direct adapter and registration tests

```bash
dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-build
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
