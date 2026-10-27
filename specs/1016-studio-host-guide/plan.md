# Implementation Plan: Custom Studio Host Guide and Program Exit

**Branch**: `agent/codex/3366-studio-host-guide` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

## Summary

Deliver the last leaf of Program #3340 using the already accepted package contract. Ship a small downloadable `tools/studio/PackageHost.Sample/` composition and a canonical developer guide at `Studio/docs/custom-hosting.md`. Prove that exact sample outside the repository using actual candidate archives. Link the four original reports and a concise M0–M3 ledger, then reconcile stale completed records and final hierarchy only from receipts. No production API, dependency, publisher or runtime-policy change is planned.

## Technical Context

- **Language/version**: existing .NET 10 public hosting APIs; Markdown/XML/JSON sample inputs. An owned local Python harness may orchestrate verification; it is evidence tooling, not a consumer prerequisite.
- **Dependencies**: unchanged Elsa.Studio.Hosting + Dashboard starter; Api/Core are transitive. Optional themes, Nuplane and other modules remain explicit choices. Document the actual preview sources; never infer nuget.org-only availability.
- **Storage**: sample application-owned configuration; optional storage follows existing package options. Qualification uses new owned temporary roots, isolated caches/feed and owned processes only.
- **Testing**: risk-based, test-after recipe validation under ADR 0080 D1 and framework 2.23. Restore/publish/run/browser proof, package/asset hashes, dependency graph, no private source/project references, source/cache/feed hidden and zero denied-tool invocations. Existing complete package CI remains authoritative for broader optional/native controls. No tests assert documentation text.
- **Platform**: supported .NET 10 host and installed observer browser. The developer needs no frontend toolchain; browser automation is an external qualification observer.
- **Project type/scope**: sample template plus documentation/record consolidation. The `.csproj.in` materializes only in an external consumer; it adds no repository production/test project, package identity or solution filter. Performance work is retired and out of scope.

## Constitution Check

Both current constitutions were read: framework v6.1.0 and Elsa v6.3.0, plus both Gate indexes and `docs/contributing/review-checklist.md`. The entrypoint's older Elsa version is routing text; the actual constitution governs.

- No new domain, implementation, contract, friend assembly or public surface. Existing package boundaries and feature identities stay unchanged (2.1/2.13/2.16/2.23.3/E5).
- The external sample consumes public extension points and owns only module/configuration choices; no private Web infrastructure, duplicate coordinator or new general helper (2.7/2.17/2.18).
- Preserve authorization, server-only keys, root lifetimes, storage/revision/retention and supported native boundaries. The recipe documents limitations instead of changing policy.
- Risk-based actual consumer proof checks the new example. Existing native/security/asset controls are retained; no weakened test or markdown assertion (2.21/2.23).
- Terminal checkbox/registry/index cleanup is backed by merged PR, actual candidate and main CI receipts (2.25.3). Other programs, ADR ownership and historical failure/skip records are preserved.
- Architecture graph/whole guards/discovery and maps remain required PR evidence. Existing complete exact-head CI/actual Full tier/Studio plus resulting-main artifact audit gate delivery. Normal three-identical-failed-check stop and six-review-round cap apply; prior issue exceptions do not transfer.
- Draft/provisional workflow-state and pattern sections do not govern any new design here. No new governance exception is assumed.

Pre-design and post-design checks pass; there is no unresolved architecture or technology choice.

## Project Structure

- `tools/studio/PackageHost.Sample/`: Program.cs, CustomStudio.csproj.in, shells.json, appsettings.json, NuGet.Config and a short README pointing to the canonical guide. Explicit selected package version; no implicit latest or project reference.
- `Studio/docs/custom-hosting.md`: runnable acquisition/version/feed/restore/publish/run recipe; public composition/migration and optional/custom choices; security/storage/native/preview limits and evidence links.
- Existing Hosting/Nuplane/Themes and Studio/tool READMEs: route readers to the guide and remove stale implementation-leaf wording.
- `docs/reports/README.md`: index all four accepted reports. `docs/reports/studio-hosting-contract.md` retains accepted boundaries, with implemented-status/guide routing rather than duplicated explanations.
- `docs/program-goals/studio-hosting-package-reuse.md` and registry: M0–M3 delivery ledger and completion record for this named scope only.
- `specs/1009`, `1013`, `1014`, `1015`: reconcile terminal task checkboxes and obsolete formal-only wording with actual current-protocol delivery receipts. This spec's artifacts record its own progress.

## Verification and Delivery

1. Extract only original 22 Studio + 2 Modularity archives from a verified CI artifact; never make a feed from recursive evidence mutants. Preserve producer/version/archive provenance.
2. Materialize/download exact sample files into a new external folder. Use explicit preview/source mapping and an exact candidate version; generate a lock, repeat locked restore and publish with isolated package/http caches and denied frontend tools.
3. Assert Elsa libraries are packages, no ProjectReference, selected published DLL bytes match candidate archives, and source/runtime configuration remain application-owned. Hide only this run's source/feed/cache, start the published host in Production, observe standard UI/module/discovery/runtime and assets with the existing browser observer, then stop only owned processes.
4. Trace every migration/optional handoff to accepted code/receipts, check links, execute required architecture/maps and complete three independent review lenses. No implementation delegation.
5. Final reviewed-head hosted checks must pass. Download/audit actual candidate artifacts and validate the sample against final candidates when available; preserve actual producer revision separately from reviewed source.
6. After reviewed merge, verify actual resulting main CI/Studio/CodeQL, fresh artifact and sample. Then close3366,3350,3343,3340 and read back all30 native/board records. No release/deployment/publisher activation.

## Workflow Notes

Spec number1016 follows the current/archive maximum1015 and all19 open PR trees checked in `/tmp/studio3366-number-reservation.json`. Exclusive claim6449693324. The mandatory git feature hook ran exactly once with Issue Bus branch naming. The installed resolver selects the core spec/plan templates; no `specify` CLI is required. Optional auto-commit hooks are disabled by git configuration and skipped; context-update hooks are disabled. The ignored `.specify/feature.json` pointer selects this feature independently from the branch number. Research agents are unnecessary because no unknown or technology choice remains; only the mandatory independent review lenses delegate.

## Complexity Tracking

None. The sample is a consumer template rather than a new production project or framework.
