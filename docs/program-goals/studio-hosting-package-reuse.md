# Studio Hosting and Package Reuse

- **Status:** Completed through final exit task #3366. Reviewed source and resulting-main verification are recorded on [Program #3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340). The completion entry lands with the final implementation PR; the issue remains the live authority until its final gate receipt is posted.
- **Authorization:** End-to-end M0–M3 delivery by owner authorization on 25 October 2026; ran beside the other active programs and displaced none.
- **Area:** Studio hosting, optional capabilities, package delivery and custom-host developer experience.
- **Steward:** Sipke Schoorstra. Delivery leaves are claimed under Issue Bus Lite.
- **Program:** [#3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340).
- **Scheduling:** [Project 63](https://github.com/orgs/elsa-workflows/projects/63).
- **Authority:** [ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus), owner amendment of 25 October.

## Purpose

A developer builds and publishes a custom Elsa Studio using NuGet references, application configuration and a
small composition root, without copying `Elsa.Studio.Web` infrastructure or building the standard browser shell.
Consolidate reusable behavior and adopt existing qualified infrastructure before introducing another implementation.

GitHub issues own scope, acceptance, native hierarchy, dependencies and progress. The Project is the scheduling
view. This page routes to those records and does not duplicate the backlog. The [accepted extraction contract](../reports/studio-hosting-contract.md) records the source disposition and package/public API choices. The [custom-host guide](../../Studio/docs/custom-hosting.md) is the runnable developer entrypoint; actual delivery receipts below distinguish source, candidate, runtime and resulting-main evidence.

## Delivery sequence

| Milestone | Outcome | Owning epic |
|---|---|---|
| M0 | Current-source disposition, reuse decisions, dependency set and small public hosting contract | [Consolidate the hosting foundation #3341](https://github.com/elsa-workflows/elsa-foundation/issues/3341) |
| M1–M2 | Reusable host, authentication/bridge and optional administration and themes | [Package reusable hosting and capabilities #3342](https://github.com/elsa-workflows/elsa-foundation/issues/3342) |
| M3 | Prebuilt shell assets, thin reference host, independent consumer proof, CI and developer guidance | [Prove consumption and migrate the reference host #3343](https://github.com/elsa-workflows/elsa-foundation/issues/3343) |

The M0 spikes and extraction leaves are delivered. Final leaf [#3366](https://github.com/elsa-workflows/elsa-foundation/issues/3366), [PR #3407](https://github.com/elsa-workflows/elsa-foundation/pull/3407), owns the runnable developer recipe and final record audit. Keep one implementation leaf and one integration lane; the parent feature/epic/program close only after reviewed delivery and resulting-main proof.

## Boundaries and coordination

- Keep `Elsa.Studio.Core` lightweight, module selection explicit and implementations internal by default.
- Preserve server-side management credentials, first-request authorization, API 404s, asset revalidation,
  atomic configuration writes/revisions and reload-safe root service lifetimes.
- Coordinate source and publisher ownership with [#2501](https://github.com/elsa-workflows/elsa-foundation/issues/2501)
  and active Studio changes with [#2310](https://github.com/elsa-workflows/elsa-foundation/issues/2310).
  Runtime work remains owned by [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).
- Reuse modular-hosting and runtime-composition evidence without reactivating their parked backlogs or
  reparenting existing issues by assumption. This program does not redesign workflow execution or add a general settings framework.
- The owner's later "deliver this board end to end" goal supersedes the establishment-only session boundary.
  Package implementation and integrated qualification proceed through scoped, claimed issues;
  releases, deployments, publisher activation, unrelated UI features and a new release train are outside this authorization.

## Completion

Prove restore, publish and run from actual packed artifacts in an isolated consumer with no Elsa source checkout,
copied infrastructure, project references or frontend toolchain. Verify the standard shell, custom-module journey,
optional capabilities and migrated reference application through the public package entry points. CI must check
the complete asset and dependency closure. Required evidence, failed or unrun checks and supported limits stay
explicit on the issues and PRs; program setup or source-only tests do not establish delivery.

## Delivery ledger

| Milestone | Reviewed source / accepted decision | Actual consumption and verification |
| --- | --- | --- |
| M0: reuse and boundaries | Shared adapter [PR #3370](https://github.com/elsa-workflows/elsa-foundation/pull/3370), native store [#3374](https://github.com/elsa-workflows/elsa-foundation/pull/3374), browser delivery [#3376](https://github.com/elsa-workflows/elsa-foundation/pull/3376), complete disposition/public contract [#3378](https://github.com/elsa-workflows/elsa-foundation/pull/3378) | Four [qualification/contract reports](../reports/README.md) retain source-linked decisions, bounded probes and refused profiles. These were decisions and qualification evidence, not production-package delivery by themselves. |
| M1: common host and bridge | Shared CShells.Nuplane adoption [#3379](https://github.com/elsa-workflows/elsa-foundation/pull/3379), Api dependency closure [#3381](https://github.com/elsa-workflows/elsa-foundation/pull/3381), Hosting [#3382](https://github.com/elsa-workflows/elsa-foundation/pull/3382), auth/bridge [#3383](https://github.com/elsa-workflows/elsa-foundation/pull/3383) | Public entry points, selected-shell/endpoint ownership, cold authorization, server-only management credentials and root lifetimes are exercised by the later isolated and combined consumers. |
| M2: optional capabilities | Nuplane administration [#3386](https://github.com/elsa-workflows/elsa-foundation/pull/3386), Themes [#3387](https://github.com/elsa-workflows/elsa-foundation/pull/3387) | Independent and combined capability graphs, native admission/protected readers, managed theme restart and normal-file/link behavior; review and main receipts remain on the owning PRs/issues. Unsupported stores/filesystem mutation remain explicit. |
| M3: prebuilt browser and thin reference host | Prebuilt Hosting browser [#3385](https://github.com/elsa-workflows/elsa-foundation/pull/3385); [thin-Web audit](https://github.com/elsa-workflows/elsa-foundation/issues/3362#issuecomment-6439749580) | Web retains only Program and its application-owned module assembly list. Existing module identities/assets stay with their packages; the standard browser/SDK/UI belong to Hosting. |
| M3: independent consumption and CI | Package consumers [#3391](https://github.com/elsa-workflows/elsa-foundation/pull/3391), custom module/optional composition [#3402](https://github.com/elsa-workflows/elsa-foundation/pull/3402), complete CI automation [#3404](https://github.com/elsa-workflows/elsa-foundation/pull/3404) | [#3364 delivery](https://github.com/elsa-workflows/elsa-foundation/pull/3402#issuecomment-6446433253) and [#3365 delivery](https://github.com/elsa-workflows/elsa-foundation/pull/3404#issuecomment-6449658674) link exact reviewed heads, actual Full tier, main checks and actual package/browser/native results. |
| M3: developer recipe and exit | [Small runnable sample](../../tools/studio/PackageHost.Sample/README.md), [migration guide](../../Studio/docs/custom-hosting.md), [PR #3407](https://github.com/elsa-workflows/elsa-foundation/pull/3407) | Exact external sample and final source/candidate/main/board reconciliation are recorded on [#3366](https://github.com/elsa-workflows/elsa-foundation/issues/3366); final program closure receipt belongs on [#3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340). This row is not a claim that a pending PR or check passed. |

### Candidate proof at the completed CI leaf

At verified main `e1bbbe4d733f842b0aa2c501d03ca15c134684ae`, [Studio CI 40551078628](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40551078628) produced `4.0.0-studio-ci.40551078628.1`: 22 Studio archives plus two matching lightweight Modularity archives. The downloaded artifact SHA256 is `8188ee6728e75a0e3e815d712e5b4cc94fb90bf31fe536ffa9f1b8c07b640528`. Independent audit verifies all 24 original hashes/stamps, 3,475 packaged asset hashes, 520 retained evidence file hashes, 12 archive receipts and 11 private mutant archives. The complete matrix passes 16 aggregate observations while preserving six intended failed controls separately; consumer frontend-tool invocations are zero. The [delivery receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3404#issuecomment-6449658674) records CI/Studio/CodeQL and the actual producer separately from reviewed PR heads.

Archive guards reject missing dependencies/frameworks, exclusive/empty dependency ranges, missing vendor/CSS/lazy chunks, empty entries and changed SDK bytes, then accept restored originals. Runtime controls reject missing/empty runtime configuration and missing browser assets. A successful native reconcile must contain the actual expected package update/hash before a disabled-reload observation qualifies; a no-op cannot stand in for a reload. Shared sequencing and admitted-reader protection keep their normal/control/restored observations. See [qualification commands and retained evidence](../../tools/studio/README.md).

### Boundaries retained at exit

- A candidate artifact is not a remote release. Artifact retention is 14 days; developer recipes require an available matching candidate version and explicit preview sources. No nuget.org-only availability, publisher activation or deployment is inferred.
- The small Dashboard starter proves package restore/publish/browser composition. Backend operations require a configured backend and selected modules; it is not a deployed backend acceptance test.
- Native package metadata/assets and pruning require the qualified immutable extracted/enrolled ownership profile. No automatic cutover, legacy cleanup conversion or arbitrary external-writer guarantee. Workbench Server's existing read-only administration and future maintenance convergence remain separate.
- Managed theme/file/link cases are qualified; broken/cyclic/external source mutation and measured scaling are not. Private writer duplication and public theme feature extension surface remain intentional; no general helper/performance project is added.
- Queued non-required CLA, configured retries, known quarantined #3261 failure, existing skips and superseded/cancelled runs are recorded as observed, never promoted to passed tests. Current merge/main gates are checked independently.
- Other active programs, source/publisher ownership, dependency pins, package identities and the one pre-GA version line are preserved. The owner amendment remains the authorization record for this scope.
