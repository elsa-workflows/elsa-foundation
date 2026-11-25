# Tasks: Elsa 3 to Elsa 4 migration skill and documentation

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md).

**Skill folder**: `plugins/elsa3-migration/skills/elsa3-workflows-migration/` (written `skill/` below).
**Sample root**: `extensions/Elsa3/tests/Migration/Sample/` (written `Sample/` below).

**Tests**: The sample's xunit tests (framework §2.23: they prove each *port* example behaves as the catalog says)
and the check's `--self-test`. The skill text itself is verified by the walkthrough (T029–T031). No test framework
beyond xunit.

**Dependencies outside this spec**: T013 and case 14 follow the converter's global-matching rule (same member name
after the prefix). T018's case 17 follows the storage-driver migration guide in `docs/migration/`. T021 takes the tool's command line from spec 1237 and the migration host's settings from spec 1236. T020, T021 and T029–T031 need spec 1237 implemented (its `inventory` and `assess` commands and
`report.json`). Everything else can start once this spec is approved.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 to US6 from spec.md

---

## Phase 1: Setup

- [ ] T001 Create `Sample/Elsa3.Migration.Sample/Elsa3.Migration.Sample.csproj` (`IsPackable=false`, references only the Elsa 4
  authoring projects the plan lists, CShells abstractions) and `Sample/Tests/Elsa3.Migration.Sample.Tests.csproj`
  (xunit), the paths the architecture guard derives from the names; add both to `Elsa.Server.slnx` under
  `/extensions/Elsa3/tests/Migration/Sample/`, and restore both to create their
  `packages.lock.json` (CI restores in locked mode).
- [ ] T002 Add the test project to the PR tier in `tools/ci/test-tiers.json`; run
  `dotnet run --project tools/maps/Elsa.Maps.Generator -- test-tiers-check`.
- [ ] T003 Run `Elsa3MigrationBoundaryTests` and `ArchitectureGuardTests` with the new projects; if the folder or
  `Elsa3.` prefix trips a guard, move or rename the projects to what the guard derives (not the guard) and update plan P-4.

---

## Phase 2: Foundational — sample and check (blocks US3)

Each task adds one marked region `// catalog:case-NN begin/end` and its tests. T008, T009, T013 and T014 also register in
`AcmeFeature` and extend T004's registration test, so they run one after another after T004, and the case 18 region
is final only after T014. All live in `Sample/Elsa3.Migration.Sample/` and
`Sample/Tests/`; tasks touching different files are [P].

- [ ] T004 Feature registration (case 18): `AcmeFeature` as a `[ShellFeature]` `IShellFeature`; a test that the
  feature registers its services, which each later registering task extends.
- [ ] T005 [P] Base class, type name, inputs, outputs, completion (cases 1–5): `Acme.SendInvoice` with a result record,
  `[ActivityInput]` with default, UI hint and `[Required]`, `[Output]`, `ActivityTransition.Complete`; a second
  activity whose name cannot match, with `[ReplacesActivityType("Acme.Legacy.Notify")]`. Tests: result and default.
- [ ] T006 [P] Outcomes (case 6): `Acme.Approve` with `[ActivityOutcome]`s; tests for each outcome.
- [ ] T007 [P] Bookmarks (case 8, port part): `Acme.WaitForPayment` as a stateful activity that suspends and resumes on
  a `[ResumeTarget]`; tests for suspend and resume.
- [ ] T008 Trigger (case 9): `Acme.InvoiceReceived` trigger activity and its trigger stimulus provider registered
  in `AcmeFeature`; tests for trigger delivery and stimulus description.
- [ ] T009 Services (case 10): an activity taking `IInvoiceClient` by constructor, with `IInvoiceClient` registered in
  `AcmeFeature`; test with a fake.
- [ ] T010 [P] Variables and workflow state (case 11, port part): `VariableReference<decimal>` read and write through
  `context.Variables`; an `[AccessesUndeclaredVariables]` read by name; an `[AccessesWorkflowState]` activity setting an output; tests with fake step variables and
  workflow state.
- [ ] T011 [P] Background (case 12): an activity marked `[BackgroundExecutionSafe]` that honours the cancellation token
  and keys its effect on `LogicalInvocationId` (stable across operator retries; `InvocationId` is not); test for
  cancellation.
- [ ] T012 [P] Cancellation and faults (case 13): fault and cancel transitions; tests for each.
- [ ] T013 JavaScript host function (case 14): `acme.lookupCustomer` declared in `AcmeFeature` so that the converter's
  global-matching rule maps the Elsa 3 global `lookupCustomer` to it; test calling the
  implementation with JSON arguments.
- [ ] T014 Variable type (case 16): a descriptor provider registering `acme.Invoice`; test that the descriptor has
  the dotted alias. The catalog's case 16 "before" example uses the bare alias `Invoice` and states the matching rule
  of research R-2.
- [ ] T015 Tests (case 19): the region is one of the tests above, chosen as the example of building an activity
  execution context and calling the activity.
- [ ] T016 `tools/skills/check_migration_skill.py` per [contracts/catalog-entry.md](contracts/catalog-entry.md), with
  `--self-test` covering every failure rule.
- [ ] T017 Add a step running the check (self-test, then real) next to `Check catalog and module README links` in
  `.github/workflows/ci.yml`, and add `plugins/**` to the documentation-only paths in `tools/ci/change_scope.py`
  with a case in its `--self-test`.

**Checkpoint**: sample builds, its tests pass, self-test passes.

---

## Phase 3: User Story 3 — the porting catalog (P1)

- [ ] T018 [US3] Write `skill/porting-catalog.md`: an introduction (how to use a case, the verdicts, apply cases in
  order), then cases 01–19 in the [entry shape](contracts/catalog-entry.md) with the FR-014 verdicts and the R-2
  evidence turned into plain guidance. Case 14 states the replaced-global rules of research R-2. *Port* "after" blocks are copied from the sample regions; *redesign* parts name
  the alternatives and offer no workaround code. Case 17 is an *interview*, once per driver type: restate the
  questions and goals of the Elsa 3 storage-driver migration guide in `docs/migration/` (secret, bulky data, another
  store, no reason), verify the Elsa 4 features it names in the code, and link the guide for further reading. Elsa 3 "before" examples are written for the catalog, synthetic.
- [ ] T019 [US3] Run the check; fix until it passes. Review that every case's search finds the "before" example.
  SC-006: rename an Elsa 4 member a region uses in a scratch commit, confirm the sample build or the check fails, then
  drop the commit.

**Checkpoint**: the catalog stands alone and is usable by hand.

---

## Phase 4: User Story 1 — the guided journey (P1)

- [ ] T020 [US1] Write `skill/migration-plan-template.md` per [contracts/plan-template.md](contracts/plan-template.md)
  and [data-model.md](data-model.md), with one example row per table using `Acme` names; the accepted-refusal example uses the placeholder `<code>`.
- [ ] T021 [US1] Write `skill/SKILL.md`: front matter per [contracts/adapters.md](contracts/adapters.md); then, in
  order, the privacy statement (FR-005, FR-006: what reaches the AI provider; ask before each new raw definition or
  source file, naming the file and the JSON path), what the skill does and does not do (FR-021), "read the plan first" (FR-007), asking for and recording the clone's path and commit when the plan has none (FR-004), the `.agents/skills/` copy instruction ([contracts/adapters.md](contracts/adapters.md)),
  the prerequisite check and the `dotnet run` invocation copied from spec 1237's command-line contract (FR-022), the four stages (FR-008–FR-012, R-6 searches, the report fields of
  [contracts/report-consumption.md](contracts/report-consumption.md)), the version check (R-4), the handoff
  (FR-020), and the edge cases of the spec as plain rules. No diagnostic code literal; read code, message and
  guidance from the report.
- [ ] T022 [US1] Run the check on the skill folder (no diagnostic codes); review the folder for vendor or model
  names (FR-002, SC-004).

## Phase 5: User Story 2 — readable without an AI (P1)

- [ ] T023 [US2] Add the "Migrating from Elsa 3" section to `README.md`: what the migration does, the link to
  `skill/SKILL.md`, that installing it as a skill is optional, how to install it (T027), and what it does not cover
  (FR-021). At most about fifteen lines.

## Phase 6: User Story 4 — privacy (P2)

- [ ] T024 [US4] Review the privacy statement T021 wrote against US4's scenarios: it comes first, and it asks before each new raw
  definition or source file, naming the file and the JSON path.

## Phase 7: User Story 5 — contributor triggers (P2)

- [ ] T025 [P] [US5] Add a "Changing the converter" section to `extensions/Elsa3/src/Mapping/README.md` (FR-018): a
  converter change needs JTest suites for it (the JTest suite folder of spec 1235, written as a code path, not a link,
  until that folder exists) and a skill update (catalog, plan
  template, and case 17 when the storage-driver migration guide changes); an Elsa 4 activity authoring API change used
  by a *port* case needs the sample and catalog updated.
- [ ] T026 [P] [US5] Add a one-line pointer to that section in `AGENTS.md`, and one line in `docs/skills/catalog.md`
  saying that user-facing product skills live under `plugins/` and are not contributor workflows.

## Phase 8: User Story 6 — install (P3)

- [ ] T027 [P] [US6] Add `.claude-plugin/marketplace.json` and `plugins/elsa3-migration/.claude-plugin/plugin.json`
  per [contracts/adapters.md](contracts/adapters.md) (no version field).
- [ ] T028 [US6] Install the plugin from the repository marketplace in Claude Code and confirm the skill is listed;
  copy the skill folder into a scratch workspace's `.agents/skills/` and confirm an agent lists it (quickstart).

---

## Phase 9: Walkthrough and finish (needs spec 1237)

- [ ] T029 Build a synthetic Elsa 3 export and Elsa 3 source (one custom activity, one JavaScript function, one variable
  type, one storage driver) outside the repository; no customer material.
- [ ] T030 SC-001: a reviewer with no AI follows the README and `SKILL.md`, runs the inventory and ports the activity.
- [ ] T031 SC-002 and SC-005: run the skill with two different AI assistants; compare plans; stop and resume one
  session mid-`prepare`. Fix the skill text where the plans differ. Post the evidence on the PR.
- [ ] T032 Set `**Status**: Implemented` in `spec.md` in the last implementation PR.

## Dependencies

- Phase 1 → Phase 2 → US3 (T018 needs the regions).
- US1 (T020, T021) needs spec 1237's report schema agreed; US4 edits the same file as T021 (sequence them).
- US2 needs `SKILL.md` to exist (link target). US5 and US6 are independent of the rest.
- Phase 9 needs spec 1237 implemented and everything above.

## Implementation strategy

Sample and check first, so the catalog is born verified. Then the catalog (usable by hand on its own), then the
skill and plan template, then the docs and adapters. The walkthrough closes it once the tool exists.
