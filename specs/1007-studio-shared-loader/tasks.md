# Tasks: Studio shared loader integration

Issue: [#3356](https://github.com/elsa-workflows/elsa-foundation/issues/3356).
Reviewed after [spec.md](spec.md) and [plan.md](plan.md); paths below are repository-relative.

- [x] T001 Align `Studio/Directory.Packages.props`, reached Studio extension imports, generator
  references/namespaces, `Studio/nuget.config` and corresponding NuGet lock artifacts.
- [x] T002 Select the shared adapter/profile and dependency-aware failure logging in
  `Studio/src/apps/Elsa.Studio.Web/StudioFeatureAssemblies.cs`; remove observer/provider registration
  from `Program.cs`, retaining root Console Stream.
- [x] T003 Replace `StudioRuntimeFeatureCatalogRefresher` usage in `ElsaFeatureManagementApi.cs`
  with the public runtime snapshot API. Remove it, `StudioNuplaneAssemblyProvider.cs`,
  `StudioNuplaneShellReloadObserver.cs` and obsolete mirror-only tests.
- [x] T004 Update `Studio/tests/Elsa.Studio.Tests/` public-interface doubles and add lifecycle
  tests using the production profile: cold/install/update/removal, retained scope, failure/retry,
  cancellation/shutdown and unchanged no-op. Verify generated module manifest wire metadata.
- [ ] T005 Run affected Studio suites and a disabled-auto-reload negative control, restore and
  rerun. Complete locked restore, architecture/map checks and reviewed exact-head CI evidence.
- [ ] T006 Mark the spec Implemented in the implementation PR, map acceptance to evidence and
  keep issue/program/project state current through reviewed merge and resulting-main verification.
