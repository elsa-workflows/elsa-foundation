# Studio Hosting and Package Reuse

- **Status:** Active for end-to-end M0–M3 delivery by owner authorization on 25 October 2026; runs beside the other active programs and displaces none.
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
view. This page routes to those records and does not duplicate the backlog. The [M0 extraction contract](../reports/studio-hosting-contract.md) selects package names, boundaries and public
API targets from the completed qualification spikes. Those targets are not production-package delivery evidence;
implementation and final consumer acceptance remain on the owning GitHub leaves.

## Delivery sequence

| Milestone | Outcome | Owning epic |
|---|---|---|
| M0 | Current-source disposition, reuse decisions, dependency set and small public hosting contract | [Consolidate the hosting foundation #3341](https://github.com/elsa-workflows/elsa-foundation/issues/3341) |
| M1–M2 | Reusable host, authentication/bridge and optional administration and themes | [Package reusable hosting and capabilities #3342](https://github.com/elsa-workflows/elsa-foundation/issues/3342) |
| M3 | Prebuilt shell assets, thin reference host, independent consumer proof, CI and developer guidance | [Prove consumption and migrate the reference host #3343](https://github.com/elsa-workflows/elsa-foundation/issues/3343) |

First triage the [shared-hosting reuse spike #3351](https://github.com/elsa-workflows/elsa-foundation/issues/3351)
and [browser-asset packaging spike #3352](https://github.com/elsa-workflows/elsa-foundation/issues/3352), then settle
the [class disposition and public contract #3355](https://github.com/elsa-workflows/elsa-foundation/issues/3355).
Later leaves are refined after their prerequisites settle. New issues remain `needs-triage` until maintainer
promotion. Keep one implementation leaf active and one integration lane; board readiness never overrides issue claims.

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
