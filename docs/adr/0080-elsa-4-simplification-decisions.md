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
- **Process.** About 42% of tracked bytes in `docs`, `specs`, `src` and `tests` are meta (specs, docs, reports). About 4,000 spec/FR/ADR citations sit in
  code comments. 15 program goals were active against a cap of 2.

## Decisions

### D0 — Focus

Two programs stay active: **Elsa 4 Simplification** (this ADR) and **Runtime Efficiency**, which merges the
Runtime Database Access and Runtime Throughput goals. Every other program, including all delivery programs, is
parked for six weeks from this ADR's date and reviewed at the end of that window. Goals whose remaining work
already belongs to one of the two active programs are folded into it instead of parked (Code Reality And Test
Maturity and Architecture Review Remediation fold into Elsa 4 Simplification).

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
in every mode (ADR 0032). The completion checkpoint is flushed in every mode except `Ephemeral` with
`RecordCompletion` off, which is the one opt-in case that keeps no record of a finished run.

ADR 0073 D7 is amended: **deterministic** budgets on EF command counts per reference workflow are correctness
gates, not performance measurement, and run in CI. Wall-clock timing stays retired. The Runtime Throughput
constraint against a default cadence flip is lifted.

### D3 — Internal by default

- Implementations are `internal sealed`. Tests reach them through `InternalsVisibleTo` to their own test
  assembly. Framework §2.23.3 is amended.
- Public types are: contracts users implement or call, models crossing a package boundary, activities, feature
  classes, options and entry points.
- An interface exists only where more than one implementation is meaningful or where it is a documented extension
  point. Framework §2.5 is amended: a collaborator becomes a contract when it becomes an extension point, not
  before.
- PublicApiAnalyzers runs on every packable project; a change to the public surface is a reviewed diff.
- Target: fewer than 1,500 public types.

### D4 — Pre-GA cleanup

- Delete every `[Obsolete]` member and v1-compatibility surface now.
- One version line until GA; Line A (ADR 0067) and its MSBuild machinery are retired. Elsa constitution §E5's
  two-line gate is suspended until GA (Elsa constitution 5.0.0).
- EF migrations are squashed to one initial migration per context, once, in an announced freeze window just
  before the release candidate. Preview databases are recreated at that point.
- After GA the normal deprecation policy applies.

### D5 — Plain language, renamed now

Jargon in public and internal type names is renamed before GA, one term per batch in a freeze window, paired
with each domain's D3 internalisation so no type is renamed twice. Old names are banned by an analyzer. The suffix rules in framework §2.6.1 and Elsa §E6 R4 follow the rename:
`…Extension` replaces `…Contributor`, domain by domain; both suffixes are accepted until the rename completes.

| Today | Becomes |
|---|---|
| Stimulus | Signal |
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
- Program metrics (public types, projects, test LOC, EF commands per reference workflow, glossary size,
  `[Obsolete]` count, code citations) are reported weekly. If fewer than half of the filed issues are promoted
  after four weeks, or the metrics do not move, the routines are paused and their prompts revised.

## Consequences

- Framework constitution 4.0.1 → 5.0.0 (MAJOR: §2.5, §2.21.1 and §2.23 rules removed or redefined). Elsa
  constitution 4.2.0 → 5.0.0 (MAJOR: §E5's two-line gate suspended until GA, §E6 R4 suffix follows D5, §2.21.1
  summary updated).
- Breaking changes (renames, internalisation, migration squash, mode names) are accepted because nothing has
  shipped. They become far more expensive after GA, so they come first.
- Existing tests that assert on documentation or source text are deleted, not migrated.
- Elsa 3 users lose the familiar term "stimulus"; the migration guide maps it.
