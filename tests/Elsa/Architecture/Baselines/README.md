# EF Core surface ratchet

`ef-core-surface.json` is the temporary, reviewable inventory permitted while the repository moves
to Groundwork-only first-party persistence. The architecture suite scans every project in the
repository, not only projects reachable from the solution, and records:

- EF-named projects and direct EF package references;
- central and shared-build EF package references;
- direct, statically transitive, and MSBuild-evaluated restored project/package consumers;
- projects missing restored dependency assets;
- EF migration and `DbContext` files;
- normalized registration token counts and structured host-configuration paths, including Docker
  reference-host configuration.

The test requires an exact match. A new entry fails as an expansion; a removed implementation fails
as a stale baseline entry so the same PR must shrink the inventory. Core and Groundwork projects have
a separate absolute-zero assertion, including path-based Core/Groundwork classification, and can
never use this baseline as an exception. Source scanning ignores comments and string literals while
recognizing EF migration/context base types, provider registrations, and common host configuration
formats. Registration counts and JSON paths make additions inside an already-known file visible;
JSON comment properties are ignored.

After an intentional removal, regenerate the mechanical snapshot and review that the JSON diff only
removes entries:

```bash
dotnet restore Elsa.Server.slnx --force-evaluate

ELSA_UPDATE_EF_CORE_BASELINE=1 \
  dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  --filter FullyQualifiedName~Ef_core_surface_matches_the_reviewed_shrink_only_baseline
```

Baseline generation refuses to run unless every repository project has a restored
`obj/project.assets.json`. This prevents an incomplete solution or restore from silently omitting a
transitive EF package. The forced restore evaluates imported and conditional package/project
references before the scanner reads the assets graph.

The update switch requires the reviewed baseline to exist, rejects every expansion, and permits only
removals. Deleting the file cannot be used to recreate and bless a larger surface. CI restores and
builds the complete solution, then runs this architecture project in the container-free test filter.
Resolved dependency failures identify the consuming project and EF package; use
`dotnet nuget why <project> <package>` when the intermediate package chain is not obvious.

The final zero-EF cleanup tracked by GitHub issue #647 deletes this baseline and update switch. It
retains the scanner with every category required to be empty, including all transitive consumers.

`frozen-aspnetcore-identity-ef-oracle.json` is stricter than the repository-wide surface inventory:
it fingerprints every source, project, and migration file in the temporary ASP.NET Core Identity EF
oracle. The fingerprint is immutable until issue #647 removes that oracle; there is intentionally no
environment-variable update path. Paths and line endings are normalized before hashing so the same
reviewed tree produces the same SHA-256 values on every supported checkout platform.
