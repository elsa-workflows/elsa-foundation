---
status: accepted
date: 2026-10-10
decision_context: Whole-product quality review of 2026-10-10 (code quality, architecture, public API, domain language, runtime persistence, structure, tests, docs, process); decided by Sipke Schoorstra.
---

# Elsa 4 simplification decisions

Status: accepted (owner decisions 2026-10-10). Sipke Schoorstra decided every topic below after a whole-product
review. This ADR records those decisions; it does not reopen them. The program that delivers them cites them by
number (for example "ADR 0080 D3").

## Context

Elsa 4 is an unreleased preview. The review measured where its complexity budget goes:

- **Runtime persistence.** Under the default `Immediate` checkpoint mode a four-activity HTTP workflow costs 635 EF
  commands and 22 checkpoints (`docs/reports/runtime-db-access/delivery-response.md`, Q1). Each leaf activity
  passes through about six queued hops and about five checkpoint commits. `Coalesced` exists but is host-wide,
  off by default, and still commits once per CLR activity that is not marked `ReplaySafe`. `CommitStrategyType`
  on `WorkflowStrategyOptions` is declared and never read.
- **Public surface.** About 4,855 public types in `src/essentials`, 84% of all types there (`tools/quality-metrics`). 66% of public interfaces have exactly
  one implementation. Two constitution rules produce this: framework §2.23.3 makes every logic-bearing
  implementation `public sealed`, and framework §2.5 requires every collaborator to be registered and consumed
  through an interface.
- **Developer experience.** No `AddElsa()` entry point; the Workbench `Program.cs` is 497 lines. A waiting
  activity needs about 18 concepts, a composite activity about 25. No guide for writing a first activity.
- **Language.** 202 glossary terms. Internal jargon leaks into public type names (Stimulus, Attention,
  Contributor, Pump, Burst, Incarnation, Passivation, Upcaster, Withheld, Stamp). Retired terms (Capability,
  Envelope) remain in 45 public types.
- **Structure.** 141 production projects, 30 of them under 300 LOC. About 157k LOC of EF migrations. Two version
  lines and about 700 lines of versioning MSBuild before any release. 26 `[Obsolete]` members guard
  compatibility with releases that never shipped.
- **Tests.** About 11.2k test methods and 407k LOC, excluding generated migrations. Framework §2.23.1 requires a registration test per feature and
  §2.23.2 requires every branch covered. An estimated 12–18% of test LOC is low value, including about 98 files
  that assert on markdown or source text. §2.21.1 and §2.23.4 make every test deletion an architect decision,
  which freezes simplification.
- **Process.** About 42% of tracked bytes in `docs`, `specs`, `src` and `tests` are meta (specs, docs, reports). About 4,500 spec/FR/ADR citations sit in
  code comments. 15 program goals were active against a cap of 2.

## Decisions

### D0 — Focus

Two programs stay active: **Elsa 4 Simplification** (this ADR) and **Runtime Efficiency**, which merges the
Runtime Database Access and Runtime Throughput goals. Every other program, including all delivery programs, is
parked for six weeks from this ADR's date and reviewed at the end of that window. Goals whose remaining work
already belongs to one of the two active programs are folded into it instead of parked (Code Reality And Test
Maturity and Architecture Review Remediation fold into Elsa 4 Simplification).

**Owner amendment, 11 October 2026:** [Foundation/Studio consolidation #2501](https://github.com/elsa-workflows/elsa-foundation/issues/2501#issuecomment-6111314461)
is the first priority now, without waiting until 21 November or for whole peer programs to finish.
[Website and documentation #2495](https://github.com/elsa-workflows/elsa-foundation/issues/2495#issuecomment-6111314757)
is reactivated in parallel, with Elsa 4 source in Foundation at `website/docs/` and native GitBook sync.
These are named exceptions to D0's parking and the two-program cap; the remaining decisions in this ADR
continue to apply. The consolidation target is `Essentials/` (src/tests/apps),
`Extensions/<capability>/` (src/optional studio/tests) and `Studio/` (shell/shared UI/tests/tooling),
with shared docs/tools/workflows at the root. This direction does not rename package or API identities.
The [11 October preservation checkpoint](https://github.com/elsa-workflows/elsa-foundation/issues/2510#issuecomment-6113334884)
admits isolated source moves, imports and consolidation validation against captured copies under #2510.
Fresh source capture and live cutover require delta reconciliation and renewed coordination. No global
freeze, cleanup or publisher activation is enacted by this amendment.

**Owner amendment, 14 October 2026:** the [BPMN Engine](../program-goals/bpmn-engine.md) program is reactivated
now instead of on 21 November. It is a named exception to D0's parking and the two-program cap, like the
11 October exceptions. Its first units are moving Elsa 4's BPMN runtime onto the `Bpmn.Semantics` package
([#2735](https://github.com/elsa-workflows/elsa-foundation/issues/2735), finishing ADR 0063) and refusing BPMN
models whose meaning changed on import ([#2736](https://github.com/elsa-workflows/elsa-foundation/issues/2736)).
The remaining decisions in this ADR apply to that work as to any other, including D3's public-surface rules
and D7's human merge for runtime and persistence PRs.

**Owner amendment, 15 October 2026:** the [Elsa 3.8/3.9 Parity](../program-goals/elsa3-parity.md) program
(#2771) is created as a named exception to D0's parking and the two-program cap, like the 11 and 14 October
exceptions, by [owner decision on valence-works/strategy#8](https://github.com/valence-works/strategy/issues/8#issuecomment-6183509892)
(maintainers only). Its decisions are recorded in [ADR 0085](0085-elsa-3-parity-identity-administration-and-scope.md);
its first units are Phase 1, users and roles. The remaining decisions in this ADR apply to that work as to any other.

**Owner amendment, 18 October 2026:** [Unified Execution #2892](../program-goals/unified-execution.md)
is authorized for end-to-end implementation as a named exception to D0's parking and two-program cap.
The [approved continuation](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6249319967)
and [BPMN authority amendment](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6249325156)
record the scope: native/BPMN lifecycle integration, extensible hosting, ETL, agent collaboration and shared tooling,
including necessary BPMN-library changes and releases. Reuse the existing #2892 hierarchy and #2735 adoption work;
keep one active implementation leaf and do not reactivate unrelated parked objectives. The remaining decisions
in this ADR continue to apply within the current owner-authorized scope.

**Owner amendment, 19 October 2026:** [Native Compensation and Saga Recovery #3035](../program-goals/native-compensation.md)
is authorized for program establishment and planning as a named exception to D0's parking and two-program cap.
The owner approved the proposed program with "This sounds great. Let's proceed and create a program."
Its [PRD #3036](https://github.com/elsa-workflows/elsa-foundation/issues/3036) includes native compensation and
integrated Elsa Studio visual authoring and recovery inspection from the first durable slice. BPMN is semantic
inspiration only, with no implementation reuse, shared compensation abstraction or dependency. Elsa 3
implementation follows separately. This amendment authorizes the program records and prepared design work;
the establishment task does not authorize production implementation, releases, deployment or reactivation of
other parked programs. The remaining decisions in this ADR continue to apply.

**Owner amendment, 19 October 2026 (later that day): the two-program cap is replaced.** Five named exceptions
in nine days show that the cap no longer describes how programs are approved. The owner retired it instead of
adding another exception. From this amendment on:

- The active programs are exactly the rows of the Active table in the
  [program-goal registry](../program-goals/README.md#registry). There is no numeric cap.
- A program becomes active only through an owner amendment to this section that names it and states its
  sequencing against the active programs: what it waits on, what it displaces, or why it can run beside them.
- Parking of every unlisted program and the 2026-11-21 parked-work review remain. The cap of 20
  `ready-for-agent` issues remains.

The same amendment widens #3035 from establishment to **end-to-end delivery of its M0–M5 milestones** ("Yes
clean it up, then deliver the program end to end"). Sequencing: it runs beside #2892 and consumes existing
runtime contracts instead of waiting on #2892; see
[its relationship to other programs](../program-goals/native-compensation.md#relationship-to-other-programs).
Releases and deployment are not included. Merging follows D7 as amended on 15 October: the Code Review app
merges once a review round leaves nothing actionable and CI is green, runtime and persistence PRs included.
The separate Elsa 3 compensation program (elsa-workflows/elsa-core#8884) is paused until the Elsa 4 design
packet #3043 is reviewed.

**Owner amendment, 23 October 2026:** [Studio Expression Developer Experience #2310](../program-goals/studio-expression-developer-experience.md)
is active again, by the owner's decision of 23 October to salvage its plan and start the program. Sequencing: the
first two units are the program records (#2367) and the expression-tooling permission-hash fix ported from #2373,
which runs as its own small PR on `main`. Milestones 2 and 3 (#2378 and #2380) are rebuilt only after the
[#3204](https://github.com/elsa-workflows/elsa-foundation/issues/3204) ADR settles how scripts read variables
(declared parameters or ambient variables, and the future of `get<Name>()`). That decision is now
[ADR 0089](0089-scripts-read-variables-by-name-and-publish-pins-them-as-declared-parameters.md), so the rebuild waits
for ADR 0089 to be accepted. The program runs beside the
other active programs, displaces none of them, and reactivates no other parked work. Releases and deployment
are not included. The remaining decisions in this ADR apply to this work as to any other.

**Owner amendment, 25 October 2026:** [Studio Hosting and Package Reuse #3340](../program-goals/studio-hosting-package-reuse.md)
is authorized as an active program. The owner instructed: "Run it alongside the other active programs. Go ahead
and create the Program, issue hierarchy and dedicated Kanban Project board on GitHub." Sequencing: run beside
the active programs, displace none, and coordinate changes to shared Studio paths with their current owners.
The program consolidates reusable Studio hosting into NuGet-consumable capabilities, reuses qualified existing
CShells/Nuplane and Elsa contracts, and proves independent package consumption. It does not reactivate the parked
Runtime Composition & Configuration or Modular Hosting Upstream Delivery backlogs. Establishment creates the
planning records and board; package implementation proceeds through scoped, claimed and triaged issues, with one
implementation leaf at a time. Later in the same session the owner set the goal to "deliver this board end to end",
authorizing implementation and integrated qualification of this named program's M0–M3 outcomes, including reviewed
PR delivery and verification on resulting main and candidate packages. This supersedes the establishment-only
session boundary; the sequencing and parked-work boundaries above remain. Releases, deployments and publisher
activation are not included. The remaining decisions in this ADR apply.

### D1 — Risk-based testing

- Test observable behaviour at the cheapest level that proves it; unit-test logic that carries real risk.
- One generated composition test proves that every feature's registrations resolve in a built shell. It replaces
  the per-feature registration tests.
- No coverage quota. Branch coverage is a reviewer's judgement, not a gate.
- Tests MUST NOT assert on markdown, documentation or source text. Mechanical rules are enforced by analyzers or
  architecture tests over compiled code.
- A refactor may rewrite or delete a test when the code it covers was removed, or when the behaviour is still
  proven elsewhere. The PR states which. No separate architect approval is needed.

Framework §2.21.1, §2.23.1, §2.23.2 and §2.23.4 are amended accordingly.

### D2 — Persistence modes, Balanced by default

The runtime offers three persistence modes, settable per host and per workflow (the workflow wins) and shown in
Studio:

| Mode | Commits | After a crash |
|---|---|---|
| `Durable` | After every activity | Resumes at the last completed activity |
| `Balanced` (default) | On suspend, fault, completion, end of burst, and every N activities | Activities since the last commit run again (at-least-once) |
| `Ephemeral` | Only on suspend or fault, plus a completion record unless `RecordCompletion` is off | A run that never suspended leaves only its completion record, or no trace with `RecordCompletion` off |

`Immediate`/`Coalesced` and `CommitStrategyType` are removed. Suspend, bookmark and fault checkpoints are flushed
in every mode (ADR 0032). The completion checkpoint is flushed in every mode, with one opt-in exception: under
`Ephemeral` with `RecordCompletion` off, a run that completes without ever having been persisted writes no
record at all. A run that already has a record (because it suspended or faulted earlier) always gets its
completion flushed, so no stored run is left looking suspended.

ADR 0073 D7 is amended: **deterministic** budgets on EF command counts per reference workflow are correctness
gates, not performance measurement, and run in CI. Wall-clock timing stays retired. The Runtime Throughput
constraint against a default cadence flip is lifted.

**Owner amendment, 12 October 2026:** three decisions taken while delivering D2
([#2585](https://github.com/elsa-workflows/elsa-foundation/issues/2585)). `Durable` commits once per activity, not
once per checkpoint: the checkpoints an activity produces are committed together, so the table's "after every
activity" holds and the per-checkpoint drain is gone. `Ephemeral` keeps the attempt-claim commit before an activity
that is not ReplaySafe (ADR 0032). The target of under 40 EF commands for ten WriteLine activities under `Balanced`
moved to epic [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564); the budgets in CI hold the
measured counts. Not a decision, but a description of the result: as implemented, `Ephemeral` commits at more points
than the table's row lists. Besides a suspension, a fault and completion, it commits a cancellation, a recorded or
resolved incident and an alteration (as every mode does), that attempt claim before an activity that is not ReplaySafe,
a child workflow dispatch, and the end of a pass that does not complete the run.
"No trace" therefore applies only to a run that completes in one pass with none of those earlier commits: all its
activities are ReplaySafe, it dispatches no child, and it is not cancelled, altered or given an incident.

### D3 — Internal by default

- Implementations are `internal sealed`. Tests reach them through `InternalsVisibleTo` to their own domain's
  test assemblies. Framework §2.23.3 is amended.
- Public types are: contracts users implement or call, models crossing a package boundary, activities, feature
  classes, options and entry points.
- Whether a type is public depends on what it means to someone building on Elsa: a host author, an activity author, a
  module author or a persistence-provider author. The categories above are the
  parts of a package those people work with. Two arguments do not count: "another project in the solution uses it,
  so it stays public", and "nothing outside its project uses it, so it goes internal".
- When a sibling Elsa production project needs a type, the sibling test decides: would a third party writing the
  same kind of package need it too? If yes, the type is an extension point and is public. If no, it stays internal
  and the owning project grants `InternalsVisibleTo` to that sibling, within the same domain only (a domain is the path key
  framework §2.23.3 defines, not a #2591 delivery batch). A need from a project in another domain means the type is an extension point or is in the wrong place; it never gets a
  cross-domain grant.
- An interface exists only where more than one implementation is meaningful or where it is a documented extension
  point. Framework §2.5 is amended: a collaborator becomes a contract when it becomes an extension point, not
  before.
- PublicApiAnalyzers runs on every packable project; a change to the public surface is a reviewed diff.

**Owner amendment, 14 October 2026:** decisions taken while delivering D3
([#2591](https://github.com/elsa-workflows/elsa-foundation/issues/2591#issuecomment-6163682967)). There is no numeric
target. The original "fewer than 1,500 public types" is withdrawn: the public-type count stays a reported metric
(`tools/quality-metrics`), never a goal. The rule above on what makes a type public, and the sibling test, were added
by the same decision. Framework §2.23.3 is amended again (constitution 6.1.0): a production project may now grant
`InternalsVisibleTo` to a production project of its own domain, which 6.0.1 refused, and never to one in another
domain. #2591 tracks auditing domains 1 and 2, already realigned, again under this rule. The same change brought two older
lines up to date: D3's first bullet now names the domain's test assemblies, as framework 6.0.1 decided on 13 October,
and the Consequences no longer say Elsa 3 users lose "stimulus", which the owner kept the same day.

### D4 — Pre-GA cleanup

- Delete every `[Obsolete]` member and v1-compatibility surface now.
- One version line until GA; Line A (ADR 0067) and its MSBuild machinery are retired. Elsa constitution §E5's
  two-line gate is suspended until GA (Elsa constitution 5.0.0).
- EF migrations are squashed to one initial migration per context, once, in an announced freeze window just
  before the release candidate. Preview databases are recreated at that point.
- After GA the normal deprecation policy applies.

### D5 — Plain language, renamed now

Jargon in public and internal type names is renamed before GA, one term per batch in a freeze window, paired
with each domain's D3 internalization so no type is renamed twice. Old names are banned by an analyzer. The suffix rules in framework §2.6.1 and Elsa §E6 R4 follow the rename:
`…Extension` replaces `…Contributor`, domain by domain; both suffixes are accepted until the rename completes.

| Today | Becomes |
|---|---|
| Stimulus | Stimulus (kept; see the owner amendment below) |
| Contributor (`I*Contributor`) | Extension (`I*Extension`) |
| Attention | Alert |
| Pump | Worker |
| Burst | ExecutionPass |
| Incarnation | ProcessRunId |
| Passivation | Unload |
| Upcaster | SchemaMigrator |
| Withheld | Redacted |
| Stamp | SchemaVersion |
| Capability, Envelope (retired) | Feature / Module, or the domain word that fits |

The glossary splits into a user glossary of about 40 terms and a contributor glossary. Process vocabulary (work
unit, ratification, phase-owned) does not appear in product docs or type names.

**Owner amendment, 13 October 2026:** decisions taken while delivering D3 and D5
([#2591](https://github.com/elsa-workflows/elsa-foundation/issues/2591)). The old names are banned by a real Roslyn
analyzer, not an architecture test. Every production project references it, and the lock-file churn that brings is
accepted. A term is banned domain by domain, as each domain is realigned, not across the repository at once. Not a
decision, but a description of the result: the analyzer is `Elsa.Architecture.Analyzers`, kept in `Essentials/build/`
outside the module roots because it is build tooling, not a domain. It reports a retired term in a type name, public or
internal (`ELSANAME001`), and in a namespace (`ELSANAME002`), naming the replacement, and a term list it cannot parse
is itself an error (`ELSANAME003`). A domain activates its terms with a section for its folder in the root
`.editorconfig` (key `elsa_naming.retired_terms`, for example `Pump|Pumps=Worker`). The analyzer reference is
analyzer-only, which NuGet leaves out of the consumers' restore graphs, so no existing lock file changed.

**Owner amendment, 13 October 2026 (later the same day):** two adjustments from a BPMN review
([#2591](https://github.com/elsa-workflows/elsa-foundation/issues/2591#issuecomment-6147212044)).

- Stimulus is not renamed. Signal collides with BPMN's Signal, the broadcast throw and catch event, which Elsa's BPMN
  engine exposes in the API and in Studio next to the runtime concept. Trigger is taken too: Elsa uses it for start
  triggers. "Stimulus" stays and is not a retired term.
- `Essentials/src/Activities/Bpmn` is left out of the Activities domain's D3 and D5 work. ADR 0063 is being finished:
  Elsa 4's BPMN token engine is replaced by the `Bpmn.Semantics` package, which Elsa 3 already ships on (3.9.0).
  The BPMN program is parked until 21 November 2026, and whatever survives the switch is made internal and renamed then.

### D6 — Meta diet

- `docs/reports` and closed specs move to an archive outside `main`. Only live reports stay.
- Code comments do not cite specs, FRs or ADRs; traceability lives in commits and PRs.
- The two constitutions are compacted to enforceable gates, under 400 lines in total. `AGENTS.md` stays under 120
  lines.
- ADR and spec numbering collisions are fixed; finished specs are marked as such.
- Findings go into issues, not new report files.

### D7 — Recurring review and fix routines

- A **reviewer** routine runs twice a week. Each run applies one rotating lens (performance, API, naming,
  simplification, tests, docs, structure) and files at most three issues, labelled `needs-triage`,
  `auto-review`, `kind:*` and `size:*`, after checking for duplicates. It skips filing while 15 or more
  `auto-review` issues wait for triage.
- A **fixer** routine runs on weekdays. It takes one ready issue, claims it, and opens one PR, keeping at most
  three open.
- Auto-merge applies from day one to `size:S` PRs of kind simplify, docs or tests, through the
  `elsa-auto-review-merge` gate on a fully green CI. Runtime and persistence code and public renames always get a
  human merge. One reverted auto-merge pauses auto-merge until a maintainer re-enables it.
- A **UX QA** routine runs twice a week. It builds the Workbench and Studio from `main`, walks the golden path,
  and explores one rotating area through one user lens in a real browser. It files at most three defects and two
  proposals, labelled `auto-qa`. A defect that a second agent reproduced and whose fix needs no
  design choice goes straight to `ready-for-agent`. Proposals to simplify, replace or remove UI stay
  `needs-triage`. Every run appends what worked and what it filed to the `ux-qa-ledger` issue. The fixer routine
  takes the `ready-for-agent` ones, Studio's included, and their PRs always get a human merge.
- A **product scout** routine runs weekly. It mines feature requests, discussions and workarounds in
  elsa-core, elsa-studio and elsa-foundation, the UX QA ledger, deferred specs and Elsa 3 parity gaps, using other
  engines' public docs only as prior art. It files at most two evidence-backed proposals, labelled
  `needs-triage` and `auto-product` and never `ready-for-agent`. Deciding what gets built stays a maintainer
  decision. Each run records its candidates and near misses on the `product-scout-ledger` issue.
- Program metrics (public types, projects, test LOC, EF commands per reference workflow, glossary size,
  `[Obsolete]` count, code citations) are reported weekly. The public-type count is reported, not targeted (D3), so it does not count toward the pause trigger below.
  If fewer than half of the filed issues are promoted after four weeks, or the metrics do not move, the routines are
  paused and their prompts revised.

**Owner amendment, 15 October 2026:** merging under D7 follows the review loop of the org-wide
[Issue Bus Lite][org-agents-review] protocol, by
[owner decision quoted on #2766](https://github.com/elsa-workflows/elsa-foundation/pull/2766#issuecomment-6183395119)
and recorded for the org in [elsa-workflows/.github#6](https://github.com/elsa-workflows/.github/pull/6). The Code
Review app (`elsa-workflows-review-bot[bot]`) merges every PR, including the runtime, persistence and public-rename
PRs and the UX QA fix PRs that D7 sent to a human merge, once a review round leaves nothing actionable and CI is
green on the exact head commit. Actionable findings are fixed in new commits and reviewed again, for at most 6
rounds; if blocking findings remain after the sixth, the PR is escalated to the maintainer with
`blocked: review loop exhausted` and `needs:decision`. D7's human-merge cases are dropped, and so is the 14 October
note's reference to D7's human merge. D7's pause after a reverted auto-merge is kept for now, pending owner confirmation (see
[this comment](https://github.com/elsa-workflows/elsa-foundation/pull/2766#issuecomment-6199163408)): while #2559
carries `auto-merge-paused`, routine PRs request no Code Review. The fixer routine never merges
its own PRs and no longer merges through `elsa-auto-review-merge`; it requests review and stops. Routine PRs carry
the `auto-fix` label, which replaces the `claude/auto-fix-*` branch prefix as the way the routine finds its own PRs
and their reverts.
D7's routines, caps and labels are otherwise unchanged.

## Consequences

- Framework constitution 4.0.1 → 5.0.0 (MAJOR: §2.5, §2.21.1 and §2.23 rules removed or redefined). Elsa
  constitution 4.2.0 → 5.0.0 (MAJOR: §E5's two-line gate suspended until GA, §E6 R4 suffix follows D5, §2.21.1
  summary updated).
- Breaking changes (renames, internalization, migration squash, mode names) are accepted because nothing has
  shipped. They become far more expensive after GA, so they come first.
- Existing tests that assert on documentation or source text are deleted, not migrated.

[org-agents-review]: https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#review-and-merge-rules
