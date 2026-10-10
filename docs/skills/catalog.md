# AI-Provider-Neutral Skill Catalog

This catalog defines the workflows agents and engineers should follow. AI-provider-specific skill folders may wrap these workflows, but this file is the canonical catalog.

## Skill Layer Rules

- Skills describe repeatable workflows: trigger, inputs, steps, and output.
- Skills link to constitutions for gates, glossary for meanings, maps for generated facts, reports for findings, and reference docs for rationale.
- Skills should not duplicate long concept explanations. If a workflow needs a term, use [Glossary Lookup](#glossary-lookup).
- AI-provider wrappers should stay thin and point back to this catalog.
- Plan first for decisions. Act as follow-through for obligations: before starting a new architecture-affecting unit, produce a plan/checklist and wait for approval; after an approved unit is underway, complete required tests, extension-point catalog updates, generated-map refreshes, and small docs follow-through without a second approval unless the follow-through changes architecture meaning.

The current skill audit lives in [skills-stabilization-audit.md](../reports/skills-stabilization-audit.md).

## AI-Provider Wrapper Validation

Claude Elsa wrappers can be checked for drift with:

```powershell
tools/skills/validate-claude-wrapper-drift.ps1
```

The validation compares `.claude/skills/elsa-*/SKILL.md` against this catalog and `.specify/integrations/claude.manifest.json`, including manifest hashes, thin-wrapper metadata, and program-goal lookup guardrails.

## Orientation And Review Skills

### Architecture Tour

**Use when:** a user asks for orientation, "what is this repo?", or a concise explanation of the architecture.

**Workflow:** read [../architecture-tour.md](../architecture-tour.md), then follow links only for terms or systems the user asks about.

**Output:** a short guided tour with next places to inspect.

### Glossary Lookup

**Use when:** a term must be understood before continuing.

**Workflow:** check [../glossary/root.md](../glossary/root.md), then [../glossary/elsa.md](../glossary/elsa.md), then worked references.

**Output:** the relevant meaning in this architecture, not a generic definition.

### Critical Constitution Review

**Use when:** a user wants to question, revise, ratify, or challenge constitution sections.

**Workflow:** identify the target section; separate enforceable gate from explanation/history; compare against glossary terms and current code reality; list ambiguity, contradictions, and missing exceptions; propose a work unit when changes are needed.

**Output:** findings first, then proposed revision path. Do not silently rewrite constitutional meaning.

### Verify Codebase Against Constitution

**Use when:** a user asks whether the codebase complies with the constitution.

**Workflow:** choose the relevant gates; inspect project references, extension-point catalogs, package references, tests, and docs; report mismatches with file references; classify each finding as code drift, doc drift, missing test, or unclear gate.

**Output:** a verification report that can become a work unit.

### What's Next / Unfinished Work

**Use when:** a user asks what is unfinished, unratified, weakly implemented, or ready for the next work unit.

**Workflow:** read the program goals and drift guard in [../../AGENTS.md](../../AGENTS.md#program-goals-and-drift-guard), then inspect [../program-goals/](../program-goals/) to identify the current program-goal state when it affects ranking or is unclear: a named bucket, `none/free-flow`, or temporarily `unknown/not-assessed`. Use [../reports/unfinished-work.md](../reports/unfinished-work.md) as an inventory of findings and candidate concerns, not as the active queue. Do not invent a bucket just because one is missing; use `none/free-flow` when the work should remain outside a mid-term coordination bucket. Check whether any short-term roadmap notes in the active program-goal file have already been implemented or captured in their normal source-of-truth layers; if so, retire, replace, or mark completed objectives before relying on them for ranking. Refresh with searches for `TODO`, `DEFERRED`, `pending`, `stub`, `placeholder`, and specs marked superseded or retained-for-intent. Rank candidates by current program-goal state when applicable, user intent, local severity, and unblock value. Before turning a report finding into substantial durable planning, implementation, or a multi-session handoff, add or move the item to the relevant program-goal bucket, or explicitly keep it `none/free-flow`. Also read `.agent-prefs/session-execution-model.md` when it exists; if it prefers fresh workers or ask-each-time, ask whether to plan/execute here or prepare a reviewed handoff prompt for a fresh agent/thread.

**Output:** prioritized list of next candidates, with whether each is architecture, docs, tests, or code, the program-goal route for any selected item, the program-goal state when it was used for ranking, any completed short-term objective that was removed or superseded, and the recommended execution route. If the recommended next unit is substantial and the local session execution model calls for fresh-worker control-room behavior, stop at the route question or produce the requested handoff prompt instead of silently planning in the current session.

### Program Goal Drift Review

**Use when:** the user asks whether work is drifting, a thread is moving into a third consecutive plan/work unit under the same topic, recent work is repeatedly deepening one local area, or a proposed task would change/split a program-goal bucket.

**Workflow:** read the program goals and drift guard in [../../AGENTS.md](../../AGENTS.md#program-goals-and-drift-guard), then inspect [../program-goals/](../program-goals/) and identify the current program-goal state: a named bucket, `none/free-flow`, or temporarily `unknown/not-assessed`. Identify recent short-term objectives and the next proposed objective; decide whether the work is aligned, knowingly specialized, over-invested, under-invested elsewhere, free-flow by intent, or evidence that a program goal should be updated. When a short-term objective has been implemented, verify that its result still advances the program goal and remove or replace the completed objective in the goal file once the result is captured in reports, specs, maps, skills, glossary, constitution, or code. If multiple architects or domains are involved, treat each valid mid-term bucket separately instead of forcing all work into one global priority list.

**Output:** a concise alignment note with one of four recommendations: continue in the current bucket, continue as `none/free-flow`, redirect to a more relevant bucket, or update/split/create a program goal and record that change in `docs/program-goals/` plus any linked report/spec/work-unit surface. Do not perform this review as a ritual at every fresh session start.

### Work Unit Planner

**Use when:** findings need to become Speckit-ready architecture or feature work.

**Workflow:** apply the program goals and drift guard in [../../AGENTS.md](../../AGENTS.md#program-goals-and-drift-guard) when a trigger is present; use [../program-goals/](../program-goals/) as the shared backlog, goal registry, and program-goal state model. Identify whether the work belongs in a named bucket or should remain `none/free-flow`; add or move durable planned work to the relevant bucket before implementation. Define goal, success criteria, in/out of scope, affected gates/docs/maps, tests, and review points; choose whether it is feature development or architecture development.

**Output:** a concise work-unit plan ready for `speckit-specify` or architecture review, including the program-goal route, any program milestone the work advances, and the reason it is not merely local polish.

### Source-of-Truth Audit

**Use when:** content may be in the wrong layer, duplicate concept explanations appear, constitution text needs thinning, or new knowledge/workflow/rule material needs a canonical home.

**Workflow:** read [../../AGENTS.md](../../AGENTS.md#source-of-truth-layers) and [../reports/knowledge-inventory.md](../reports/knowledge-inventory.md); classify content as gate, glossary term, reference explanation, report finding, generated map, skill workflow, spec, or code; identify duplication, drift, missing links, and proposed moves.

**Output:** findings and a proposed relocation/update plan. Do not change constitutional meaning while moving explanatory material.

### Create Agent Preference

**Use when:** a user states a stable personal workflow preference, asks to remember how they want agents to work, or wants a preference like the Git operating model without turning it into shared repo doctrine.

**Workflow:** read [../../AGENTS.md](../../AGENTS.md#personal-operating-preferences) and [../reference/agent-preferences.md](../reference/agent-preferences.md); classify the request as a personal selection, reusable preference catalog/template, or shared repo rule. If it is personal, create or update a short ignored file under `.agent-prefs/` and do not commit it. If the preference needs reusable options or examples, update the committed reference catalog/template first, then ask before recording the user's local selection when the value is ambiguous. Refuse to store secrets, credentials, environment variables, or project facts as agent preferences.

**Output:** local `.agent-prefs/<preference-name>.md` content or a proposed preference file, plus any committed reference/catalog updates needed to make the preference reusable. Never commit personal preference files.

### Initialize Agent Preferences

**Use when:** `.agent-prefs/` has no preference files other than `.gitkeep`, or a user asks to set up local agent preferences for the repository.

**Workflow:** read [../../AGENTS.md](../../AGENTS.md#personal-operating-preferences) and [../reference/agent-preferences.md](../reference/agent-preferences.md#expected-preference-files). Use the expected preference table to identify known local preference files and their ask triggers. Read [../reference/git-operating-models.md](../reference/git-operating-models.md) only if Git workflow setup is relevant. Apply the maintainer/contributor Git publication path without asking for a model selection; missing preferences do not gate publication. Ask a brief setup question only for personal choices needed by the current work, such as session execution model before substantial planning/multi-session work. Create only the selected ignored files under `.agent-prefs/`; leave skipped preferences unset. Use [Create Agent Preference](#create-agent-preference) for the actual file shape. Do not ask for preferences unrelated to the current task.

**Output:** created local `.agent-prefs/*.md` files, or a short note that the user chose to decide per session. Never commit personal preference files.

## Review And Merge Skills

### Auto-Review Loop Then Merge

**Use when:** a branch or pull request is ready for review and the user wants it reviewed to convergence and then merged if the gate allows it. Also use when a review round produced fixes and the changed diff must be reviewed again before merge.

**Workflow:**

1. **Scope the diff before reading any code.** If a pull request exists, its base branch is the review base: `gh pr view --json number,url,baseRefName,headRefName,isDraft`. Otherwise use the merge base with `main`. Review the diff between that base and the branch head, and nothing else. For a stacked pull request the base is the parent branch rather than `main`: review only the commits this PR adds on top of its base, and state which base was used, because a stacked branch also carries whatever its base held when it was cut and reviewing the whole stack reports the parent's code as this PR's.
2. **Review with independent reviewers, one per lens.** Run them concurrently where the AI provider supports it, and do not let one reviewer see another's output — shared context turns independent lenses into one lens. At minimum: correctness; repo-standards conformance against [../../AGENTS.md](../../AGENTS.md), [../../.specify/memory/constitution-framework.md](../../.specify/memory/constitution-framework.md), and [../../.specify/memory/constitution.md](../../.specify/memory/constitution.md); and spec/issue conformance against the spec under `specs/` or the issue the branch names, where the branch names one. Add the lenses the change earns, such as persistence, concurrency, public API surface, or test quality.
3. **Verify every finding adversarially before acting on it.** A finding is a claim until it is traced to a specific file and line and paired with a concrete failure scenario: which inputs or state produce which wrong behavior. Discard everything else, including plausible-sounding claims, because an unverified finding costs a fix round and can leave the code worse. Audit each claim against the code itself, not against the reviewer's report or the diff summary: re-reading your own reasoning confirms nothing that writing it did not already assume. Where a finding says a guard is missing or too weak, mutation-test the guard before trusting either the guard or the finding — break the thing the guard claims to catch and confirm it goes red. A guard that stays green under that mutation is itself the finding.
4. **Apply the surviving findings, then re-establish evidence.** Rebuild, and re-run the affected suites as whole test projects rather than filtered subsets; compare what executed against what exists, because a green summary line reports only the tests that ran. If files or project references changed, refresh the generated maps with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` and stage every changed map by explicit path, `docs/maps/manifest.json` included, per [../../AGENTS.md](../../AGENTS.md#refresh-generated-maps). Use [Refresh Generated Maps](#refresh-generated-maps) for the narrower refreshes.
5. **Loop on the new diff.** Re-derive the diff against the same base and repeat from step 2 against the new head, since fixes are unreviewed code. Stop when a round produces no surviving findings, or at a stated iteration cap; three rounds is a reasonable default. Reaching the cap is an unconverged result and must be reported as one, not as a pass.
6. **Only then consider merging,** through [Merge Gate](#merge-gate). Convergence of the review loop is not itself a merge decision.

**Output:** per round, the surviving findings with file, line, and failure scenario; the claims that were discarded and which verification step they failed; and the fixes applied. At the end, whether the loop converged or hit its cap, followed by the merge decision and its evidence.

### Merge Gate

**Use when:** a branch is about to be merged, whether or not it came through the review loop.

**Workflow:** refusing to merge is the default. Merging is what a fully green gate unlocks, so treat every item below as required and stop and report when any of them does not hold. This is the gate described in [../../AGENTS.md](../../AGENTS.md#program-bookkeeping).

- **Build** the solution.
- **Affected suites** pass, run as whole test projects.
- **Architecture guard** passes: `dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj`.
- **Generated-maps check** passes: `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`.
- **Diff review** is complete and its surviving findings are resolved.
- **Bite-proof** wherever the change claims a behavioral difference: revert the fix, or mutate the code the new test covers, show the test going red, then restore and show it green. A test that passes both before and after the change proves nothing about the change.
- **Evidence posted as a pull-request comment before the merge**, written so a human can check it without rerunning anything: the commands, their results, the executed test counts, and the bite-proof's red-then-green transition. That comment is the notification of record. No comment, no merge — this holds for your own pull request as much as for a peer session's.
- **Required checks are green on the head commit.** No required check may be red. An empty or missing check list is not green: a pull request that cannot merge cleanly runs no workflows at all, so no reported checks means the gate never ran.
- **Not a draft.** Mark the pull request ready and let the gate run against it; do not merge a draft.
- **Stacked pull requests merge base-first.** A child cannot merge before its base, and merging out of order rewrites what the child's diff means.
- **A red gate is a stop even when its cause is outside this branch.** A regression already present on the base, an infrastructure failure, or an unrelated flake blocks the merge exactly like a defect in the change does. Report what is red, where it came from, and what would clear it. Do not merge past it and do not restamp it as unrelated.

**Output:** either the posted evidence comment followed by the merge, or a refusal that names the failing item, its cause, and what would clear it.

## Quality Routine Skills

Two scheduled routines carry [ADR 0080 D7](../adr/0080-elsa-4-simplification-decisions.md#d7--recurring-review-and-fix-routines) and epic [#2563](https://github.com/elsa-workflows/elsa-foundation/issues/2563). Each run starts in a fresh session on current `main`, does one bounded unit, and reports what it did in its final message. The rules below are the routine's whole contract; the scheduled prompt only invokes the skill.

### Quality Review Routine

**Use when:** the reviewer routine fires (Mondays and Thursdays), or a user asks for one review pass in the routine's format.

**Workflow:**

1. **Check the backlog first.** Count open issues labelled both `auto-review` and `needs-triage`: `gh issue list --label auto-review --label needs-triage --state open --limit 100 --json number`. If 15 or more are waiting, file nothing: report the count and stop.
2. **Pick this run's lens and area by rotation.** Let `w` be the number of whole weeks from Monday 2026-10-12 to the run's Monday, and `k = 2w + (1 if the run is on a Thursday or later in its week, else 0)`, so `k` counts reviewer slots and never resets. The lens is entry `k mod 7` of: performance, API, naming, simplification, tests, docs, structure. The area is entry `floor(k / 7) mod N` of the sorted directory names under `src/essentials`, followed by `src/extensions` as the last entry (`N` is the length of that list). Each area therefore gets all seven lenses in turn before the rotation moves on, and every lens/area pair comes up once every `7N` runs. A manual run may name its own lens and area. Do not add a second lens or area to "use up" the run.
3. **Review the area through the lens only.** Read code, not reports: findings come from `src/` and `tests/`, with `file:line` evidence. Prefer findings that remove code, types, projects, tests or concepts over findings that add them. Do not edit files, push branches or open pull requests.
4. **Check for duplicates before filing.** Search open and closed issues for the file, type or symbol: `gh issue list --state all --search "<symbol> in:title,body"`. Drop a finding that an existing issue already covers, and note the issue number in the report instead.
5. **File at most three issues.** Each carries exactly these labels: `needs-triage`, `auto-review`, one `kind:*` (performance → `kind:perf`, API → `kind:api`, naming → `kind:naming`, simplification → `kind:simplify`, tests → `kind:tests`, docs → `kind:docs`, structure → `kind:structure`) and one `size:S|M|L` (S: one PR touching a handful of files in one project; M: one PR across a few projects; L: needs splitting). The body states the evidence (`file:line`), the concrete change, how to verify it, and the lens and area of the run. Do not label an issue `ready-for-agent`; promotion is a human triage decision.
6. **Report:** lens, area, the issues filed with links, and the findings dropped as duplicates or as too weak to file.

**Output:** at most three new `needs-triage` + `auto-review` issues, or a skip report when the backlog is at 15 or more.

### Quality Fix Routine

**Use when:** the fixer routine fires (weekdays), or a user asks for one fixer pass.

**Workflow:**

1. **Check for a reverted auto-merge.** List the commits on `main` since the last time `auto-merge-paused` was removed from [#2559](https://github.com/elsa-workflows/elsa-foundation/issues/2559) (its `unlabeled` event in `gh api repos/elsa-workflows/elsa-foundation/issues/2559/events`; with no such event, the last 30 days). If one of them reverts a PR whose head branch started with `claude/auto-fix-`, and no earlier comment on #2559 already names that revert, add `auto-merge-paused` to #2559 with a comment naming the revert commit and the PR. While the label is present, merge nothing automatically. Only a maintainer removes it.
2. **Service open auto-fix pull requests.** List open PRs whose head branch starts with `claude/auto-fix-`. For each: fix a red check that the branch caused, answer review comments (fix, or reply with the reason when declining), and merge it when it meets the auto-merge rule in step 9. A check that is red for a reason outside the branch blocks the merge; report it rather than working around it.
3. **Stop at three.** If three `claude/auto-fix-*` PRs are still open after servicing, report them and start nothing new.
4. **Pick one issue.** Candidates are open issues labelled `ready-for-agent` and `auto-review`, or `simplification` and `status:todo`, that carry a `size:S` or `size:M` label. Exclude any that is labelled `type:epic`, `type:program`, `blocked`, `blocked-by-*`, `size:L`, `status:in-progress` or `status:in-review`; that carries a claim comment not followed by a release (a comment saying the work shipped or was abandoned), however old; that an open PR already references; or whose paths or kind of work fall under an open freeze window. A freeze window is open from a comment on #2559 that starts with `Freeze window open` until a later comment on #2559 that starts with `Freeze window closed`; the opening comment names the paths it covers. Prefer `size:S`, then the oldest issue. If nothing qualifies, report that and stop. Report claims older than seven days with no release as possibly stale, for a human to clear; do not take them over.
5. **Claim it.** Choose the branch name `claude/auto-fix-<issue number>-<short-slug>` first. Then post one comment on the issue naming the routine run, the worktree, that branch and the exact scope, and set `status:in-progress`, per [AGENTS.md](../../AGENTS.md#concurrent-work-claims). Re-read the issue's comments in the same step; if another claim appeared, release yours and pick again.
6. **Branch** from current `origin/main`.
7. **Implement the issue as written, and nothing else.** Build the projects you touched, run their test projects whole, and run `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`. When files or project references changed, also run the architecture guard after `bash tools/architecture/restore-ci-project-graph.sh` (without it the EF dependency guard fails for want of restore assets). Never skip, disable or quarantine a test to get green. If the issue proves wrong or larger than its size label, comment that on the issue, release the claim, and stop without a PR. Before committing, check again: re-read the issue's comments and open PRs for a competing claim, and re-read #2559 for a freeze window opened since step 4 that covers the diff's paths or its kind of work (for example adding, moving or renaming projects). If either appears, stop, release the claim, and report.
8. **Open a ready (not draft) PR** with `Closes #<n>` in the body, then post the evidence as a PR comment: commands, results, executed test counts, and a bite-proof for any behaviour change. Set `status:in-review` on the issue and link the PR there.
9. **Auto-merge only when every condition holds:** the issue is `size:S`; its kind is `kind:simplify`, `kind:docs` or `kind:tests`; #2559 is not labelled `auto-merge-paused`; no changed path has a directory segment containing `Runtime`, `Persistence`, `EntityFramework`, `Migrations` or `Stores` (this covers `Workflows/Runtime`, `Activities/*/Runtime`, every persistence and EF project, and the store implementations); and the diff removes or renames no public API (no removed line in any `PublicAPI.*.txt`, and no public type or member deleted or renamed). When all hold, take the PR through [Auto-Review Loop Then Merge](#auto-review-loop-then-merge) and its [Merge Gate](#merge-gate). Otherwise leave it open for a human merge and say which condition failed in a PR comment.
10. **Report:** whether auto-merge is paused, the PRs serviced and their state, the issue picked (or why none), the PR opened, and whether it auto-merged.

**Output:** at most one new ready PR per run, serviced auto-fix PRs, and a run report.

## Speckit And Work-Unit Skills

### Speckit Flow Guide

**Use when:** a user asks to create, plan, task, or implement a feature/work unit.

**Workflow:** create/switch to a feature branch, then follow specification -> plan -> tasks -> implementation, reviewing each artifact before the next step. With an AI integration, use the Speckit git extension and `speckit-specify`, `speckit-plan`, `speckit-tasks`, and `speckit-implement` in that order. Without one, create and review the same artifacts manually using [the contributor guide](../../CONTRIBUTING.md#plan-feature-work). The specification, constitution, testing, and review gates apply to both paths.

**Output:** specs under `specs/NNN-feature-name/`, branch-aligned work, and constitution-gated implementation.

**Future extraction note:** this flow remains in `elsa-foundation` until feature-development workspace assets move to `elsa-workspace`.

### Promote Finding To Work Unit

**Use when:** a report finding, deferred decision, weak implementation, or review question needs to become planned work.

**Workflow:** read the source report and relevant constitution gates; classify the work as architecture development, codebase verification, feature development, docs/maps/skills work, or code; use [Work Unit Planner](#work-unit-planner) to define goal, success criteria, scope, source-of-truth layer, and review points; use [Speckit Flow Guide](#speckit-flow-guide) only after the user approves moving from plan to spec.

**Output:** a proposed work-unit plan with exact files or spec surfaces to create/update.

## Feature And Extension Implementation Skills

### Create Feature Or Module

**Use when:** a new feature/module is added, ported, or split from existing code.

**Workflow:** read framework gates for three-layer separation, naming, feature identity, provider decomposition, and unit tests; identify the owning domain and dependency envelope; choose `.Core`, helper, and implementation package shape; plan behaviour tests for logic-bearing implementations and a registration test only where the feature's wiring carries logic (framework §2.23.1's composition test covers plain registrations), docs, and extension-point catalog updates before coding. After the user approves the feature/module plan, treat required tests, catalog updates, and generated-map refreshes as normal completion work.

**Output:** feature/module implementation plan or Speckit-ready scope, including package placement, dependency rules, tests, and docs/catalog updates.

### Extend Feature By Inheritance

**Use when:** one feature must extend, decorate, or specialize another feature's registration pipeline.

**Workflow:** check framework feature-inheritance rules; verify the base feature is public, inheritable, and has virtual registration; plan the derived feature registration override, service replacement/addition behavior, dependency direction, and tests. After the user approves the inheritance plan, complete required tests and extension-point documentation as follow-through.

**Output:** inheritance-based extension plan or implementation checklist with registration and test expectations.

### Add Event Contribution

**Use when:** a feature contributes to a fan-in event or lifecycle contribution surface.

**Workflow:** check event/contribution gates and the owning domain's `EXTENSION_POINTS.md`; choose the correct contributor-interface kind (`Source`, `Contributor`, `PreProcessor`, `PostProcessor`, or action-named equivalent); ensure the owning feature has one aggregating event handler for that contribution purpose; verify the publisher-owned delivery strategy is Sequential when contributions are read back; check the publisher-owned dispatcher failure policy (`Throw immediately` or `Run all then throw aggregate` for failing gates); plan the contributor's subscriber-owned failure classification, noting current code gaps where dispatcher failure policies and handler failure classifications are not yet implemented. After the user approves the contribution plan, complete required tests, catalog updates, and map refreshes as follow-through.

**Output:** contribution plan or implementation checklist with contributor contract, handler, registration, docs, and tests.

### Add Independent Event Subscriber

**Use when:** a feature observes an event for auditing, cache invalidation, telemetry, notifications, or other non-fan-in behavior.

**Workflow:** confirm the behavior is not a fan-in contribution; inspect the event contract, publisher-owned delivery strategy, publisher-owned dispatcher failure policy, and subscriber-owned failure classification; plan the handler registration and tests. If the subscriber needs stronger delivery or dispatcher-failure semantics than the event provides, raise an architecture question and model a separate event phase rather than attaching the subscriber as-is. Note current code gaps where dispatcher failure policies and handler failure classifications are not yet implemented. After the user approves the subscriber plan, complete required tests and catalog updates as follow-through.

**Output:** subscriber plan or implementation checklist with event contract, handler behavior, registration, and tests.

### Add Replacement Contract Implementation

**Use when:** exactly one implementation should be active per application/runtime context.

**Workflow:** identify the replacement contract and its owning domain; ensure the implementation does not use contribution-style `IEnumerable<T>` semantics; plan conflict detection or prevention, registration behavior, extension-point catalog updates, and tests. After the user approves the replacement-contract plan, complete required tests, catalog updates, and map refreshes as follow-through.

**Output:** replacement implementation plan or checklist with conflict semantics and test expectations.

### Add Bridge Or Adapter

**Use when:** code must connect two seams, isolate a heavy/external dependency, or translate between phase-owned contracts.

**Workflow:** read the relevant glossary/reference material, especially [../seams.md](../seams.md); identify both sides' `.Core` contracts; keep the bridge/adapter outside the domains it connects; verify dependency direction; plan failure semantics, docs, extension-point catalog updates, and tests. After the user approves the bridge/adapter plan, complete required tests, catalog updates, and map refreshes as follow-through.

**Output:** bridge/adapter plan or checklist with owner package, dependencies, translated contracts, and tests.

### Add Extension-Point Catalog Entry

**Use when:** a project exposes, implements, or changes events, contributor interfaces, replacement contracts, feature inheritance points, or other extension points.

**Workflow:** update the owning project's `EXTENSION_POINTS.md`; tag known implementations as intra-domain default or cross-domain contribution where applicable; update README cross-domain contribution notes when the contributing feature lives elsewhere; refresh generated extension-point maps when inputs changed or freshness is uncertain.

**Output:** updated extension-point documentation and map-refresh recommendation.

## Testing Skills

### Add Feature Registration Tests

**Use when:** a feature's wiring carries logic (conditional registration, decoration, options validation), or a new feature must join the composition test.

**Workflow:** read framework §2.23.1. Plain wiring is covered by the one composition test, which builds a shell from every feature and resolves its registrations; make sure the new feature is part of it. Write a dedicated registration test only for wiring that carries logic.

**Output:** the feature included in the composition test, plus a focused test only where the wiring has logic.

### Add Implementation Unit Tests

**Use when:** a logic-bearing implementation is created or changed.

**Workflow:** read framework §2.23.2. Test the behaviour that carries risk (decisions, failure paths, boundaries, concurrency and persistence semantics, infrastructure exception wrapping) at the cheapest level that proves it. Implementations are `internal sealed`; reach them through `InternalsVisibleTo` granted to the test assembly. Never assert on markdown, documentation or source text.

**Output:** behaviour tests for the implementation's risky paths; no coverage quota.

## Maps And Composition Skills

### Feature/Dependency Map Builder

**Use when:** a user asks for project, package, feature, or external NuGet dependency maps.

**Workflow:** parse `.csproj` files for `ProjectReference` and `PackageReference`; record package ID/version; group by domain; flag direct version clusters and reference signals.

**Output:** generated map/report under `docs/maps/` or `docs/reports/`, depending on whether it is stable navigation or a point-in-time finding.

### Refresh Generated Maps

**Use when:** the user explicitly requests a map refresh or authorizes one as part of an approved task.

**Workflow:** establish freshness with `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`, which is the authoritative signal; `docs/maps/manifest.json` summarizes what the v1 maps cover but does not report staleness. When the user authorizes a refresh, run the narrowest map generator that fits the task before using the map as evidence. Use the split scripts from [../../AGENTS.md](../../AGENTS.md#refresh-generated-maps). Review generated findings reports before continuing; if they expose drift that makes the current work unsafe, stop and tell the user. Record findings in reports rather than hand-editing generated facts. When no explicit refresh was requested, report freshness concerns without running a generator. Stage every changed map file by explicit path, `docs/maps/manifest.json` included: since #1278 it carries no per-commit provenance, so it changes only when the tree did, and the freshness check now compares it. See [../../AGENTS.md](../../AGENTS.md#refresh-generated-maps).

**Output:** refreshed map snapshots and any report-worthy findings.

### Feature Composition Explorer

**Use when:** a user wants to compose an API/shell from selected Elsa features.

**Workflow:** identify required capabilities; map them to projects/features; use [../reports/cshells-composition-evidence.md](../reports/cshells-composition-evidence.md#reviewed-classification-v1) for dependency/settings classification boundaries; include only required activations backed by evidence or explicit review; flag optional companions, provider/default choices, endpoint/API features, bridge features, host-loading needs, and external package/version compatibility concerns; propose a minimal shell composition before generating config.

**Output:** selected feature set, dependency rationale, missing decisions, and config-generation inputs.

### CShells Appsettings Generator

**Use when:** a user has selected features and wants appsettings JSON for a CShells/Nuplane shell.

**Workflow:** scan feature activation patterns and dependencies; consume only explicit feature IDs and reviewed classification evidence from [../reports/cshells-composition-evidence.md](../reports/cshells-composition-evidence.md#reviewed-classification-v1); generate JSON from selected features only when required activations, feature-bound settings, shell-wide settings, and host-loading outputs are classified; mark optional features separately; block generation on duplicate concrete feature IDs or unknown required settings.

**Output:** appsettings JSON and a note on how it was derived. Do not guess feature IDs, activation requirements, required setting values, secrets, host-loading output, or appsettings keys when the repo does not expose or classify them.
