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
10. **Deferred follow-up, not a current delivery gate:** when Sipke explicitly resumes the trials and two willing people unfamiliar with the repositories are available, one per repository, they can use published guidance to reach reviewed PRs. Agent walkthroughs support preparation but never substitute for human acceptance. Until then, participant recruitment, consent, scheduling, and reviewed newcomer-PR acceptance are unperformed follow-up, not completed or passed evidence.

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

Record an exact-revision setup baseline; establish the contribution entrance; verify backend and Studio development paths; demonstrate the joint workflow and one source edit per repository. Introduce deeper architecture material when a task needs it. Use verified technical evidence for current delivery. The unfamiliar-human checkpoint and any friction learned from it remain deferred until the trials resume.

Proof: platform/toolchain and revision record, documented commands, visible workflow outcomes, affected checks, and explicit failed/unrun steps. A static documentation audit is an interim result, not a successful source run. No human trial is claimed or required for current M1 acceptance.

### M2: supported first-contribution path

Publish accurate validation and fork-PR guidance, seed suitable work, establish willing human review coverage and support routing, and expose clear issue progress. Resolve contributor terms and public contact decisions before making promises.

Proof: issue dry-runs and evidence that the scoped checks work for external fork contributions without privileged credentials. The two unfamiliar-participant PR and review outcomes are deferred follow-up, not current M2 acceptance. A review acknowledgement target is published only after capacity is accepted.

### M3: dependable onboarding

Publish a tested preview combination and protect the setup path with repeatable checks. Inventory branch state and agree any naming changes with publishing behavior before cleanup. Housekeeping must not delay the working development paths. Two unfamiliar-contributor trials remain deferred follow-up, not current M3 work or a delivery blocker.

Proof: immutable paired artifacts and source identity, clean setup and workflow evidence, and accepted ongoing maintenance ownership. Newcomer PR/review outcomes are not claimed or required for current M3 acceptance.

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
| External contribution policy | Accepted: small concrete fixes may arrive directly by PR; substantial features start with issue alignment; AI tools are optional. Published in [Foundation #2438](https://github.com/elsa-workflows/elsa-foundation/pull/2438) and [Studio #575](https://github.com/elsa-workflows/elsa-foundation-studio/pull/575), following the [owner decision](https://github.com/elsa-workflows/elsa-foundation/issues/2420#issuecomment-6006624090) | Repository maintainers when contributor guidance changes | Existing checks, review expectations, and merge gates remain in force |
| Contributor questions and actionable work | Accepted and published: Foundation Discussions Q&A is shared across both repositories; actionable bugs and features go to the relevant repository's Issues. The control room performs initial triage, with Sipke accountable as maintainer ([owner decision](https://github.com/elsa-workflows/elsa-foundation/issues/2420#issuecomment-6006624090), [Foundation guide](https://github.com/elsa-workflows/elsa-foundation/pull/2438), [Studio guide](https://github.com/elsa-workflows/elsa-foundation-studio/pull/575)) | Control room, at initial triage; revisit if ownership changes | No response-time promise or colleague review capacity is implied |
| Human reviewers and response target | Accepted: Sipke will review the first five small contributor PRs across Foundation and Studio. The control room prepares the issues, verifies technical evidence, and supplies focused review packets. This is not a backup commitment or completed review; no response-time SLA is promised ([owner decision](https://github.com/elsa-workflows/elsa-foundation/issues/2424#issuecomment-6032659923)) | Sipke for the first five reviews; control room for readiness and review packets | Refresh candidate scope, claims, and readiness before advertising availability; do not promise a response time |
| Studio license terms | Accepted and integrated: MIT applies to Elsa-owned material with Copyright (c) 2026 Elsa Workflows; third-party material retains its own terms and notices. The license-specific invitation hold is lifted ([Studio #570](https://github.com/elsa-workflows/elsa-foundation-studio/issues/570), [PR #571](https://github.com/elsa-workflows/elsa-foundation-studio/pull/571), [parent record](https://github.com/elsa-workflows/elsa-foundation/issues/2420#issuecomment-6007528922)) | Studio maintainers, if repository license terms change | Do not extend Elsa's terms to third-party material |
| Existing CLA check and first unsigned-contributor path | The first unsigned/nonmember human-path evidence remains unverified deferred follow-up; it is not part of current delivery acceptance. Existing checks and merge gates remain authoritative and unchanged ([evidence record](https://github.com/elsa-workflows/elsa-foundation/issues/2420#issuecomment-6007336333)) | Repository/organization policy owner, only if a concrete policy or configuration choice becomes necessary | Follow existing CLA checks and any action they request; do not invent a second signing process, make a legal inference, or use the unverified newcomer path to block current delivery |
| Security reporting | Accepted: GitHub private vulnerability reporting is enabled in both repositories, and Sipke accepts initial receipt and triage through those private routes. Direct page validation remains tracked in [#2476](https://github.com/elsa-workflows/elsa-foundation/issues/2476); no response-time promise is made ([owner decision](https://github.com/elsa-workflows/elsa-foundation/issues/2425#issuecomment-6032696200)) | Sipke for initial receipt and triage; control room maintains repository guidance | Keep sensitive details on the private route; ordinary Q&A and actionable issues remain public support paths |
| Confidential conduct reporting | Accepted: Sipke accepts the initial handling role for both repositories. A monitored contact address and any public policy/contact guidance remain pending verification; no response-time promise is made ([role decision](https://github.com/elsa-workflows/elsa-foundation/issues/2425#issuecomment-6032721359), [contact readiness](https://github.com/elsa-workflows/elsa-foundation/issues/2425#issuecomment-6032770230)) | Sipke, before any public conduct-contact publication | Do not publish an unverified address or route confidential reports to public issues/Q&A |
| Preview cadence and promotion | Accepted: use on-demand compatibility checkpoints after deliberately selecting and verifying an immutable Studio/backend image pair. Checkpoints reference existing images and do not publish packages or images. No fixed cadence is promised; publication of the first concrete checkpoint still requires review and exact evidence ([owner decision](https://github.com/elsa-workflows/elsa-foundation/issues/2426#issuecomment-6032798325)) | Release owner, at a proposed promotion/checkpoint | Do not create a calendar or per-push promise, publish an unreviewed checkpoint, or imply package/image publication |
| Supported platforms/toolchains | Verify existing manifests/CI and clean journeys | Repository maintainers, before advertising supported setup combinations | Record executed platform coverage and gaps explicitly |
| Two unfamiliar-human trials | Deferred by Sipke on 7 October because willing participants are unavailable and the repositories remain in flux ([owner decision](https://github.com/elsa-workflows/elsa-foundation/issues/2428#issuecomment-6037784871)). No trial has occurred; participant recruitment, names, consent, scheduling, and reviewed newcomer PRs remain unperformed, not passed or complete. This follow-up does not block current delivery, and no date is promised. | Revisit only when Sipke explicitly resumes the trials and two willing unfamiliar participants are available; then confirm names, consent, selected available issues, and timing | Do not recruit or schedule on the owner's behalf, claim human acceptance, or make trial completion a delivery or handover gate |
| Ongoing maintenance ownership | Accepted: after handover, Sipke owns ongoing maintenance for both repositories, including setup failures, Studio/backend compatibility, starter issues, and human reviewer coverage. Codex may support investigation and fixes; existing CI runs on relevant changes and compatibility is checked before promotion. No new calendar cadence or response-time SLA is promised ([owner decision](https://github.com/elsa-workflows/elsa-foundation/issues/2428#issuecomment-6033046110)) | Sipke after handover; control room records the handover evidence | Ownership acceptance does not complete handover. Trials remain separate deferred follow-up; do not imply a periodic interval |
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

Close current delivery after the nondeferred M1–M3 acceptance evidence is complete, reviewer-backed starter work is genuinely available, both repositories publish the approved conduct policy and contact guidance with a verified confidential-reporting route, and ongoing maintenance/triage ownership has completed handover. The two unfamiliar-human trials and their reviewed PRs remain deferred follow-up: they are not complete or passed and do not block delivery or handover. Resume only after Sipke explicitly resumes the trials and two willing participants are available ([owner decision](https://github.com/elsa-workflows/elsa-foundation/issues/2428#issuecomment-6037784871)). Remaining product fixes stay with their engineering owners. Closed issue counts alone do not establish success.

## Starting evidence and dependencies

The planning audit on 5 October inspected docs/configuration and GitHub metadata; it did not perform a fresh-machine build or browser journey. Its counts and settings are dated observations. Refresh evidence on the issue before acting.

- Existing entry assets: [workflow execution walkthrough](../how-a-workflow-executes.md), [developer solution filters](../reference/developer-solution-filters.md), [backend E2E guide](../../e2e-tests/README.md), [Docker quickstart](../../docker/compose/README.md), and [Studio README](https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/README.md).
- Existing [development-secrets PR #2377](https://github.com/elsa-workflows/elsa-foundation/pull/2377) must be reconciled before overlapping setup fixes.
- [Main CI incident #2293](https://github.com/elsa-workflows/elsa-foundation/issues/2293) remained open despite subsequent successful runs during the audit. A later green run is not automatic incident closure; verify the cause and current state.
- Existing [Studio architecture-tour issue #333](https://github.com/elsa-workflows/elsa-foundation-studio/issues/333) and [versioning strategy #516](https://github.com/elsa-workflows/elsa-foundation-studio/issues/516) are adjacent ownership surfaces to inspect before creating overlapping work.
- The organization has an [existing CLA policy](https://github.com/elsa-workflows/.github/blob/main/policies/cla.yml); applicability must be verified rather than inferred from its presence.
