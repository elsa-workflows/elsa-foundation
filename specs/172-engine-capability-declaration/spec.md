# Feature Specification: The EF Provider Engine Is Declared by the Module and Selected by One Host Key

**Feature Branch**: `172-engine-capability-declaration`

**Created**: 2026-09-22

**Status**: Implemented — E0 ([#1937](https://github.com/elsa-workflows/elsa-foundation/issues/1937)) merged as [PR 1953](https://github.com/elsa-workflows/elsa-foundation/pull/1953) and E1 ([#1938](https://github.com/elsa-workflows/elsa-foundation/issues/1938)) as [PR 1954](https://github.com/elsa-workflows/elsa-foundation/pull/1954) on 2026-09-22; E2 ([#1939](https://github.com/elsa-workflows/elsa-foundation/issues/1939)) merged in this PR. Upstream: [valence-works/nuplane#77](https://github.com/valence-works/nuplane/issues/77) through its children #87–#90, released as `0.0.11-preview.91`.

**Input**: GitHub issue [#1936](https://github.com/elsa-workflows/elsa-foundation/issues/1936) and the owner's decision of 2026-09-22 to deliver U5 end to end, Nuplane first and then the Elsa consumer. U5 was recorded and deferred by [spec 171](../171-persistence-script-cli/spec.md) (research.md, "U1–U5 evidence table"); the decisions it builds on are in [ADR 0076](../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md).

---

## Problem Statement

A package-hosted Elsa host has to name its EF provider engine by hand in its package closure, and nothing checks that it did.

Every EF persistence module binds its engine reflectively — `EfRelationalProviderBinding` resolves `Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions, Npgsql.EntityFrameworkCore.PostgreSQL` by assembly-qualified type name, then falls back to a scan of the loaded assemblies and to `Assembly.Load` — so no nuspec dependency edge names the engine, and Nuplane's dependency walk, which reads `.nuspec` only, never acquires it. `docs/foundation-host-feeds.md` documented the workaround in prose ("Generating the closure", step 1), and `dotnet elsa persistence` repeated it in two refusal messages.

Naming the engine by hand is the wrong shape for two reasons:

1. **It puts a package-level fact into every host's configuration.** "This module needs one of these four engines" is a property of the module package. A host that composes ten module packages restates the same fact once, and has no way to learn it from the packages themselves.
2. **It is the one place a host's closure can be silently wrong.** The modules reconcile, the cycle reports `IsDegraded=False`, and the failure surfaces much later as a reflection error at shell load. Every other missing package is at least reported as a failed package.

There is a third, quieter failure beside those two. `dotnet elsa persistence --provider` is authoritative (ADR 0076 D4) and compares itself against every enabled shell feature's `Provider` setting — but not against what engine the host's closure will actually carry. A host that is going to install PostgreSQL and a command asked for SQL Server agreed with each other on every check that existed.

---

## Settled Decisions

Settled with the owner on 2026-09-22, on [#1936](https://github.com/elsa-workflows/elsa-foundation/issues/1936). These are inputs, not open questions.

| # | Decision | Rejected |
|---|---|---|
| **D1** | Every EF module package ships a package-root `nuplane.json` (schema 2) declaring capability `ef-provider` with one option per non-null provider on its `[EfModule]`, the option's package id from `EfRelationalProviderBinding.ProviderPackageId`, and the version pinned in `Directory.Packages.props` as a single-point range. | One package per provider (`Elsa.X.Persistence.EntityFrameworkCore.PostgreSql` and three siblings): multiplies thirteen modules by four and contradicts ADR 0076 D2's one-assembly-many-contexts shape. A nuspec dependency on the engine: makes it a `Dependency` node with no discoverable assets, and forces one engine per module package. |
| **D2** | The files are hand-written; `EfModuleDescriptorTests` guards them against `[EfModule]`, `ProviderPackageId`, and `Directory.Packages.props`, the same way it already guards `[ManifestExtension("efModules", …)]`. | Pack-time generation from `[EfModule]`: new build machinery (the manifest generator lives in another repository and is Nuplane-agnostic) for a file that changes only on engine bumps. A guard test gives the same no-drift guarantee with none of it. |
| **D3** | The host selects with Nuplane's own `Nuplane:Capabilities:ef-provider`, not an Elsa key. There is no host-wide Elsa provider key today — the provider is per feature (`EfProviderAgreement.UnsetProvider`, ADR 0076 D4) — Nuplane must stay product-agnostic, and `--restore` already hands Nuplane the host's own configuration root. The per-feature `Provider` keeps deciding what **binds**; the capability decides what is **on disk**. | An `Elsa:Persistence:EntityFramework:Provider` key bridged into Nuplane by a custom contributor: Nuplane's restore path would not see it, and there is no such host-wide key to extend. |
| **D4** | `--provider` stays authoritative (ADR 0076 D4). When the host's appsettings select `ef-provider` and the selection does not contain `--provider`, the command exits 3 and lists the selection as an offender beside the feature offenders. | Treating the capability selection as the provider, or letting it fill in an omitted `--provider`: the same silent-fallback failure D4 exists to refuse. |
| **D5** | Exact versions in the declaration, so `--restore`'s single-point rule (ADR 0076 D10) holds for the injected engine by construction. A host overrides with `Nuplane:Capabilities:ef-provider:Version`. | Ranges. Elsa binds against the engine version it builds with, and a determinism story with ranges would depend on feed contents at the moment the cycle ran. |
| **D6** | The "named by hand" prose is replaced, not merely appended: `docs/foundation-host-feeds.md` "Generating the closure" step 1, both `WorkerRunner` refusal details, and `EfRelationalProviderBinding.EngineMissing` all point at the key while still allowing an explicit root. | Keeping both stories side by side in the docs, which would leave the reader to work out which one is current. |

### Why refusal and not a default

The schema has no `default` option, and Nuplane never picks one. A silently chosen engine is precisely the failure this feature exists to remove: it would reconcile cleanly and then throw a reflection error at shell load, which is further from the mistake in both time and vocabulary than a refusal at cycle time naming the key.

The same reasoning decides the one place where this repository's consumer could have been quiet instead of loud. A host that pins a persistence build predating the agreement check, while setting the key, is refused by name (`host-tooling-capability-unaware`) rather than having the check skipped — because a skipped check produces an artifact for a provider the host never agreed to, and says nothing.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator composes a package-hosted Elsa host without knowing which engine package its modules need (Priority: P1)

An operator assembling `Elsa.Foundation.Host`'s package closure lists the module roots that map onto `shells.json`, sets one key to say which database they run, and does not have to know that `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore` needs `Npgsql.EntityFrameworkCore.PostgreSQL` at exactly the version this Elsa build binds against.

**Why this priority**: This is the feature as #1936 names it. Without it the operator carries a package-level fact in host configuration, and carries it wrongly in silence.

**Independent Test**: An `Elsa.Foundation.Host` build with a directory feed holding the module closure and the four engines but no engine in `IncludePatterns`, with `Nuplane:Capabilities:ef-provider=PostgreSql` set, starts, records `Npgsql.EntityFrameworkCore.PostgreSQL` as a root with source `capability:ef-provider=PostgreSql`, and `dotnet elsa persistence script --provider PostgreSql --all` produces the same artifact as a host that named the engine by hand. With the key unset the host fails to start naming the key; with `--provider Sqlite` the CLI exits 3 naming the selection.

**Acceptance Scenarios**:

1. **Given** a host whose feed holds the module packages and every engine package, and whose include patterns match no engine, **When** the operator sets the key to `PostgreSql` and reconciles, **Then** the engine is installed as a root with `SourceName = capability:ef-provider=PostgreSql` and the modules bind it.
2. **Given** the same host with the key unset and no engine package as an explicit root, **When** it reconciles, **Then** the cycle is degraded, the declaring modules fail with stage `capability-unselected`, and the message names the capability, every declared option and the key.
3. **Given** the same host, **When** the operator names an engine package as an explicit root instead of setting the key, **Then** the capability is satisfied by that root, nothing is injected, and the host behaves exactly as it did before this feature existed.

### User Story 2 - A DBA-pipeline operator cannot script the wrong dialect for a host (Priority: P1)

An operator running `dotnet elsa persistence` against a host whose closure carries PostgreSQL asks for SQL Server by mistake and is told so, rather than handed an artifact for a database the host will never talk to.

**Why this priority**: ADR 0076 D4's whole point is that `--provider` is authoritative and disagreement is loud. The engine the closure carries is the one provider decision the check could not previously see.

**Independent Test**: Against a host with `Nuplane:Capabilities:ef-provider=PostgreSql`, run `dotnet elsa persistence script --provider Sqlite --modules <any> --output <dir>`; assert exit 3, that the message names `Nuplane:Capabilities:ef-provider` and `PostgreSql`, and that the output directory is empty. Repeat with `--provider PostgreSql` and assert exit 0 and an artifact.

**Acceptance Scenarios**:

1. **Given** a host selecting `PostgreSql`, **When** a command asks for `Sqlite`, **Then** the run exits 3 with code `provider-disagreement`, names the key and the selected option, and writes nothing — including for `script`, whose own SQLite refusal (ADR 0076 D5) would otherwise answer a different question.
2. **Given** a host selecting `PostgreSql,Sqlite`, **When** a command asks for `Sqlite`, **Then** it proceeds: the agreement rule is containment, not equality.
3. **Given** a host that sets no such key, **When** any command runs, **Then** nothing about the capability is compared and the per-feature check behaves exactly as it did before.
4. **Given** a host whose per-feature configuration also disagrees, **When** a command runs, **Then** one refusal lists both offender sources rather than the first one found.

### User Story 3 - An operator restores a never-started host whose engine is selected rather than listed (Priority: P2)

An operator populating a fresh host with `dotnet elsa persistence --restore` gets the selected engine installed along with the modules, and — when no selection was made — a refusal that names the key rather than "a package could not be installed".

**Why this priority**: `--restore` (ADR 0076 D10's opt-in exception) is the path that assembles a package set without starting the host, so it is where an unresolved capability is first observable by a tool rather than by a crashing host.

**Independent Test**: Run `--restore` against a host whose feed holds the module and engine packages, first with the key set (assert the state file records the engine as a root with the `capability:` source name, and the artifact equals a hand-named host's) and then with it unset (assert exit 3, a message naming the key and every declared option, and an empty output directory).

**Acceptance Scenarios**:

1. **Given** the key is set and the engine is on a configured feed, **When** `--restore` runs, **Then** the engine is acquired as a root and the run proceeds to produce the artifact.
2. **Given** the key is unset and no engine is an explicit root, **When** `--restore` runs, **Then** the run is refused with code `restore-capability-unresolved`, naming the key, the declaring package and Nuplane's own stage, and nothing is scripted.
3. **Given** a selection whose effective version range names more than one version, **When** `--restore` runs, **Then** the contribution is refused before the engine is fetched, and the refusal names the declaring package and the key.

---

## Edge Cases

- **A selection naming an option no module declares** (`Postgres` rather than `PostgreSql`) fails the declaring packages with `capability-unknown-option`, listing the declared options. It is not silently normalized: the option vocabulary belongs to the declaration, not to Elsa's provider-alias table.
- **An explicit root at a version the module cannot bind** fails the declaring module with `capability-conflict` naming both requests. The explicit root the operator asked for is still applied; Nuplane picks between them never.
- **A selection shape Nuplane's reader cannot resolve** — a JSON array, or a raw value and an `Option` child that disagree — is refused by the CLI rather than read as "no selection". Reading it as absent would skip the agreement check for a host that plainly made a selection.
- **A host that pins Nuplane, has an `appsettings.json`, and cannot load the configuration providers to read it** is refused (`capability-selection-unreadable`) rather than run with the comparison quietly skipped.
- **A host whose persistence build predates the check** while setting the key is refused (`host-tooling-capability-unaware`) naming both versions and the key. The tooling contract refuses unmapped request properties, so the field is only sent to a build that carries it, and the absence is detected rather than discovered as a malformed request.
- **A host with no `shells.json`** reports the per-feature check as `not-checked` and is still compared against its selection: the key is a fact about the package closure, not about shells.
- **`list`** asks for no provider, so it carries no selection and refuses one it could not compare.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every EF module package MUST ship a package-root `nuplane.json` at `schemaVersion` 2 declaring exactly one capability named `ef-provider`, whose options are the non-null provider properties of that assembly's `[EfModule]` declaration(s), each carrying the package id `EfRelationalProviderBinding.ProviderPackageId` names for it and the single-point version range of that package's `Directory.Packages.props` pin. A module that already declares a `loading` section keeps it unchanged.
- **FR-002**: A guard test MUST fail when any declared option set, package id, or version drifts from `[EfModule]`, `ProviderPackageId`, or `Directory.Packages.props`.
- **FR-003**: `Elsa.Foundation.Host` MUST document the key beside its `Nuplane` section, in both the simple and the object form, and its feed guidance MUST NOT tell an operator that the engine has to be listed.
- **FR-004**: `dotnet elsa persistence` MUST read `Nuplane:Capabilities:ef-provider` from the host's own layered `appsettings.json` (base file plus `--environment` overlay) for every command that takes a `--provider`, and MUST exit 3 with code `provider-disagreement` when the selection does not contain the canonical `--provider`, listing it as an offender beside any per-feature offenders. It MUST NOT use the selection as a provider, and MUST NOT skip the comparison silently for any reason — an unreadable configuration, an unresolvable selection shape, and a host build that cannot compare the field are each their own named refusal.
- **FR-005**: `--restore` MUST map Nuplane's `capability-*` refusals to a refusal of its own (`restore-capability-unresolved`) that names `Nuplane:Capabilities:ef-provider` and carries Nuplane's own message, which names the declaring package and every declared option. The stage MUST be read from the store's failure record for the packages this cycle failed, rather than inferred from a package id.
- **FR-006**: No Elsa project may gain an EF provider engine package reference; `EfCoreDependencyGuardTests` stays unchanged.
- **FR-007**: `Elsa.Secrets.Persistence.EntityFrameworkCore` MUST keep its `loading: HostIntegrated` section, unchanged in shape and meaning, alongside its new `capabilities` section.

### Key Entities

- **Capability declaration** — the `capabilities` array in a module package's `nuplane.json`: a name, a description, and one option per engine, each an option name, a package id and a single-point version range.
- **Capability selection** — the host's `Nuplane:Capabilities:ef-provider` value: one option name, several separated by commas, or an object with `Option`, and optionally `Version` and `Feed`.
- **Injected root** — the engine package Nuplane acquires for a selected option, recorded in `store-state.json` with `PackageRole = Root` and `SourceName = capability:ef-provider=<option>`.

---

## Success Criteria *(mandatory)*

The independent test of #1936, restated as the criteria this feature is measured by:

- **SC-001**: An `Elsa.Foundation.Host`-shaped build with a directory feed holding the module closure and the engines, and no engine in `IncludePatterns`, with `Nuplane:Capabilities:ef-provider=PostgreSql` set, records `Npgsql.EntityFrameworkCore.PostgreSQL` as a root with `SourceName = capability:ef-provider=PostgreSql`.
- **SC-002**: `dotnet elsa persistence script --provider PostgreSql` against that host produces an artifact byte-identical to the one a host that named the engine by hand produces.
- **SC-003**: With the key unset and no engine as an explicit root, the run fails naming the key, the declaring package and every declared option, and writes no artifact.
- **SC-004**: `dotnet elsa persistence script --provider Sqlite` against a host selecting `PostgreSql` exits 3 and names `Nuplane:Capabilities:ef-provider`; with `--provider PostgreSql` it proceeds.
- **SC-005**: `grep -r "named by hand" docs src` returns nothing.
- **SC-006**: No Elsa project gains an EF package reference; `EfCoreDependencyGuardTests` is unchanged and green.

---

## Assumptions

- Nuplane `0.0.11-preview.91` reads schema-2 `nuplane.json`, binds `Nuplane:Capabilities`, contributes the selected option as a root, and records `capability-*` refusals against the declaring package. An older Nuplane rejects any `schemaVersion` other than 1 and therefore ignores a v2 `loading` section too, which is why the pin bump (E0) had to land before any module shipped a v2 file.
- Nuplane's configuration reader accepts the string form (one option, or several comma-separated) and the object form with an `Option` child; it does not accept a JSON array, so the CLI refuses that shape rather than accepting something the host itself would be refused for.
- `NuplaneRestoreResult.FailedPackages` carries package ids without stages, so the CLI reads the stage from the store state the same restore wrote, joined on that cycle's own failed ids. This is a gap in the restore result rather than in the design; it is recorded in the Delivery slices below.
- All thirteen first-party modules declare all four providers today, so every declaration lists four options. A module that drops one fails the guard test until its file is updated.
- The per-feature `Provider` settings keep deciding what each feature binds. This feature adds no host-wide Elsa provider setting and changes no feature's default.

---

## Out of Scope

- Retiring Nuplane's host-provided-dependency allowlist. It answers a different question — which *dependencies* the host already supplies, never which *root* a package additionally needs — and was replaced upstream by `Nuplane:HostProvidedPackages` in its own change ([valence-works/nuplane#90](https://github.com/valence-works/nuplane/issues/90)).
- Any host-wide Elsa provider key (D3), and any change to how a feature's own `Provider` setting is read or defaulted.
- Any capability other than `ef-provider`. The mechanism is general; this feature declares one.
- Pack-time generation of `nuplane.json` (D2), and any change to the package-manifest generator.
- Any change to EF Core's migration mechanics, to the artifact format, or to the exit-code vocabulary. The agreement refusal reuses `provider-disagreement` and exit 3 exactly as ADR 0076 D4 defines them.
- Any performance measurement or threshold derived from one (#1668, ADR 0073).

---

## Delivery slices

Filed as sub-issues of [#1936](https://github.com/elsa-workflows/elsa-foundation/issues/1936) on 2026-09-22, delivered in order because each depends on the one before it.

**E0 ([#1937](https://github.com/elsa-workflows/elsa-foundation/issues/1937), merged in [PR 1953](https://github.com/elsa-workflows/elsa-foundation/pull/1953)) — Pin Nuplane `0.0.11-preview.91`.** Bump `Directory.Packages.props` to the release carrying the schema-2 reader, capability selection, the desired-state contributor, and `Nuplane:HostProvidedPackages`. This had to land first: an older Nuplane rejects a schema-2 document outright, so a module shipping one before the bump would silently lose its `loading` section too. **Acceptance:** the pin is bumped and every existing suite stays green.

**E1 ([#1938](https://github.com/elsa-workflows/elsa-foundation/issues/1938), merged in [PR 1954](https://github.com/elsa-workflows/elsa-foundation/pull/1954)) — Declare `ef-provider` in every EF module package.** FR-001, FR-002 and FR-007: eleven projects, thirteen modules, each with a package-root `nuplane.json` packed at `/`, plus the `EfModuleDescriptorTests` guard against `[EfModule]`, `ProviderPackageId` and `Directory.Packages.props`. **Acceptance:** every EF module package carries the declaration, the guard fails on drift in any of the three sources, and `Elsa.Secrets.Persistence.EntityFrameworkCore` keeps `loading: HostIntegrated`.

**E2 ([#1939](https://github.com/elsa-workflows/elsa-foundation/issues/1939), this PR) — The host and the CLI consume it.** FR-003, FR-004, FR-005 and FR-006: the commented example and the feed guidance in `Elsa.Foundation.Host`; the worker reading the key from the host's layered settings and stating it to the tooling entry point; the merged provider-agreement refusal; the `--restore` mapping; the replacement of the "named by hand" prose in both worker refusals and in `EfRelationalProviderBinding.EngineMissing`; and the `docs/foundation-host-feeds.md` rewrite. **Acceptance:** the independent test above passes, `grep -r "named by hand" docs src` returns nothing, and `EfCoreDependencyGuardTests` is unchanged and green.

### Upstream slices

Each lives in `valence-works/nuplane` under [#77](https://github.com/valence-works/nuplane/issues/77), and all were delivered and published as `0.0.11-preview.91` before E0.

- **#87 — schema-2 reader.** `nuplane.json` gains an optional `capabilities` section; the reader moves from `Nuplane.Loading` into core `Nuplane` so reconciliation can read it, with every schema-1 rule unchanged. A schema-1 document never affects the closure and so never fails a package; an invalid schema-2 document can, and does (`capability-metadata-invalid`).
- **#88 — selection.** `Nuplane:Capabilities:<name>`, its object form, `NuplaneBuilder.SelectCapability`, and the options validator.
- **#89 — desired-state contributor.** `IDesiredStateContributor` and `CapabilityDesiredStateContributor`, running between root resolution and graph expansion, as a bounded fixpoint; the `capability-*` refusal stages; the `capability:<name>=<option>` source name; and `RequirePinnedVersions` travelling into the cycle so an unpinned contribution is refused before the package is fetched.
- **#90 — host-provided packages.** `Nuplane:HostProvidedPackages` replaces the hard-coded allowlist. Adjacent rather than part of this feature: it is consulted for dependencies only, and an injected root is never subject to it.

### Known gap

`NuplaneRestoreResult.FailedPackages` is a list of package ids with no stage, so a caller cannot tell a capability refusal from an unreachable feed out of the restore result alone. The CLI closes it by reading `LastFailureById` from the store state that same restore wrote and joining on the cycle's own failed ids — correct, because the record carries the stage and the message, but one read more than a caller should need. A `FailedPackages` that carried the stage, or a `Refusals` collection beside it, would remove the join; that is an upstream request, not a design change here.
