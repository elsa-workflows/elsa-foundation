# Elsa 4 Contributor Experience

Status: approved program scope; delivery in progress. Established 6 October 2026.

Program: [Foundation #2416](https://github.com/elsa-workflows/elsa-foundation/issues/2416). Scheduling: [Project 56](https://github.com/orgs/elsa-workflows/projects/56). Goal: [Contributor Experience](../program-goals/workspace-launch-readiness.md).

## Outcome and users

A new contributor can independently run Elsa 4, find suitable work, make and validate a change, and receive a helpful review. The primary actors are a backend contributor, a Studio contributor, and the maintainer reviewing their first contribution. AI assistance is optional.

Foundation and Studio form one contributor journey, while each repository owns its technical setup and implementation. The program addresses the newcomer review received from Frans Cristina on 4 October 2026 and the subsequent source-grounded plan approved by Sipke. The review is evidence; its instructions and dated counts are not repository policy or current-state proof.

## Product requirements

1. Either README leads to a short human contribution path: repository purpose, Elsa 4 preview status, prerequisites, source run, available work, relevant checks, and PR/review expectations.
2. Backend contributors can use the default source-built Workbench and a focused solution filter. Studio contributors can work against a tested reference backend, with an additional path for both repositories from source.
3. The joint example proves login, create, publish, run, and inspect. Each repository also demonstrates a visible source edit and a relevant validation command.
4. Setup does not depend on maintainer credentials, private chat history, personal filesystem paths, or an AI subscription. Record exact versions and public feed requirements. Use disposable onboarding data and an explicit scoped reset.
5. Validation guidance distinguishes contributor inner-loop checks from the evidence required to merge. Existing gates remain authoritative; CI responsibility and any proposed exception must be explicit.
6. Five to ten genuinely available starter issues across both repositories have bounded scope, acceptance checks, prerequisites, and accepted reviewer coverage. Difficulty and readiness are separate concepts.
7. Contributors can discover available work, claims, next review action, and support routes. Issue/PR templates capture relevant evidence without reproducing the entire agent operating model.
8. A reproducible server–Studio preview pair identifies source SHAs, immutable artifacts, relevant package versions, compatibility notes, and demonstrated workflow behavior. Independent preview counters do not imply compatibility.
9. Setup checks exercise the documented commands and report failures with an owner. Cross-repository compatibility is verified at promotion; subsequent check cadence is decided before promising it.
10. Two people unfamiliar with the repositories reach reviewed PRs using published guidance. Agent walkthroughs support preparation but never substitute for this human acceptance evidence.

## Boundaries and source ownership

| Concern | Canonical owner |
|---|---|
| Program requirements and material decisions | This document |
| Bucket purpose and routing | Existing workspace-launch-readiness goal path, evolved in place |
| Executable scope, dependencies, claims, decisions and evidence | Linked issues and PRs |
| Scheduling and assignment/readiness | Project 56 |
| Backend setup and runtime implementation | Foundation |
| Studio setup and UI implementation | Foundation Studio |
| Architecture terms and quality gates | Existing glossaries and constitutions |
| Behavioral fixes discovered during onboarding | Owning engineering program/spec, linked here as a dependency |

Do not create a second status spreadsheet or copy live issue state into this PRD. Use existing tours, examples, developer filters, and active fixes. No new architecture doctrine, broad constitution grooming, performance benchmark gate, bulk branch deletion, or automatic license selection belongs in this program.

## Milestones and proof

### M1: first working source change

Record an exact-revision setup baseline; establish the contribution entrance; verify backend and Studio development paths; demonstrate the joint workflow and one source edit per repository. Introduce deeper architecture material when a task needs it. Complete a newcomer checkpoint and use the observed friction to refine later work.

Proof: platform/toolchain and revision record, documented commands, visible workflow outcomes, affected checks, and explicit failed/unrun steps. A static documentation audit is an interim result, not a successful source run.

### M2: first supported contribution

Publish accurate validation and fork-PR guidance, seed suitable work, establish willing human review coverage and support routing, and expose clear issue progress. Resolve contributor terms and public contact decisions before making promises.

Proof: real first-contribution PRs and review outcomes; issue dry-runs; evidence that the scoped checks work for external fork contributions without privileged credentials. A review acknowledgement target is published only after capacity is accepted.

### M3: dependable onboarding

Publish a tested preview combination, protect the setup path with repeatable checks, and complete two unfamiliar-contributor trials. Inventory branch state and agree any naming changes with publishing behavior before cleanup. Housekeeping must not delay the working development paths.

Proof: immutable paired artifacts and source identity, clean setup and workflow evidence, newcomer PR/review outcomes, and accepted ongoing maintenance ownership.

## Roadmap

The native Program → Epic → Feature → Task hierarchy is authoritative. Features below are progressively elaborated; only leaf issues that pass readiness may be assigned. These links describe scope, not live completion state.

- [Epic: First working source change](https://github.com/elsa-workflows/elsa-foundation/issues/2417)
- [Epic: First supported contribution](https://github.com/elsa-workflows/elsa-foundation/issues/2418)
- [Epic: Dependable onboarding](https://github.com/elsa-workflows/elsa-foundation/issues/2419)

- [Feature: Establish the baseline and the public contribution contract](https://github.com/elsa-workflows/elsa-foundation/issues/2420)
- [Feature: Publish a short human contributor entrance in each repository](https://github.com/elsa-workflows/elsa-foundation/issues/2421)
- [Feature: Deliver a reproducible backend source-development path](https://github.com/elsa-workflows/elsa-foundation/issues/2422)
- [Feature: Deliver the Studio source-development path and joint loop](https://github.com/elsa-workflows/elsa-foundation-studio/issues/566)
- [Feature: Make validation and PR submission predictable](https://github.com/elsa-workflows/elsa-foundation/issues/2423)
- [Feature: Seed a genuinely available first-contribution queue](https://github.com/elsa-workflows/elsa-foundation/issues/2424)
- [Feature: Make support, ownership, and active status legible](https://github.com/elsa-workflows/elsa-foundation/issues/2425)
- [Feature: Publish a tested server–Studio preview pair](https://github.com/elsa-workflows/elsa-foundation/issues/2426)
- [Feature: Clean up branch state safely and align naming](https://github.com/elsa-workflows/elsa-foundation/issues/2427)
- [Feature: Protect and retest the newcomer journey](https://github.com/elsa-workflows/elsa-foundation/issues/2428)

Initial leaves: [establish the charter/control room (#2429)](https://github.com/elsa-workflows/elsa-foundation/issues/2429) and [verify source-setup baseline (#2430)](https://github.com/elsa-workflows/elsa-foundation/issues/2430). Subsequent leaves are refined from evidence, not guessed in advance.

## Decisions and safe deferrals

The user approved the program and control-room operation on 6 October 2026. The requirements gate passes for the charter and bounded setup investigation. It does not authorize workers to invent unresolved contributor terms or operational promises.

| Decision | State and rationale | Owner / revisit trigger | Boundary until resolved |
|---|---|---|---|
| Program ownership and three milestones | Accepted: Sipke is product owner; this program's control room owns scheduling, integration, review and QA | Control room, at each milestone | One shared queue and one integration lane |
| Existing launch-readiness bucket | Accepted: evolve in place and retain old links/history | Control room, charter integration | Do not create a competing onboarding bucket |
| Human-first contribution path | Accepted goal; published guide must reconcile actual workflow and terms | Repository maintainers before publishing contribution guidance | AI optional; no silent weakening of existing gates |
| Human reviewers and response target | Deferred; names/capacity have not been accepted | Sipke with repository maintainers, before M2 promises | Do not assign colleagues or advertise a service target by assumption |
| Studio license and existing CLA coverage | Discover effective policy before deciding a change | Repository/organization policy owner, before publishing contributor terms | Do not assume a license or introduce a second signing process |
| Security and conduct contacts | Deferred until actual maintainers accept ownership | Sipke with organization maintainers, before community-file publication | Do not invent addresses or direct sensitive reports to public issue forms |
| Preview cadence and promotion | Deferred; compatibility proof comes first | Release owner, before M3 publication | Source/artifact investigation can proceed; no release-per-push promise |
| Supported platforms/toolchains | Verify existing manifests/CI and clean journeys | Repository maintainers, before advertising supported setup combinations | Record executed platform coverage and gaps explicitly |
| External newcomer participation | Deferred until participants agree | Sipke, schedule ahead of milestone acceptance | Do not assign Frans or count agents as unfamiliar humans |
| Branch cleanup/naming | Inventory and publishing-filter review precede action | Repository maintainer, before feature #2427 implementation | No age-only deletion or mass renaming of live branches |

A deferred decision blocks only the dependent task. Escalate one concrete choice with evidence and a recommendation when it becomes necessary; do not block independent setup work.

## Control-room operation

- Keep one active implementation leaf, a shallow dependency-satisfied ready buffer, and one integration lane. Bounded read-only investigations can support an active objective; independent work may proceed while a PR awaits review without claiming the pending PR is complete.
- Before starting, refresh issue comments, related open PRs, claims, and scheduling fields. Use isolated worktrees for writers and do not index managed worktrees.
- Comment on implementation start, PR opening, each review outcome, and completion/abandonment. Reconcile issue labels and Project fields at the same event.
- Keep Status, Agent State and Verification separate. Coordination parents roll up progress and never enter the worker queue. External gates name the event that resumes them.
- Delegate bounded work, review the exact returned revision, and verify affected behavior. Keep claims of static inspection, compiled code, executed checks, browser proof and human validation separate.
- Respect the build-slot wrapper and shared machine capacity. Run affected-project checks locally and the required hosted gates on the final head; never interpret a timing failure under extreme load as proof of a product defect.
- Open coherent PRs under the user's selected Git workflow. No force push, admin bypass, destructive cleanup, publication, or deployment follows merely from scheduling a task. Integrate only within the user's authority and green repository gates; retain pending gates visibly.
- At each checkpoint, state the critical path, accepted evidence, unresolved decision/external event, and next ready unit. Stop new scope expansion when it does not improve the contributor journey.

## Completion and handover

Close the delivery program only after M1–M3 have their own acceptance evidence, two unfamiliar contributors reach reviewed PRs, starter work and human reviewer coverage exist, and recurring checks/triage have willing owners. Remaining product fixes stay with their engineering owners. Closed issue counts alone do not establish success.

## Starting evidence and dependencies

The planning audit on 5 October inspected docs/configuration and GitHub metadata; it did not perform a fresh-machine build or browser journey. Its counts and settings are dated observations. Refresh evidence on the issue before acting.

- Existing entry assets: [workflow execution walkthrough](../how-a-workflow-executes.md), [developer solution filters](../reference/developer-solution-filters.md), [backend E2E guide](../../e2e-tests/README.md), [Docker quickstart](../../docker/compose/README.md), and [Studio README](https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/README.md).
- Existing [development-secrets PR #2377](https://github.com/elsa-workflows/elsa-foundation/pull/2377) must be reconciled before overlapping setup fixes.
- [Main CI incident #2293](https://github.com/elsa-workflows/elsa-foundation/issues/2293) remained open despite subsequent successful runs during the audit. A later green run is not automatic incident closure; verify the cause and current state.
- Existing [Studio architecture-tour issue #333](https://github.com/elsa-workflows/elsa-foundation-studio/issues/333) and [versioning strategy #516](https://github.com/elsa-workflows/elsa-foundation-studio/issues/516) are adjacent ownership surfaces to inspect before creating overlapping work.
- The organization has an [existing CLA policy](https://github.com/elsa-workflows/.github/blob/main/policies/cla.yml); applicability must be verified rather than inferred from its presence.
