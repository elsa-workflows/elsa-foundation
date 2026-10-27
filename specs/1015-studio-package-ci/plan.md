# Implementation Plan: Studio Package CI

**Branch**: `agent/codex/3365-studio-package-ci` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)
**Input**: Task #3365 and its accepted Code Review handoffs; delivered package/consumer proofs through #3364.

## Summary

Extend the existing archive verifier and Studio CI. Reuse actual production archives and the committed API, HostingAssets, Composition and lifecycle/pruning probes. Add strict aggregation that recognizes only the intended causal failures, preserves their failed child reports and retains diagnostics. Keep the existing exact22Studio inventory, frozen consolidation provenance, package names/pins and publisher holds.

## Technical Context

- Existing Python3 standard-library archive/XML/JSON orchestration and GitHub Actions YAML; existing .NET10 and Playwright Chromium consumers. No new dependency or technology choice.
- Canonical inventory: `tools/ci/studio-package-identities.json`; canonical generated resource ownership: `tools/ci/verify-studio-assets.py`. Derive archive paths from each inventory project's wwwroot rather than maintaining a competing module-path list.
- Existing public packages and pins: CShells0.0.31, Nuplane0.0.12; one injected local candidate version. Modularity.Core/Nuplane candidates share that version but remain separate from the strict22Studio inventory.
- Owned temporary reports, candidate copies, caches and published processes; original archives remain immutable. Keep runtime feeds separate from hidden build inputs.
- Supported initial local profile is macOS/native; actual hosted Ubuntu/native execution is required for the CI integration. Native refusal is a failed qualification, never a skip.
- No performance target, new C# feature/API/test project, project reference, package identity/version-line change or publication. Timeouts bound failure only.

## Constitution Check

Load framework6.1.0 and Elsa6.3.0 plus both Gate indexes and the review checklist. This is incremental internal build/qualification tooling over accepted product contracts, not a new application architecture or greenfield feature requiring a new test-cadence decision. Meaningful behavior/causal controls are required; do not test documentation/source strings. Archive mutations and wrong-stage/wrong-diagnostic/no-update controls must demonstrate guard rejection. Existing actual lifecycle fixtures stay owned and source-visible; no production friend/public API or duplicate framework coordinator is added. No unratified workflow/catalog pattern is relied on.

The existing [hosting contract](../../docs/reports/studio-hosting-contract.md), [hosting qualification](../../docs/reports/studio-hosting-reuse.md) and current probe READMEs govern their separate proof scopes. Do not label scripted adapter evidence as actual native production consumption. Reuse the existing public candidate/package and runtime proofs, preserve server-only credential/store ownership boundaries, and expose supported-native failure. Recheck gates after design: no unresolved architecture choice or required exception.

## Project Structure

- `tools/ci/verify_studio_nuget_packages.py`: existing identity checks plus required dependency/static-content checks and meaningful defect/restoration coverage.
- `tools/ci/verify-studio-assets.py`: canonical resource ownership; preserve it rather than duplicating paths.
- `tools/ci/qualify_studio_consumers.py`: internal CI orchestrator over existing probes, precise result/control aggregation, provenance and retained evidence.
- `tools/studio/{ApiPackageClosure.Probe,HostingAssets.Probe,Composition.Probe,HostingReuse.Probe}/`: existing qualified consumers/fixtures; make only justified orchestration/guard refinements and document their executed scopes.
- `.github/workflows/studio-ci.yml`: trigger coverage, supported browser/native prerequisites, version-stamped actual candidates/reference publish, qualification and evidence retention. Existing publishers and their permissions remain unchanged.
- `specs/1015-studio-package-ci/`: scope, plan, task/evidence records. Developer sample and program completion remain #3366.

## Execution and Verification

1. Preserve claim6446465147 and branch/base8083f8e54, inspect current source/package expectations and all accepted3365handoffs.
2. Extend archive verification using actual nuspec/resource bytes and canonical ownership. Verify complete candidates, then private dependency/asset omissions, empty/changed payloads and restoration. Original bytes and identities remain intact.
3. Automate API positive/omission/restored and actual Hosting standard/fallback/custom-frontend/asset/runtime controls against the same candidates. Read complete report.json, not its short stdout summary. Require intended stage/diagnostic, tool/provenance and actual denial audits, including failed cases.
4. Reuse Composition's external/native/cold-auth/theme/live-network matrix. Require genuine expected package update and archive hash, successful non-skipped reconcile, before accepting disabled-reload generation failure; mutation-test a no-update case.
5. Execute HostingReuse and StorePruning normal/causal-control/restored profiles, or source-traced equivalent existing whole tests if a fixture is already superseded. Preserve real nupkg/shared-contract, generated-manifest and admitted-reader protection assumptions. No arbitrary exit1 is an acceptable control.
6. Wire CI after actual22Studio pack; add the two same-version Modularity artifacts separately and publish matching actual Web. Install supported Chromium in the executing job. Cover all owned tool inputs in path selection and retain reports/logs/candidate provenance without uploading caches/source trees.
7. Run complete local archive/control and consumer aggregation; compare genuine candidate bytes and audit cleanup. Existing Studio .NET suite still runs whole in CI; rerun locally if C# fixture/runtime source changes. Run architecture graph restore/whole guard/discovery and generated maps per repo gate. Preserve existing publisher tests and source/layout/version policy.
8. Keep PR draft during independent correctness/standards/acceptance review (normal6round cap), terminalize spec/remove checklist before delivery, then ready/request review and wait for real exact-head Full tier plus all Studio checks. Complete authorized reviewed delivery and verify resulting main with actual candidate/CI observations before Done/claim release.

## Hooks and Scope

The mandatory git feature hook ran once with the Issue Bus Lite branch name. Spec1015 was selected after current/archive and all18open-PR spec trees (highest1014). The installed common resolver selects the core template; the optional global specify CLI is absent, so the equivalent installed resolver is used. Setup-plan uses the persisted ignored `.specify/feature.json` path; actual git branch is independently verified even though its JSON branch field is empty. Optional auto-commit hooks are deliberately skipped (also disabled in git configuration); disabled context hooks remain disabled. Existing ignore patterns cover generated outputs. Only required independent review agents are delegated, not implementation.
