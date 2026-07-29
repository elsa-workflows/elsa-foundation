# Contract: Absolute-Zero EF Certification

## Purpose

Define the permanent, fail-closed proof that EF cannot remain or return anywhere in the repository.

## Discovery boundary

The certifier MUST:

1. Discover every `*.csproj` beneath the repository while excluding only generated/build-output directories.
2. Evaluate dependency evidence for every discovered project, whether or not a solution references it.
3. Restore the exact independently discovered project set with forced evaluation rather than relying on membership in `Elsa.Server.slnx`.
4. Produce and retain a restore receipt that binds the repository/worktree state, exact project set, restore command/tool identity, each project's dependency-affecting project/import/central/config inputs, and each resulting `project.assets.json` content hash.
5. Recompute and validate every receipt binding at certification time; refuse to pass for a missing project, missing receipt, missing assets file, stale-but-present assets, changed input, changed assets file, or project-set mismatch.
6. Inspect project XML, central/shared/imported build inputs, static project/package edges, and restored transitive packages.
7. Scan source and maintained host configuration for EF projects, migrations, contexts, registrations, provider selection, and aliases.

## Required empty categories

- EF-named projects
- Direct EF package references
- Central EF package versions
- Shared/imported EF package references
- Direct references to EF projects
- Static transitive EF project consumers
- Static transitive EF package consumers
- Restored transitive EF package consumers
- EF migration files
- EF `DbContext` files
- EF registration occurrences
- EF host-configuration occurrences
- EF-free boundary violations
- Projects missing restored assets

Package matching includes:

- `Microsoft.EntityFrameworkCore*`
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
- `OpenIddict.EntityFrameworkCore`
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- Any additional provider package proven to introduce `Microsoft.EntityFrameworkCore*`

## Verdict

`Pass` is valid only when:

- every category is empty;
- every repository project has current restored evidence;
- the scan ran against the same repository/worktree, discovered-project, dependency-input, and assets state recorded by the all-project restore receipt;
- the repository head is recorded;
- the permanent test has no baseline/update/allow-list path.

Any unknown, stale, missing, or nonempty evidence produces `Fail`.

## Required diagnostics

Each failure reports:

- category;
- repository-relative project/file;
- dependency consumer and EF package when applicable;
- remediation hint (`restore`, `dotnet nuget why`, remove registration/configuration, or add project coverage).

No connection value, credential, or secret may appear in output.

## Bypass-resistance tests

Isolated fixtures MUST prove detection of:

1. An EF project omitted from `Elsa.Server.slnx`.
2. A Windows-style project reference.
3. A central package version.
4. A shared/imported conditional package reference.
5. A direct EF package.
6. A transitive EF project/package.
7. A restored-only transitive EF package.
8. A missing `project.assets.json`.
9. A stale-but-present `project.assets.json` or restore receipt after a project/import/central dependency input changes.
10. A receipt whose project set omits a newly discovered project.
11. An EF migration outside an `EFCore` folder.
12. A `DbContext` outside an `EFCore` folder.
13. An EF registration in source.
14. An EF provider key in JSON/YAML host configuration.
15. Comment/string-literal false positives being ignored where appropriate.

## Retirement of temporary controls

When the real repository passes:

- delete `tests/Elsa/Architecture/Baselines/ef-core-surface.json`;
- remove `ELSA_UPDATE_EF_CORE_BASELINE`;
- remove baseline comparison/save behavior;
- delete `frozen-aspnetcore-identity-ef-oracle.json` and its ratchet test with the oracle;
- retain the scanner and fixture coverage as the absolute-zero guard.
