# Feature Specification: Expression Code Intelligence

**Feature Branch**: `codex/expression-code-intelligence`

**Created**: 2026-07-28

**Status**: Implemented

**Input**: User description: "Provide JavaScript and Liquid expressions with syntax highlighting, context-aware code intelligence, diagnostics, and a consistent rich editing experience in compact single-line property fields and expanded editors."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author expressions in place (Priority: P1)

A workflow author can switch an activity property to JavaScript or Liquid and edit the expression directly in the compact property row with syntax highlighting and familiar code-editing behavior. The author can expand the same expression for multiline work without losing text, selection, or undo history.

**Why this priority**: Expression authoring begins in the property row. A rich expanded editor alone still leaves the most frequent editing path as a generic text field.

**Independent Test**: Open a workflow containing JavaScript and Liquid-capable properties, edit single-line expressions from the property rows, expand and collapse them, and verify that source and editor state remain continuous. This surface is the prerequisite used by the later capability slices.

**Acceptance Scenarios**:

1. **Given** a property using JavaScript or Liquid with a single-line value, **When** the author focuses its compact editor, **Then** rich editing activates with syntax highlighting without changing the value.
2. **Given** an active compact editor, **When** the author presses Enter with no completion selected, **Then** the expanded editor opens on the same expression and editor session.
3. **Given** an active completion, **When** the author presses Enter, **Then** the completion is accepted without opening the expanded editor.
4. **Given** a multiline expression, **When** it appears in the compact property row, **Then** the source remains unchanged and a keyboard- and pointer-operable compact preview with an accessible name opens the expanded session.
5. **Given** any value, including empty, invalid, or multiline source, **When** the author switches between text-based Expression Types, **Then** the exact value is carried over and reinterpreted without translation.
6. **Given** several visible expression fields, **When** none is focused, **Then** none behaves as an active code editor; focusing one field activates only that field.
7. **Given** either compact or expanded editing, **When** the author presses Tab or Shift+Tab, **Then** the selection is indented or outdented.
8. **Given** either editor profile, **When** the author presses Escape followed by Tab or toggles Tab-focus mode, **Then** keyboard focus can leave the editor and the available escape instructions are discoverable.

---

### User Story 2 - Discover valid language constructs (Priority: P2)

A workflow author receives completions, member suggestions, signatures, and documentation that match the selected Expression Type, the current workflow location, the installed backend modules, and the author's permissions.

**Why this priority**: Syntax coloring improves readability, but context-aware discovery is what prevents authors from guessing how workflow data and expression-specific capabilities are accessed.

**Independent Test**: Using the prerequisite compact and expanded surfaces from Story 1, invoke automatic and explicit completion for built-in symbols and visible workflow data, navigate nested members, and verify that unavailable or unauthorized symbols never appear.

**Acceptance Scenarios**:

1. **Given** a JavaScript expression, **When** the author types an identifier or member-access character, **Then** matching JavaScript symbols and visible workflow facts appear automatically.
2. **Given** a Liquid expression, **When** the author enters a variable, property, filter, or tag position, **Then** suggestions follow Liquid syntax and the capabilities of Elsa's installed Liquid module.
3. **Given** either compact or expanded editing, **When** the author invokes completion explicitly, **Then** the same context-aware suggestions appear.
4. **Given** a structured workflow value, **When** the author selects successive members, **Then** nested suggestions follow its language-neutral value shape.
5. **Given** an expected activity-property result type, **When** suggestions are ranked, **Then** compatible symbols rank higher without hiding other available symbols.
6. **Given** a symbol with documentation or callable signatures, **When** the author requests details, **Then** sanitized documentation and signature help are shown.
7. **Given** a symbol or member disallowed by permissions or Host Policy, **When** code intelligence is requested, **Then** its metadata is not delivered to or displayed by Studio.
8. **Given** a runtime or module upgrade, **When** its symbol capabilities change, **Then** Studio uses the current compatible catalog and does not retain an obsolete capability view.

---

### User Story 3 - Correct expressions before consequential actions (Priority: P3)

A workflow author receives immediate syntax feedback while typing and authoritative semantic feedback from the backend module that will execute the selected Expression Type. Known errors remain editable but cannot pass consequential workflow actions.

**Why this priority**: Feedback must be responsive and must also agree with the configured runtime. Either local-only or backend-only validation leaves a significant correctness gap.

**Independent Test**: Using the prerequisite authoring surfaces and code intelligence, introduce syntax and semantic errors in JavaScript and Liquid, observe compact and expanded diagnostics, repair the expression, and exercise draft save, Test Run, and publication boundaries.

**Acceptance Scenarios**:

1. **Given** an incomplete expression, **When** the author types, **Then** local syntax feedback appears without waiting for a backend round trip.
2. **Given** a syntactically valid but semantically invalid expression, **When** the author pauses or leaves the field, **Then** authoritative diagnostics from the owning backend Expression Module appear.
3. **Given** rapid edits or a changed workflow scope, **When** an older validation response arrives, **Then** it is discarded instead of replacing current diagnostics.
4. **Given** multiple diagnostics, **When** the expression is compact, **Then** markers and the highest-priority message are shown; the expanded editor shows the complete list.
5. **Given** known semantic errors, **When** the author edits or saves the draft, **Then** the operation remains available.
6. **Given** known semantic errors, **When** the author requests a Test Run or publication, **Then** the consequential action is blocked with actionable diagnostics.
7. **Given** authoritative validation is temporarily unavailable, **When** the author requests a Test Run, **Then** the author may proceed after an explicit warning.
8. **Given** authoritative validation is temporarily unavailable, **When** the author requests publication, **Then** publication fails closed with an explanation.
9. **Given** a workflow-wide consequential check, **When** validation runs, **Then** every expression in the draft is validated against its current context and Expression Type.

---

### User Story 4 - Continue authoring safely when tooling changes (Priority: P4)

A workflow author can continue viewing and editing source when language tooling is unavailable, receives an accurate explanation of the degraded state, and never has source exposed or destroyed by fallback behavior.

**Why this priority**: Rich tooling is module-dependent and network-dependent, but workflow source is durable user data and must not become inaccessible when an enhancement fails.

**Independent Test**: Against the prerequisite authoring and tooling slices, disable or fail symbol and validation services, change module capability versions, exercise unauthorized sensitive source, and verify graceful, accessible fallback on desktop and touch browsers.

**Acceptance Scenarios**:

1. **Given** syntax support is available but semantic tooling fails, **When** the editor opens, **Then** source editing and local diagnostics remain available and the missing capabilities are explained.
2. **Given** an Expression Module becomes unavailable, **When** an existing expression is opened, **Then** its exact source remains visible and editable through a generic text fallback.
3. **Given** a user is not authorized to reveal sensitive expression source, **When** the property is shown, **Then** only a protected placeholder is delivered and no hidden editor contains the source.
4. **Given** tooling reports no symbols, **When** Studio receives the response, **Then** it distinguishes a valid empty catalog from unavailable, unauthorized, and incompatible states.
5. **Given** a keyboard-only or screen-reader user, **When** they edit, complete, inspect, diagnose, expand, or leave an expression, **Then** every action remains discoverable and operable.
6. **Given** a supported touch browser, **When** the author edits an expression, **Then** highlighting, completion, diagnostics, selection, and expansion remain functional.

### Edge Cases

- An expanded multiline expression is displayed in a one-line property row.
- Two activities have properties with the same descriptor name and Expression Type.
- The author switches Expression Types while the value is empty, invalid, or multiline.
- A workflow variable, input, output, or scope changes while a completion or validation request is in flight.
- A value shape is recursive, very large, or shared by many symbols.
- A symbol catalog is empty, too large for one response, unauthorized, unavailable, or incompatible with the client.
- A completion provider or semantic validator responds after its document, context, or catalog version has changed.
- A module advertises only some tooling capabilities.
- A user loses permission after a catalog or value shape was loaded.
- A sensitive property is visible to one user but not another.
- An expression module is disabled while a workflow containing that Expression Type remains open.
- Completion, hover, or diagnostics would otherwise overflow a narrow inspector.
- The author pastes a newline into a compact single-line editor.
- The editor is cold-loaded on a slow connection or a panel contains many expression-capable fields.
- Documentation supplied by a module contains unsafe markup or links.
- Test Run validation is unavailable while publication validation must fail closed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Studio MUST provide rich compact and expanded Expression Editor Surfaces for JavaScript and Liquid.
- **FR-002**: Compact and expanded surfaces for one expression MUST share source, selection, undo history, diagnostics, and language-tooling state.
- **FR-003**: Each Expression Document MUST have a stable identity scoped by workflow draft, activity, property, and Expression Type.
- **FR-004**: Only a focused compact field MUST activate rich editing; unfocused compact fields MUST remain lightweight.
- **FR-005**: A compact surface MUST remain one visual line and MUST NOT flatten, translate, or otherwise mutate multiline source.
- **FR-006**: Multiline source MUST be edited through the expanded surface while remaining faithfully previewed in compact form.
- **FR-006a**: A compact multiline preview MUST provide a keyboard- and pointer-operable control with a discoverable accessible name that opens the expanded session.
- **FR-006b**: When insertion or paste introduces a newline in compact editing, Studio MUST preserve the exact source and transition that Expression Document to expanded editing.
- **FR-007**: Changing between text-based Expression Types MUST carry the exact current value, including an empty value, without automatic translation.
- **FR-008**: Pressing Enter in compact editing MUST accept an active completion; otherwise it MUST open the expanded surface.
- **FR-009**: Tab and Shift+Tab MUST indent and outdent in compact editing, matching the coding behavior of the expanded editor.
- **FR-010**: Tab and Shift+Tab MUST indent and outdent in expanded editing.
- **FR-011**: Both editor profiles MUST provide and announce keyboard mechanisms for leaving an editor that captures Tab.
- **FR-012**: Escape MUST dismiss active completion or hover UI before leaving compact editing and MUST NOT revert already-applied draft changes.
- **FR-013**: Expression changes MUST use the workflow draft's existing autosave, undo, and concurrency behavior; tooling MUST NOT create a separate save path.
- **FR-014**: Studio MUST supply a language-neutral Expression Authoring Context containing the target property, expected result shape, and visible workflow inputs, variables, and activity outputs.
- **FR-014a**: Expression Authoring Contexts MUST contain design-time metadata only; Studio and Expression Modules MUST NOT evaluate expressions, retrieve live runtime values, or disclose values to construct them.
- **FR-015**: Each Expression Module MUST project the Expression Authoring Context into symbols appropriate for its Expression Type.
- **FR-016**: The owning backend Expression Module MUST be authoritative for the built-in Expression Symbol Catalog and semantic validation of its Expression Type.
- **FR-017**: Symbol catalogs MUST contain design-time metadata only and MUST NOT evaluate expressions or retrieve live runtime values.
- **FR-018**: Symbol catalogs and referenced value shapes MUST be filtered by the current user's permissions and Host Policy before Studio receives them.
- **FR-019**: Expression Symbols MUST support names, kinds, documentation, callable signatures, type references, and hierarchical member completion.
- **FR-020**: Expression Value Shapes MUST describe scalar, nullable, collection, object-member, and callable shapes without binding consumers to one expression language.
- **FR-021**: Recursive or complex value shapes MUST be referenceable and resolvable without embedding an unbounded object graph in every authoring context.
- **FR-022**: Large or dynamic symbol sets MUST be bounded and searchable without requiring Studio to download an unbounded catalog; search requests MUST be cancellable, versioned, and discarded when their query, document, or context is stale.
- **FR-023**: Symbol catalogs and value-shape contracts MUST be versioned and capability-negotiated.
- **FR-024**: Before editor activation, each Expression Type descriptor MUST advertise a supported or unsupported value for highlighting, completion, hover, signatures, formatting, local diagnostics, and semantic validation.
- **FR-025**: Completion MUST activate automatically in relevant language positions and explicitly through Ctrl+Space or Cmd+Space.
- **FR-026**: The expected result shape MUST influence completion ranking and diagnostics without hiding otherwise available symbols.
- **FR-027**: Compact and expanded surfaces MUST share the same underlying highlighting, completion, hover, signature, and diagnostic tooling and diagnostic set; presentation MAY differ only as specified for compact diagnostics.
- **FR-028**: JavaScript tooling MUST understand JavaScript syntax and Elsa-provided JavaScript symbols without requiring full project-wide type analysis in the initial release.
- **FR-029**: Liquid tooling MUST understand Liquid syntax while treating Elsa's backend Liquid module—not an external Liquid dialect—as authoritative for supported tags, filters, symbols, and semantics.
- **FR-030**: Formatting MUST be explicit, MUST NOT run automatically on typing, blur, expansion, or autosave, and MUST be offered only when the Expression Type advertises it.
- **FR-031**: Module-supplied documentation MUST render as sanitized Markdown with arbitrary HTML and executable links disabled.
- **FR-032**: Editors MUST provide immediate local syntax diagnostics.
- **FR-033**: Editors MUST request authoritative semantic diagnostics asynchronously after an idle interval and when focus leaves the expression.
- **FR-034**: Interactive semantic validation MUST be cancellable and MUST NOT block typing.
- **FR-035**: Validation and completion responses MUST be associated with the current Expression Document, authoring-context, and catalog versions; stale responses MUST be discarded.
- **FR-036**: Changes to workflow inputs, variables, activity outputs, or scope MUST invalidate affected authoring contexts and semantic diagnostics.
- **FR-037**: Compact surfaces MUST show diagnostic markers and the highest-priority message; expanded surfaces MUST make the complete diagnostic set available.
- **FR-038**: Semantic errors MUST NOT block editing or draft persistence.
- **FR-039**: Known error-level syntax or semantic diagnostics MUST block Test Run and publication.
- **FR-040**: A Test Run MAY proceed with an explicit warning when authoritative validation is unavailable only when no known blocking syntax or semantic error exists.
- **FR-041**: Publication MUST fail closed when authoritative expression validation is unavailable.
- **FR-042**: Interactive validation MUST target the current Expression Document; Test Run and publication MUST validate the full workflow draft.
- **FR-043**: When semantic tooling is unavailable, source editing, syntax highlighting, and local diagnostics MUST remain available.
- **FR-044**: When an Expression Module is unavailable, Studio MUST preserve and expose existing source through a generic editor and MUST NOT clear or convert it.
- **FR-045**: Tooling state MUST distinguish supported-empty, unavailable, unauthorized, and incompatible outcomes.
- **FR-046**: Sensitive expression source MUST be delivered to the editor only when the current user is authorized to reveal it.
- **FR-047**: Telemetry MUST be limited to aggregate usage, latency, availability, and non-sensitive error codes and MUST NOT record expression source, completion prefixes, symbol names, or diagnostic messages.
- **FR-048**: Rich expression editing MUST remain usable on supported keyboard, screen-reader, pointer, and touch/mobile environments.
- **FR-049**: Completion, hover, diagnostic, and keyboard-escape interactions MUST meet WCAG 2.2 AA.
- **FR-050**: Rich tooling MUST be independently enableable per Expression Type under Host Policy, with generic editing retained as fallback.
- **FR-051**: The first release MUST provide complete rich tooling for JavaScript and Liquid and MUST NOT advertise placeholder support for future Expression Types.
- **FR-052**: Language tooling implementations MAY use different internal protocols or services, but Studio MUST consume one stable Expression Module tooling contract.
- **FR-053**: Expression tooling metadata cached in the client MUST be memory-only, MUST NOT survive the browser session, and MUST be isolated and reused by user, tenant, backend, Expression Type, backend module version, permission revision, and Host Policy revision.
- **FR-054**: A shared conformance suite MUST verify every enabled Expression Module against symbol, shape, completion, diagnostics, degradation, permission, and surface-parity requirements.
- **FR-055**: Field-specific suggestions MUST combine reusable Expression Type catalog metadata with the current Expression Authoring Context without refetching the entire catalog per field.
- **FR-056**: Permission or Host Policy revision changes MUST invalidate affected catalog and value-shape caches, reauthorize active requests, and remove source or metadata from active views when the user is no longer authorized.
- **FR-057**: The changed inspector workflows MUST use shared Studio primitives and the public `--studio-*` design-token contract.
- **FR-058**: Verification MUST include the real activity-properties inspector in compact, expanded, loading, unavailable, unauthorized, invalid, and multiline states.

### Key Entities

- **Expression Document**: One editable Expression Type value applied to one activity property in one workflow draft, with stable identity and editor-session state.
- **Expression Authoring Context**: Language-neutral design-time facts visible at an Expression Document's workflow location.
- **Expression Symbol**: A named value or callable available to one Expression Type, including documentation, signatures, and a reference to its value shape.
- **Expression Symbol Catalog**: A versioned, permission-scoped backend description of built-in symbols for one Expression Type.
- **Expression Value Shape**: A language-neutral description of a symbol's scalar, collection, object-member, nullable, or callable structure.
- **Expression Diagnostic**: A versioned syntax or semantic finding tied to an Expression Document and source range.
- **Expression Tooling Capability**: An advertised feature supported by one Expression Type, such as completion, hover, formatting, or semantic validation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Authors can edit, complete, expand, collapse, and diagnose the same JavaScript or Liquid expression without any source, selection, or undo-history loss.
- **SC-002**: At the 95th percentile over 30 activations in the reference benchmark, a focused compact editor becomes interactive within 100 ms when its engine and catalog are memory-cached and within 500 ms after a fresh page load with no editor engine or catalog cache.
- **SC-003**: At the 95th percentile over 100 keystrokes in the reference benchmark's 50-field inspector fixture (10 rich-capable fields, 40 ordinary fields, expressions up to 2,000 characters), expression typing produces no main-thread task longer than 50 ms.
- **SC-004**: In conformance fixtures, 100% of authorized built-in and workflow-context symbols are discoverable in both compact and expanded surfaces, and 0 unauthorized symbols are delivered.
- **SC-005**: In stale-response tests, 100% of completions and diagnostics produced for obsolete document, context, or catalog versions are discarded.
- **SC-006**: JavaScript and Liquid both pass the shared Expression Module conformance suite, and every capability each module advertises produces equivalent results from compact and expanded surfaces.
- **SC-007**: All keyboard-only and screen-reader acceptance scenarios can be completed without a keyboard trap and with announced completion, diagnostic, and escape behavior.
- **SC-008**: When semantic tooling or an Expression Module is unavailable, 100% of existing expression source remains viewable and editable by authorized users without conversion or loss.
- **SC-009**: Known semantic errors block 100% of Test Run and publication attempts, while draft editing and saving remain available.
- **SC-010**: Publication is rejected in 100% of validation-unavailable scenarios; Test Run presents an explicit warning and remains available.
- **SC-011**: Automated security tests find no expression source, completion prefix, symbol name, or diagnostic message in emitted telemetry.
- **SC-012**: The feature passes the supported desktop and touch-browser matrix with no critical or serious automated accessibility violations and documented keyboard and screen-reader acceptance runs for focus, completion, hover/signature help, diagnostics, compact-to-expanded transition, Tab-capture escape, and unavailable states.

## Assumptions

- Existing workflow draft autosave, undo, concurrency, permission, Host Policy, and Test Run/publication mechanisms remain authoritative.
- JavaScript and Liquid backend modules can expose design-time metadata and validation without evaluating authored expressions.
- Workflow scope analysis can provide visible inputs, variables, and activity outputs without language-specific projection.
- Expression source is durable workflow data; editor sessions and cached tooling metadata are transient.
- Rich tooling can be rolled out independently per Expression Type while retaining the current generic editor.
- Full project-wide JavaScript type analysis, cross-expression rename, references, and navigation are outside the initial release.
- Python and other future Expression Types are outside the initial release but must be able to adopt the stable contracts without modifying workflow-specific scope discovery.
- External module documentation is untrusted presentation content and is sanitized before display.
- The reference performance benchmark runs in the repository's browser test harness using current stable Chromium at 4× CPU throttling on the project CI runner; results record the browser, runner class, fixture revision, samples, and percentile so regressions are reproducible.
- Accessibility acceptance evidence uses the supported browser matrix and at least Chromium with a desktop screen reader plus Safari with VoiceOver; automated checks supplement but do not replace the documented manual flows.
