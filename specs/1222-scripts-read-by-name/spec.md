# Feature Specification: Scripts Read Variables By Name

**Feature Branch**: `claude/3052-s4-scripts-read-by-name-60c01a`

**Created**: 2026-11-09

**Status**: Draft — provisional (see *Initiative status* below)

**Spec number**: 1222 (reserved on the #3052 board; S-10 = 1221, S-1 = 1223)

**Input**: Epic #3052 spec **S-4**, implementing **ADR 0089** as changed by the **minimal D-7 amendment** (scripts read
variables by name; publish pins the names it can resolve; computed and opaque reads fall back to a run-time lookup).
Analysis **A-4**, decision **D-7**. Issues: #3204, #3296, #3287 (arguments part).

## Initiative status *(provisional)*

This spec is **provisional**, written as if its ADRs were agreed, so that its plan and tasks test those decisions
against the code (initiative process, *Provisional specs*). Formal approval waits on the ADRs below.

**Decision this spec implements — D-7 / A-4, minimal amendment.** Chosen by Sipke on 6 Nov
([#3420 comment](https://github.com/elsa-workflows/elsa-foundation/pull/3420#issuecomment-6698998180)), approved by
Frans on 27 Oct, accepted by Joey on 11 Nov:

- A **literal** variable name is resolved and **pinned at publish** to the nearest declaring scope.
- A **computed** name (`getVariable(expr)`, `variables[expr]`) and an **opaque host-function read** fall back to a
  **lookup at run time**.
- Publish reports computed reads **conservatively** (which variables could be read).
- The **actual** reads are recorded, checked for protection, and become part of the step's input snapshot.
- A computed name that matches no visible variable reads `undefined` with a warning, and the read is marked absent.
- Reading a transient variable keeps its existing publish diagnostic.
- Readable names stay in source; rename updates the references tooling can resolve and flags the rest.
- Input snapshots and an activity body's variable session stay distinct; the session shows the body's pending writes.

**Depends on:**

- **ADR 0089** — *Scripts read variables by name and publish pins them as declared parameters*, with the minimal D-7
  amendment of decisions 4 and 5. The amendment text on **PR #3420** is being rewritten to this scope in parallel.
- **ADR 0092** (PR #3411, in review) — the `RunJavaScript` body reads through its *variable session*, which this spec
  stays consistent with, including the visibility of the body's pending writes.
- **spec 095** — the expression contract: declared parameters, the `Variable` parameter binding, and the importer
  lowering (§6). This spec adds exposure of pinned `Variable` parameters under a `variables` root and a run-time
  fallback lookup for computed names (changes §1 / §6; *Open question OQ-1*).
- **spec 1216 / S-2** (PR #3726) — FR-025 to FR-027 (a missing computed name reads `undefined` in JavaScript and nil in
  Liquid with a warning; a missing literal name stays a publish diagnostic). This spec keeps them.

**Superseded findings.** The earlier findings F-1 (refuse computed reads, collapse #3420 to the original ADR 0089),
F-2 (body-session carve-out) and F-3 (split out the `activities` root) are superseded by the D-7 decision. F-2's
carve-out is now part of the decision text; F-3's split stays as this spec's scope boundary.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A script reads a workflow variable by its direct name (Priority: P1)

An author (or a converted Elsa 3 workflow) writes a script expression that reads a workflow variable by name —
`variables.order`, `variables['order']`, or `getVariable('order')` — and gets the value of the variable declared in
the nearest enclosing scope, in both JavaScript and Liquid.

**Why this priority**: This is the core capability S-4 exists to deliver and the basis for every converted Elsa 3
script that reads a variable. Without it, the tracked issues cannot be closed.

**Independent Test**: Publish a workflow whose activity input or `RunJavaScript` body reads `variables.order`, run it,
and confirm the script sees the value of `order` from the nearest declaring scope, with shadowing resolved
innermost-first.

**Acceptance Scenarios**:

1. **Given** a workflow variable `order` declared on the workflow and a script `variables.order.total`, **When** the
   workflow is published and run, **Then** the script reads the workflow-level `order` value.
2. **Given** a container that declares its own `order` and a script inside it reading `variables.order`, **When** it
   runs, **Then** it reads the container's `order`, not the workflow's (nearest scope wins).
3. **Given** a loop body whose iteration scope declares `item` and a script reading `variables.item`, **When** it
   runs, **Then** it reads the iteration value.
4. **Given** `getVariable('order')` and `variables['order']` (string literals), **When** published, **Then** both
   resolve, pin and read the same variable as `variables.order`.
5. **Given** a Liquid template `{{ variables.order }}`, **When** published and run, **Then** it reads `order` from the
   nearest declaring scope.
6. **Given** a declared variable `order` with no value set, **When** a script reads `variables.order`, **Then** it
   reads JSON `null` (never `undefined`).

---

### User Story 2 - A computed variable name is read at run time (Priority: P1)

A script that builds a variable name at run time — `variables[key]` or `getVariable(name)` with a non-literal
argument — or calls a host function that reads variables it does not name in source, publishes. Publish reports which
variables the script could read; at run time the name is looked up nearest-scope-first, the actual read is recorded,
checked for protection and added to the step's input snapshot. A name that matches nothing reads `undefined` with a
warning.

**Why this priority**: Real Elsa 3 scripts use computed names; refusing them would block conversion. The fallback keeps
them faithful while literal names keep the publish-known, pinned path.

**Independent Test**: Publish a workflow containing `getVariable(name)` with a non-literal argument; confirm publish
succeeds and reports the conservative candidate set; run it and confirm the value read, the recorded read in the input
snapshot, and the `undefined` + warning for a name that matches nothing.

**Acceptance Scenarios**:

1. **Given** a script `variables[key]` where `key` evaluates to `'order'`, **When** it runs, **Then** it reads `order`
   from the nearest declaring scope, and the read of `order` is recorded in the step's input snapshot.
2. **Given** `getVariable(expr)` with a non-literal argument, **When** published, **Then** publish succeeds and reports
   the variables the script could read (the visible variables at that point), not a single pinned name.
3. **Given** a computed name that matches no visible variable, **When** the script runs, **Then** JavaScript reads
   `undefined` (never `null`), a warning is recorded, and the read is marked absent.
4. **Given** the same missing computed name in Liquid, **When** it renders, **Then** it reads nil and the same warning
   is recorded.
5. **Given** a computed name that resolves at run time to a protected variable the execution context may not read,
   **When** the script reads it, **Then** the value is not provided and the step faults with the protection diagnostic.
6. **Given** a host function that reads a variable the script does not name in source, **When** it runs, **Then** the
   read goes through the same run-time lookup and is recorded, checked and snapshotted the same way.
7. **Given** a script that mixes `variables.order` and `variables[key]`, **When** published, **Then** `order` is pinned
   and the computed read is reported conservatively; at run time both read the same `order` value.

---

### User Story 3 - A converted Elsa 3 script reads variables faithfully (Priority: P2)

An Elsa 3 workflow whose scripts read variables (`Variables.X`, `getVariable("X")`, `getVariable(expr)`, generated
`get<Name>()`) is converted so that the Elsa 4 script reads the same variables, without the script's own `args`
swallowing the reads and without publish failing on an arguments type mismatch.

**Why this priority**: These are the concrete defects (#3296, #3287) blocking real converted workflows; they are the
reason S-4 is on the board.

**Independent Test**: Convert an Elsa 3 `RunJavaScript` that reads a variable by literal and by computed name, publish
it, and confirm it publishes and reads the right values; convert a script that declares its own `args` and confirm the
converter refuses with guidance rather than silently mis-binding.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 script `getVariable("order")`, **When** converted, **Then** the Elsa 4 script reads
   `variables.order` (spelled as the variable is declared) and publishes.
2. **Given** an Elsa 3 script `getVariable(name)` with a computed argument, **When** converted, **Then** the Elsa 4
   script keeps `getVariable(name)`, publishes, and reads through the run-time fallback (it is not refused).
3. **Given** an Elsa 3 script that declares its own `args` in a scope that also contains converted reads, **When**
   converted, **Then** the converter refuses with guidance (faithful-or-refuse) rather than emitting reads that bind
   to the author's `args` (#3296).
4. **Given** a converted `RunJavaScript` that reads a variable or input, **When** published, **Then** it is not
   refused for an arguments type mismatch — the `arguments` expression carries the real type, not a hard-coded
   `Object` (#3287; the full fix pairs with #3167 in S-0).
5. **Given** a converted statement body, **When** emitted, **Then** it is not wrapped in an immediately-invoked
   function for the `RunJavaScript` body path (#3010).
6. **Given** an Elsa 3 literal read whose nearest-scope resolution in Elsa 4 would pick a different declaration than
   Elsa 3 did, **When** converted, **Then** the converter refuses that read (faithful-or-refuse).
7. **Given** an Elsa 3 computed read whose candidate names include a name declared in more than one visible scope,
   **When** converted, **Then** the converter records an import warning that the run-time lookup resolves
   nearest-scope-first.

---

### User Story 4 - Renaming never silently breaks a read (Priority: P3)

When a variable is renamed in Studio, scripts that read it by name are kept correct: references tooling can resolve
are updated, references it cannot (computed names, unusual cases) are flagged for manual repair, and a literal name
that no longer resolves is refused at publish. Published workflow versions never change.

**Why this priority**: Protects authors from a silent behaviour change on rename; the publish refusal is the safety
net for literal names, and the flag is the safety net for computed names.

**Independent Test**: Rename a variable referenced by a script in a draft, confirm Studio updates the resolvable
reference and flags a computed one; remove the declaration a script names literally and confirm publish refuses it.

**Acceptance Scenarios**:

1. **Given** a draft script reading `variables.order` and a rename of `order` to `purchase`, **When** Studio applies
   the rename, **Then** the script's reference becomes `variables.purchase`.
2. **Given** a reference Studio cannot safely update (for example a computed name that could produce `order`), **When**
   the rename runs, **Then** it is surfaced for manual repair rather than silently left or wrongly rewritten.
3. **Given** a script naming a variable literally that no longer resolves to any declaration, **When** publish runs,
   **Then** it is refused.
4. **Given** a literal name that now resolves to a *different* declaration than the previously published version,
   **When** publish runs, **Then** a warning is recorded (not a refusal).
5. **Given** an already published version, **When** anything is renamed in the draft, **Then** the published version's
   behaviour is unchanged.

---

### User Story 5 - Secret variable values never leak into logs (Priority: P3)

A script may read a protected (secret) variable by literal or computed name. A literal read is protection-checked at
publish; a computed read is checked when it happens. Its value never appears in logs or diagnostics, and a value the
execution context has no permission to read is not handed to the script at all.

**Why this priority**: Secrets handling is the one hard protection guarantee; the rest is diagnostics and author
accountability.

**Independent Test**: Read a secret variable in a script by literal and by computed name, run it, and confirm the value
is redacted everywhere it could be logged (including the recorded read); attempt to read a withheld (no-permission)
value and confirm it is not provided.

**Acceptance Scenarios**:

1. **Given** a script reading a protected variable `apiKey`, **When** it runs and anything is logged, **Then** the
   value of `apiKey` is redacted, including in the recorded read and the input snapshot view.
2. **Given** a value the execution context is not permitted to read, **When** a script names it literally or reaches it
   by a computed name, **Then** the value is not provided (access control, independent of logging).
3. **Given** a computed read whose candidate set includes a protected variable, **When** published, **Then** publish
   reports it as a possible protected read (a diagnostic, not a refusal); the actual read is checked at run time.
4. **Given** a script that copies a secret into a non-secret variable, **When** it runs, **Then** the engine does not
   hard-block it; this is the author's accountability (diagnostics are emitted where detectable).

### Edge Cases

- A literal name that resolves to no visible declaration at publish → publish refusal (US4 #3; spec 1216 FR-027).
- A literal name that resolves to a *different* declaration than the previously published version → publish warning
  (US4 #4).
- A computed name that matches no visible variable at run time → `undefined` in JavaScript, nil in Liquid, a warning,
  and the read marked absent (US2 #3–#4; spec 1216 FR-025/FR-026).
- A literal read of a **transient** (non-durable) variable → publish diagnostic (retained rule; its value cannot be
  snapshotted). A computed read that resolves at run time to a transient variable faults the step with the same
  diagnostic; publish lists transient candidates in the conservative report (*OQ-3*).
- A converted read of a variable that is transient in Elsa 4 is flagged at import with its own diagnostic.
- A script that ends with a line comment must still parse at publish (the publish parser accepts what the runtime
  parser accepts — #3171 is a side track fixing the underlying parse bug on `main`).
- `args.X` and `variables.X` both naming the same value in one script → allowed; both resolve to the same value
  (the former "two-ways ambiguity" refusal is dropped).
- A `RunJavaScript` body that writes a variable and then reads it by literal or computed name → reads its own pending
  write through its variable session (ADR 0092); an input-binding expression never sees pending writes.

## Requirements *(mandatory)*

### Functional Requirements

**Reading variables by literal name (pinned)**

- **FR-001**: Publish MUST resolve a literal variable name (`variables.X`, `variables['literal']`,
  `getVariable('literal')`) to the variable in the nearest enclosing declaring scope, resolving iteration scope,
  then enclosing container, then outer containers, then the workflow, with an inner declaration shadowing an outer
  one.
- **FR-002**: Publish MUST pin each resolved literal read as a spec 095 `Variable` declared parameter recording the
  declaring scope and variable key, without rewriting the authored source.
- **FR-003**: The evaluator MUST expose pinned variable reads under a `variables` root that is distinct from the
  `args` root, so a read of `variables.X` does not collide with an explicit `args.X` parameter.
- **FR-004**: A literal read MUST see the pinned value; it MUST NOT be served by the run-time fallback lookup.
- **FR-005**: Liquid templates MUST read variables through the same `variables` root and the same nearest-scope
  resolution as JavaScript.
- **FR-006**: A declared variable with no value set MUST read JSON `null`, never `undefined`.
- **FR-007**: For literal reads, loading MUST materialize only the variables the script names (not every visible
  variable).

**Computed names and opaque reads (run-time fallback)**

- **FR-008**: A computed variable read — `variables[expr]`, `getVariable(expr)` with a non-literal argument — and a
  host-function read of a variable not named in source MUST publish and MUST be served at run time by a lookup that
  resolves the name nearest-scope-first over the variables visible to the step.
- **FR-009**: Publish MUST report each expression with a computed or opaque read conservatively: the set of variables
  it could read (the variables visible at that point), so dependency, protection and rename tooling can use it.
- **FR-010**: Each actual run-time read MUST be recorded (name, resolved declaring scope, and whether it was present),
  checked for protection, and made part of the step's input snapshot.
- **FR-011**: A computed name that matches no visible variable MUST read as `undefined` in JavaScript (never `null`)
  and nil in Liquid, MUST record a warning, and MUST mark the read absent (spec 1216 FR-025/FR-026).
- **FR-012**: A literal name that matches no visible variable MUST remain a publish diagnostic (spec 1216 FR-027).
- **FR-013**: The fallback MUST be confined to expressions that contain a computed or opaque read; an expression with
  only literal reads MUST NOT pay the fallback's loading cost.
- **FR-014**: Publish MUST parse each script with the language's parser (JavaScript parser for JavaScript, Liquid's
  parser for Liquid), not regular expressions, to determine the literal read set and detect computed access.

**Scope of roots**

- **FR-015**: `input.X` and `workflow.correlationId` / `workflow.instanceId` MUST continue to be read as they are
  today (no new publish-time pin-by-name introduced by this spec).
- **FR-016**: Activity-output reads (`activities.<name>.<output>`) are OUT OF SCOPE for this spec and MUST be left to
  a dedicated follow-up spec; this spec MUST NOT introduce the `activities` script root.

**Transient and protected variables**

- **FR-017**: Publish MUST keep its existing diagnostic for a literal read of a transient (non-durable) variable,
  because its value cannot be snapshotted. A computed read that resolves at run time to a transient variable MUST fault
  the step with the same diagnostic.
- **FR-018**: A literal read of a protected (secret) variable MUST be protection-checked at publish (the name is
  known); a computed or opaque read MUST be protection-checked at run time for the variable actually read.
- **FR-019**: Secret variable values MUST NOT appear in logs or diagnostics (redaction) at publish or run time,
  including in recorded reads.
- **FR-020**: A value the execution context has no permission to read MUST NOT be provided to the script, whether
  named literally or reached by a computed name (access control, independent of redaction).

**Rename safety**

- **FR-021**: Readable variable names MUST stay in the authored script source (names are not replaced by internal
  identifiers).
- **FR-022**: On rename, Studio MUST update the references it can resolve and MUST surface references it cannot safely
  update — including computed reads whose candidate set contains the renamed variable — for manual repair, never
  silently breaking or retargeting a read.
- **FR-023**: Publish MUST refuse a literal name that no longer resolves to any visible declaration.
- **FR-024**: Publish MUST record a warning when a literal name now resolves to a different declaration than in the
  previously published version.
- **FR-025**: An already published workflow version MUST NOT change behaviour because of a draft rename.

**Converter (spec 247)**

- **FR-026**: The converter MUST emit `variables.X` for Elsa 3 literal variable reads (`Variables.X`,
  `getVariable("X")`, generated `get<Name>()`), spelled as the variable is declared, and MUST keep `args.X` for inputs,
  activity results and instance metadata.
- **FR-027**: The converter MUST convert an Elsa 3 computed read (`getVariable(expr)`, `Variables[expr]`) faithfully to
  the Elsa 4 computed form (`getVariable(expr)`, `variables[expr]`), served by the run-time fallback; it MUST NOT refuse
  it for being computed.
- **FR-028**: The converter MUST refuse (faithful-or-refuse), with guidance, when a script's own declared `args`
  would collide with lowered `args` reads in the same scope (#3296).
- **FR-029**: The converter MUST give the `RunJavaScript` `arguments` expression its real result type instead of a
  hard-coded `Object`, so a converted script that reads a variable or input is not refused at publish for a type
  mismatch (#3287; the full fix pairs with #3167 in S-0).
- **FR-030**: The converter MUST emit statement bodies without the immediately-invoked-function wrapper for the
  `RunJavaScript` body path (#3010).
- **FR-031**: The converter MUST refuse a literal read when Elsa 4's nearest-scope resolution would pick a different
  declaration than Elsa 3's resolution (faithful-or-refuse), and MUST record an import warning when a computed read's
  candidate names include a name declared in more than one visible scope.

**Input snapshots and the body session**

- **FR-032**: Input-binding expressions MUST read from the step's input snapshot (pinned literal reads plus recorded
  fallback reads) and MUST NOT see pending writes.
- **FR-033**: `RunJavaScript` body reads — literal and computed — MUST read through the body's variable session
  (ADR 0092), which shows the body's own pending writes; the recorded reads of the body MUST still enter its input
  snapshot. Writes themselves are out of scope (ADR 0090 / S-7); this spec MUST record the write assumptions it relies
  on for verification against S-7.

### Key Entities

- **Literal variable read**: a read whose name is a literal in source; resolved and pinned at publish.
- **Computed / opaque variable read**: a read whose name is only known at run time (`variables[expr]`,
  `getVariable(expr)`, a host function); served by the run-time fallback lookup.
- **`variables` root**: the script-facing surface exposing pinned variable reads, distinct from the `args` root, and
  backed by the fallback lookup for names that are not pinned.
- **Conservative read report**: the publish-time set of variables an expression with computed or opaque reads could
  read.
- **Recorded read**: one actual run-time read (name, declaring scope, present or absent), checked for protection and
  stored in the step's input snapshot.
- **Pinned `Variable` parameter**: a spec 095 declared parameter recording the declaring scope and variable key for
  one resolved literal read.
- **Protected (secret) variable**: a variable whose value is redacted from logs and whose read is protection-checked.
- **Transient variable**: a non-durable variable whose value is not persisted, and whose read keeps the publish
  diagnostic.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the tracked variable-read issues' reproduction cases (#3204, #3296, #3287 arguments part)
  publish and run correctly, or are refused with a clear diagnostic where faithful-or-refuse applies.
- **SC-002**: A converted Elsa 3 script that reads a variable, which previously failed to publish with an arguments
  type mismatch, now publishes.
- **SC-003**: A converted Elsa 3 script using a computed `getVariable(expr)` publishes and reads the same value it read
  in Elsa 3 for every name that resolves to the same declaration in both.
- **SC-004**: Every run-time read served by the fallback appears in the step's input snapshot as a recorded read; a
  computed name that matches nothing reads `undefined` (JavaScript) or nil (Liquid) with a warning in 100% of cases.
- **SC-005**: No secret variable value appears in any log, diagnostic or recorded-read output in the read scenarios.
- **SC-006**: Renaming a variable referenced by a script in a draft never changes a published version's behaviour; a
  script naming a removed variable literally is refused at publish 100% of the time; a computed read that could reach
  the renamed variable is flagged.
- **SC-007**: An expression with only literal reads materializes only the named variables, verified by a test that a
  large or external variable not named by the script is not loaded.

## Assumptions

- Elsa 4 is unreleased and has no production script data, so there is no backward-compatibility cost to the read model
  (ADR 0089 context).
- The `RunJavaScript` body's variable session (ADR 0092, PR #3411) is the read surface for bodies, including the
  body's pending writes; until writes exist it equals the input snapshot.
- Writes from scripts (`setVariable`, captures) are governed by ADR 0090 / S-7 and are out of scope here; the write
  assumptions this spec relies on (writes commit at output evaluation; the body session shows pending writes; a
  computed write to a non-existent variable refuses) are recorded for verification against S-7.
- The runtime and publish parsers accept the same source; the trailing-line-comment parse bug is fixed by the #3171
  side track on `main` and consumed here (publish must not reject source the runtime accepts).
- `#3167` (JSON alias inference) is delivered in S-0 and is a prerequisite for the full #3287 fix.
- The fallback's loading cost (making the conservative candidate set readable before a synchronous script runs) is
  accepted for expressions that use computed or opaque reads; publish makes it visible through the conservative report.

## Out of scope

- The `activities.<name>.<output>` script root — deferred to its own spec (overlaps spec 265).
- New publish-time pin-by-name for `input.X` and `workflow.*` — they read as today.
- Writing variables from scripts — ADR 0090 / S-7.
- The trailing-line-comment parse bug — the #3171 side track on `main`.

## Open questions

- **OQ-1**: The serialized shape marking a pinned `Variable` parameter as exposed under `variables` rather than `args`
  (a per-parameter surface flag vs a separate map), and how the compiled binding marks an expression that needs the
  fallback. This changes spec 095's expression contract and is settled in the plan.
- **OQ-2**: How loop iteration values (ADR 0028) are exposed to the pinned read and to the fallback lookup at run time
  (confirmed in the plan).
- **OQ-3**: Whether a computed read that resolves to a transient variable should fault the step (this spec's choice)
  or read `undefined` with a warning like a missing name. To verify with Sipke against the retained transient rule.
