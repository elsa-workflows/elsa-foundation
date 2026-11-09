# Feature Specification: Scripts Read Variables By Name

**Feature Branch**: `claude/3052-s4-scripts-read-by-name-60c01a`

**Created**: 2026-11-09

**Status**: Draft (**provisional** — see *Initiative status* below)

**Spec number**: 1222 (reserved on the #3052 board; S-10 = 1221, S-1 = 1223)

**Input**: Epic #3052 spec **S-4**, implementing **ADR 0089** (scripts read variables by name and publish pins
them as declared parameters). Analysis **A-4**, decision **D-7**. Issues: #3204, #3296, #3287 (arguments part).

## Initiative status *(provisional)*

This spec is **provisional**, written as if its ADRs were agreed, so that its plan and tasks test those decisions
against the code (initiative process, *Provisional specs*). Formal approval waits on the ADRs below.

**Depends on:**

- **ADR 0089** — *Scripts read variables by name and publish pins them as declared parameters.* This spec implements
  the **original** ADR 0089 read model (resolve + pin direct reads at publish; refuse computed reads — its decision 5;
  expose pinned reads under a `variables` root). See *Finding F-1*: the amendment proposed in **PR #3420** is largely
  unwound by deferring computed reads, and should be reduced accordingly.
- **ADR 0092** (PR #3411, in review) — the `RunJavaScript` body reads through its *variable session* view, which this
  spec keeps consistent with. Until writes exist (ADR 0090 / S-7), the session view equals the pinned input snapshot.
- **spec 095** — the expression contract: declared parameters, the `Variable` parameter binding, and the importer
  lowering (§6). This spec adds exposure of a pinned `Variable` parameter under a `variables` root (changes §1 / §6;
  *Open question OQ-1*).

**Findings that flow back (to PR #3420 / epic):** F-1 (deferring computed reads collapses the amendment to the
original ADR 0089), F-2 (the ADR's "run time sees only declared values" needs a body-session carve-out), F-3 (the
`activities.<name>.<output>` root is split out to its own spec). See *Findings* at the end.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A script reads a workflow variable by its direct name (Priority: P1)

An author (or a converted Elsa 3 workflow) writes a script expression that reads a workflow variable by name —
`variables.order`, `variables['order']`, or `getVariable('order')` — and gets the value of the variable declared in
the nearest enclosing scope, in both JavaScript and Liquid.

**Why this priority**: This is the core capability S-4 exists to deliver and the basis for every converted Elsa 3
script that reads a variable. Without it, the four tracked issues cannot be closed.

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
   resolve and read the same variable as `variables.order`.
5. **Given** a Liquid template `{{ variables.order }}`, **When** published and run, **Then** it reads `order` from the
   nearest declaring scope.
6. **Given** a declared variable `order` with no value set, **When** a script reads `variables.order`, **Then** it
   reads JSON `null` (never `undefined`).

---

### User Story 2 - A dynamic (computed) variable name is refused at publish (Priority: P2)

A script that builds a variable name at run time — `variables[someExpr]`, `getVariable(expr)`, or passes `variables`
/ `getVariable` around as a value — is **refused at publish** with a clear, stable diagnostic and guidance, rather
than silently loading every visible variable before each evaluation.

**Why this priority**: Supporting computed names forces a hidden "load every visible variable before each evaluation"
cost (synchronous script reads, asynchronously stored values, unknown key) that becomes a performance cliff under
repeated activity evaluation. Deferring it keeps the default path honest; the capability gets a dedicated spec.

**Independent Test**: Publish a workflow containing `getVariable(name)` with a non-literal argument and confirm
publish fails with the dedicated diagnostic and guidance, and does not publish.

**Acceptance Scenarios**:

1. **Given** a script `variables[key]` where `key` is not a string literal, **When** publish runs, **Then** it is
   refused with a stable diagnostic that names the construct and points to the direct-name form.
2. **Given** `getVariable(expr)` with a non-literal argument, **When** publish runs, **Then** it is refused with the
   same diagnostic family.
3. **Given** a script that assigns `getVariable` or `variables` to another name and calls it, **When** publish runs,
   **Then** it is refused.
4. **Given** the refusal, **When** the author reads the diagnostic, **Then** it tells them to use a direct variable
   name and notes that computed names are a deferred capability.

---

### User Story 3 - A converted Elsa 3 script reads variables faithfully (Priority: P2)

An Elsa 3 workflow whose scripts read variables (`Variables.X`, `getVariable("X")`, generated `get<Name>()`) is
converted so that the Elsa 4 script reads the same variables, without the script's own `args` swallowing the reads
and without publish failing on an arguments type mismatch.

**Why this priority**: These are the concrete defects (#3296, #3287) blocking real converted workflows; they are the
reason S-4 is on the board.

**Independent Test**: Convert an Elsa 3 `RunJavaScript` that reads a variable, publish it, and confirm it publishes
and reads the right value; convert a script that declares its own `args` and confirm the converter refuses with
guidance rather than silently mis-binding.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 script `getVariable("order")`, **When** converted, **Then** the Elsa 4 script reads
   `variables.order` (spelled as the variable is declared) and publishes.
2. **Given** an Elsa 3 script that declares its own `args` in a scope that also contains converted reads, **When**
   converted, **Then** the converter refuses with guidance (faithful-or-refuse) rather than emitting reads that bind
   to the author's `args` (#3296).
3. **Given** a converted `RunJavaScript` that reads a variable or input, **When** published, **Then** it is not
   refused for an arguments type mismatch — the `arguments` expression carries the real type, not a hard-coded
   `Object` (#3287; the full fix pairs with #3167 in S-0).
4. **Given** a converted statement body, **When** emitted, **Then** it is not wrapped in an immediately-invoked
   function for the `RunJavaScript` body path (#3010).
5. **Given** an Elsa 3 read whose nearest-scope resolution in Elsa 4 would pick a different declaration than Elsa 3
   did, **When** converted, **Then** the converter refuses that read (faithful-or-refuse).

---

### User Story 4 - Renaming never silently breaks a read (Priority: P3)

When a variable is renamed in Studio, scripts that read it by name are kept correct: references Studio can resolve are
updated, references it cannot (e.g. unusual cases it cannot analyse) are flagged for manual repair, and a name that no
longer resolves is refused at publish. Published workflow versions never change.

**Why this priority**: Protects authors from a silent behaviour change on rename; the publish refusal is the safety
net under any rename mechanism.

**Independent Test**: Rename a variable referenced by a script in a draft, confirm Studio updates the resolvable
reference; remove the declaration a script names and confirm publish refuses it.

**Acceptance Scenarios**:

1. **Given** a draft script reading `variables.order` and a rename of `order` to `purchase`, **When** Studio applies
   the rename, **Then** the script's reference becomes `variables.purchase`.
2. **Given** a reference Studio cannot safely update, **When** the rename runs, **Then** it is surfaced for manual
   repair rather than silently left or wrongly rewritten.
3. **Given** a script naming a variable that no longer resolves to any declaration, **When** publish runs, **Then** it
   is refused.
4. **Given** a name that now resolves to a *different* declaration than the previously published version, **When**
   publish runs, **Then** a warning is recorded (not a refusal).
5. **Given** an already published version, **When** anything is renamed in the draft, **Then** the published version's
   behaviour is unchanged.

---

### User Story 5 - Secret variable values never leak into logs (Priority: P3)

A script may read a protected (secret) variable by its direct name; its protection is checked at publish, its value
never appears in logs or diagnostics, and a value the execution context has no permission to read is not handed to the
script at all.

**Why this priority**: Secrets handling is the one hard protection guarantee; the rest is diagnostics and author
accountability.

**Independent Test**: Read a secret variable in a script, run it, and confirm the value is redacted everywhere it
could be logged; attempt to read a withheld (no-permission) value and confirm it is not provided.

**Acceptance Scenarios**:

1. **Given** a script reading a protected variable `apiKey`, **When** it runs and anything is logged, **Then** the
   value of `apiKey` is redacted.
2. **Given** a value the execution context is not permitted to read, **When** a script names it, **Then** the value is
   not provided (access control, independent of logging).
3. **Given** a script that copies a secret into a non-secret variable, **When** it runs, **Then** the engine does not
   hard-block it; this is the author's accountability (diagnostics are emitted where detectable).

### Edge Cases

- A name that resolves to no visible declaration at publish → publish refusal (US4 #3).
- A name that resolves to a *different* declaration than the previously published version → publish warning (US4 #4).
- A read of a **transient** (non-durable) variable → publish refusal (its value cannot be snapshotted); a converted
  read of a variable that is transient in Elsa 4 is flagged at import with its own diagnostic.
- A script that ends with a line comment must still parse at publish (the publish parser accepts what the runtime
  parser accepts — #3171 is a side-track fixing the underlying parse bug on `main`).
- `args.X` and `variables.X` both naming the same value in one script → allowed; both resolve to the same value
  (the former "two-ways ambiguity" refusal is dropped).

## Requirements *(mandatory)*

### Functional Requirements

**Reading variables by direct name**

- **FR-001**: Publish MUST resolve a direct variable name (`variables.X`, `variables['literal']`,
  `getVariable('literal')`) to the variable in the nearest enclosing declaring scope, resolving iteration scope,
  then enclosing container, then outer containers, then the workflow, with an inner declaration shadowing an outer
  one.
- **FR-002**: Publish MUST pin each resolved direct read as a spec 095 `Variable` declared parameter recording the
  declaring scope and variable key, without rewriting the authored source.
- **FR-003**: The evaluator MUST expose pinned variable reads under a `variables` root that is distinct from the
  `args` root, so a read of `variables.X` does not collide with an explicit `args.X` parameter.
- **FR-004**: At run time the script MUST see only the pinned declared values for variables; there MUST be no run-time
  variable name lookup and no frame-chain projection for scripts.
- **FR-005**: Liquid templates MUST read variables through the same `variables` root and the same nearest-scope
  resolution as JavaScript.
- **FR-006**: A declared variable with no value set MUST read JSON `null`, never `undefined`.
- **FR-007**: Loading MUST materialize only the variables a script actually names (not every visible variable).

**Computed names (deferred)**

- **FR-008**: Publish MUST refuse a computed variable read — `variables[expr]` or `getVariable(expr)` with a
  non-literal argument, or passing `variables` / `getVariable` as a value — with a stable diagnostic and guidance that
  points to the direct-name form and states that computed names are a deferred capability.
- **FR-009**: Publish MUST parse each script with the language's parser (JavaScript parser for JavaScript, Liquid's
  parser for Liquid), not regular expressions, to determine the read set and detect computed access.

**Scope of roots**

- **FR-010**: `input.X` and `workflow.correlationId` / `workflow.instanceId` MUST continue to be read as they are
  today (no new publish-time pin-by-name introduced by this spec).
- **FR-011**: Activity-output reads (`activities.<name>.<output>`) are OUT OF SCOPE for this spec and MUST be left to
  a dedicated follow-up spec; this spec MUST NOT introduce the `activities` script root.

**Transient and protected variables**

- **FR-012**: Publish MUST refuse a read of a transient (non-durable) variable, because its value cannot be
  snapshotted.
- **FR-013**: A direct read of a protected (secret) variable MUST be protection-checked at publish (the name is
  known).
- **FR-014**: Secret variable values MUST NOT appear in logs or diagnostics (redaction) at publish or run time.
- **FR-015**: A value the execution context has no permission to read MUST NOT be provided to the script (access
  control, independent of redaction).

**Rename safety**

- **FR-016**: Readable variable names MUST stay in the authored script source (names are not replaced by internal
  identifiers).
- **FR-017**: On rename, Studio MUST update the references it can resolve and MUST surface references it cannot safely
  update for manual repair, never silently breaking or retargeting a read.
- **FR-018**: Publish MUST refuse a name that no longer resolves to any visible declaration.
- **FR-019**: Publish MUST record a warning when a name now resolves to a different declaration than in the previously
  published version.
- **FR-020**: An already published workflow version MUST NOT change behaviour because of a draft rename.

**Converter (spec 247)**

- **FR-021**: The converter MUST emit `variables.X` for Elsa 3 variable reads (`Variables.X`, `getVariable("X")`,
  generated `get<Name>()`), spelled as the variable is declared, and MUST keep `args.X` for inputs, activity results
  and instance metadata.
- **FR-022**: The converter MUST refuse (faithful-or-refuse), with guidance, when a script's own declared `args`
  would collide with lowered `args` reads in the same scope (#3296).
- **FR-023**: The converter MUST give the `RunJavaScript` `arguments` expression its real result type instead of a
  hard-coded `Object`, so a converted script that reads a variable or input is not refused at publish for a type
  mismatch (#3287; the full fix pairs with #3167 in S-0).
- **FR-024**: The converter MUST emit statement bodies without the immediately-invoked-function wrapper for the
  `RunJavaScript` body path (#3010).
- **FR-025**: The converter MUST refuse a read when Elsa 4's nearest-scope resolution would pick a different
  declaration than Elsa 3's resolution (faithful-or-refuse).

**RunJavaScript body consistency**

- **FR-026**: `RunJavaScript` body reads MUST be described as reading through the body's variable session view
  (ADR 0092), which equals the pinned input snapshot until writes exist; writes are out of scope (ADR 0090 / S-7) and
  this spec MUST record the write assumptions it relies on for verification against S-7.

### Key Entities

- **Variable read**: a named read of a workflow variable in a script, carrying the resolved declaring scope and
  variable key once pinned at publish.
- **`variables` root**: the script-facing surface exposing pinned variable reads, distinct from the `args` root.
- **Read set**: the variables an expression reads, determined at publish by parsing the source (direct names only;
  computed access is refused).
- **Pinned `Variable` parameter**: a spec 095 declared parameter recording the declaring scope and variable key for
  one resolved read.
- **Protected (secret) variable**: a variable whose value is redacted from logs and whose read is protection-checked.
- **Transient variable**: a non-durable variable whose value is not persisted, and whose read is refused.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the tracked variable-read issues' reproduction cases (#3204, #3296, #3287 arguments part)
  publish and run correctly, or are refused with a clear diagnostic where faithful-or-refuse applies.
- **SC-002**: A converted Elsa 3 script that reads a variable, which previously failed to publish with an arguments
  type mismatch, now publishes.
- **SC-003**: Every computed-name construct is refused at publish with a diagnostic that names the construct and the
  direct-name alternative; none reaches run time.
- **SC-004**: No secret variable value appears in any log or diagnostic output in the read scenarios.
- **SC-005**: Renaming a variable referenced by a script in a draft never changes a published version's behaviour, and
  a script naming a removed variable is refused at publish 100% of the time.
- **SC-006**: Reading a variable by name materializes only the named variables, verified by a test that a large or
  external variable not named by the script is not loaded.

## Assumptions

- Elsa 4 is unreleased and has no production script data, so there is no backward-compatibility cost to the read model
  (ADR 0089 context).
- The `RunJavaScript` body's variable session (ADR 0092, PR #3411) is the read surface for bodies; until writes exist
  it equals the pinned input snapshot, so this spec needs no write mechanism.
- Writes from scripts (`setVariable`, captures) are governed by ADR 0090 / S-7 and are out of scope here; the write
  assumptions this spec relies on (writes commit at output evaluation; a computed write to a non-existent variable
  refuses) are recorded for verification against S-7.
- The runtime and publish parsers accept the same source; the trailing-line-comment parse bug is fixed by the #3171
  side-track on `main` and consumed here (publish must not reject source the runtime accepts).
- `#3167` (JSON alias inference) is delivered in S-0 and is a prerequisite for the full #3287 fix.

## Out of scope

- Computed / dynamic variable names (`variables[expr]`, `getVariable(expr)`) — refused here, deferred to a dedicated
  follow-up spec.
- The `activities.<name>.<output>` script root — deferred to its own spec (overlaps spec 265).
- New publish-time pin-by-name for `input.X` and `workflow.*` — they read as today.
- Writing variables from scripts — ADR 0090 / S-7.

## Findings (flow back to PR #3420 / epic #3052)

- **F-1**: Deferring computed reads collapses the #3420 amendment. With computed reads refused, the original ADR 0089
  (pin direct reads at publish; refuse computed — decision 5) stands, and the amendment's run-time accessor / run-time
  read recording for variables is no longer needed. Proposal: reduce #3420 to the original model plus the rename
  reconciliation and the body-session clarification; move computed-read support to a dedicated spec.
- **F-2**: The ADR's "run time sees only declared values" needs a carve-out for the `RunJavaScript` body, which reads
  through its variable session (ADR 0092), not a frozen snapshot, so later writes (ADR 0090 / S-7) stay consistent.
- **F-3**: The `activities.<name>.<output>` root in the amendment's A1 is new surface beyond this spec's issues and
  overlaps spec 265; it should be split into its own spec.

## Open questions

- **OQ-1**: The serialized shape marking a pinned `Variable` parameter as exposed under `variables` rather than `args`
  (a per-parameter surface flag vs a separate map), and how its name avoids colliding with authored `args` names. This
  changes spec 095's expression contract and is settled in the plan.
- **OQ-2**: How loop iteration values (ADR 0028) are exposed to the pinned read at run time (confirmed in the plan).
