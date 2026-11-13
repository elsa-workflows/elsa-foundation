# Feature Specification: AI-authored personal and shared dashboards

**Feature Branch**: `agent/codex/3604-ai-dashboards`

**Created**: 2026-11-06

**Status**: In progress — owner resumed #3604 on 6 November; one model-agnostic author over Weaver with the existing Copilot SDK adapter and backend options/configuration. No delivery acceptance yet.

**Input**: [Feature request #3604](https://github.com/elsa-workflows/elsa-foundation/issues/3604), activated by [owner authorization of 6 November 2026](https://github.com/elsa-workflows/elsa-foundation/issues/3604#issuecomment-6671863823).

The owner requests complete delivery through reviewed implementation, real provider and composed Studio qualification, merge and resulting-main verification. Independent specification, data/authorization, standards and UX-design reviews found no remaining native-design blockers after two correction rounds. Native implementation proceeds within that reviewed scope under the owner's delivery authorization; provider selection and configuration approach are now resolved by the owner; configured live access and source-specific provider/native product witnesses now pass. Final keyboard/editor/Home and resulting-main qualification remain open; see the exact-source evidence ledger. Program-goal state: owner-authorized AI dashboards #3604 (registry amendment of 9 November). No parent Initiative was present at intake. Existing [Weaver ownership](../../docs/program-goals/weaver-across-studio.md) is preserved.

## User Scenarios & Testing

### User Story 1 - Ask and save a new question (Priority: P1)

A person asks Weaver a question about data exposed by installed Elsa modules, clarifies its meaning, sees a proposed query and real result preview, adjusts filters or presentation, and explicitly saves a reusable question. The person can ask a question that has no prebuilt widget.

**Why this priority**: Newly composed questions are the feature's central value. Selecting an existing widget does not meet it.

**Independent Test**: Using real retained workflow runs with known outcomes, ask for workflows whose failure rate increased this week, select a clear comparison meaning, refine to group by current folder and exclude test runs, preview and save. Independently calculate the results and verify the shown metric interpretation and source coverage.

**Acceptance Scenarios**:

1. **Given** discoverable supported run data, **when** the person asks an ambiguous question, **then** Weaver requests the consequential missing meaning or shows an explicit interpretation for confirmation before a preview is treated as an answer.
2. **Given** a supported question, **when** preview runs, **then** the results derive from the authenticated viewer's authorized records and fields, with authoritative metric meaning, time basis, filters and coverage.
3. **Given** an inspected proposal, **when** the person saves, **then** the persisted question matches the validated query and visualization the person accepted; generated prose alone never indicates a successful save.
4. **Given** an unavailable metric or unexposed business field, **when** it is requested, **then** the person sees the limitation and a useful next step without an invented source, substituted metric or fabricated result.

### User Story 2 - Reuse and edit dashboards without AI (Priority: P1)

A person creates a named dashboard, adds saved questions, arranges and sizes its tiles, reopens it and refreshes its results. The person changes supported filters, parameters and visualization using ordinary controls when Weaver or its provider is unavailable.

**Why this priority**: The saved artifact must remain an ordinary durable product resource after the conversation ends.

**Independent Test**: Save a question, add it twice with independent tile presentation, arrange and reopen the dashboard, disable the provider, change a filter and refresh. Verify durable values and that the provider invocation count does not increase.

**Acceptance Scenarios**:

1. **Given** a saved question, **when** added to two dashboards or twice to one, **then** each tile has its own identity and layout while the question's reuse is visible.
2. **Given** a provider outage, **when** the person opens, edits, saves or refreshes an otherwise valid question/dashboard, **then** native controls for supported fields, filters, grouping, metrics/aggregation, sorting, time ranges/comparison, parameters and presentation remain usable and no model turn is required.
3. **Given** a concurrent editor, **when** an outdated revision is saved, **then** the person sees a conflict and can reload or save a copy without silent overwrite.
4. **Given** existing Home widgets, defaults and personal layout, **when** named dashboards are introduced, **then** those existing choices and unavailable contribution references survive unchanged.
5. **Given** a compatible preview, **when** the person selects each supported chart, table, list or metric presentation, **then** it renders with an accessible data alternative; incompatible choices explain what query shape is required.
6. **Given** a question reused on two dashboards, **when** it is updated, **then** both dashboards resolve its current saved revision after access checks, revalidate overrides and refresh explicitly; neither silently reuses results from the old definition.
7. **Given** an editor cannot inspect one dashboard question, **when** they move/resize another tile and save or copy the dashboard, **then** the hidden reference survives without exposing its identity or losing stored overrides.

### User Story 3 - Share definitions with independent viewer access (Priority: P1)

An owner shares a dashboard or saved question with selected authorized users or roles within the tenant. Owners control sharing and deletion; editors may change content; viewers may inspect and, when permitted, copy. A recipient's data always reflects that recipient's current effective authorization.

**Why this priority**: Sharing is a selected first-release capability with a direct confidentiality boundary.

**Independent Test**: Use two users with different run and field scopes, an editor, and a user in a different tenant. Share the same definitions, compare independently expected results and drill-down, revoke sharing and change data permissions, and verify no author results, counts, prompts or cached records leak.

**Acceptance Scenarios**:

1. **Given** a definition shared with a viewer, **when** it is read or refreshed, **then** definition access and source-data/field/aggregate access are checked separately and the author's access grants confer no data rights.
2. **Given** viewer/editor roles, **when** a recipient attempts save, delete or sharing changes, **then** the server enforces those capabilities; opening a shared dashboard grants no implicit edit or share rights.
3. **Given** revoked sharing, **when** any subsequent read, refresh, drill or export occurs, **then** current definition access is denied. Data-permission changes follow the identity provider's declared propagation boundary, which is shown and tested accurately.
4. **Given** a dashboard containing a question the recipient cannot access, **when** it opens, **then** the affected tile is unavailable without exposing the question, author preview or protected source details. Sharing offers an explicit review of included question access.
5. **Given** another tenant, **when** identifiers or audience entries are supplied directly, **then** no definition metadata, results, history or audience membership crosses the tenant boundary.
6. **Given** permission to copy a shared dashboard, **when** the person copies it, **then** a private dashboard owned by that person retains explicitly identified question references; the copy action explains those dependencies and offers separately permitted question-copy actions. Later question revocation makes that referenced tile unavailable, unless the person already made an independently authorized private question copy.

### User Story 4 - Understand and investigate the result (Priority: P1)

A person sees what a chart measures, its current filters, which period it covers, what data may be missing and when it last refreshed. Supported result rows or groups open only authorized underlying records. Export, when offered, follows the same current access and bounds.

**Independent Test**: Independently check numbers and a selected group against the underlying authorized records; exercise zero, no records, missing collection coverage, stale results, timeout, cancellation, removed schema and denied drill-down.

**Acceptance Scenarios**:

1. **Given** zero eligible records, **when** a ratio is requested, **then** the denominator rule is explicit and “no denominator” is distinguishable from an observed zero failure rate.
2. **Given** incomplete or unknown history, **when** a comparison is shown, **then** the person sees coverage limitations beside the metric and cannot mistake a retained sample for a complete historical claim.
3. **Given** an earlier successful result and a failed refresh, **when** stale content remains visible, **then** its last successful time and failed-refresh state are explicit. Identity, tenant or lost access clears protected content.
4. **Given** optional AI explanation, **when** shown, **then** it refers to actual result evidence and does not invent causes. Prior protected results are not redisclosed after loss of access merely because a new query can run.

### User Story 5 - Expose a business dimension through an independent module (Priority: P1)

An integrator deliberately exposes a typed business dataset, customer category and processing-time metric through the supported extension path. A person asks for processing time by customer category, saves it and drills into authorized processing records.

**Why this priority**: This proves extensibility and business value beyond workflow telemetry or a central dataset switch.

**Independent Test**: Install a separately packaged reference business module, create known processing records through its normal capability, discover its explicit fields and metric, author a novel question, and independently verify group values and authorization. The module uses the same contracts as the Workflow contribution and requires no new branch in generic orchestration.

**Acceptance Scenarios**:

1. **Given** the module is installed and the viewer is authorized, **when** discovery runs, **then** only declared available datasets, fields, dimensions, metrics and relationships are offered.
2. **Given** a workflow variable or business field was not deliberately exposed, **when** a question names it, **then** it remains unavailable regardless of what values happen to exist in runtime state.
3. **Given** module removal or an incompatible schema/metric change, **when** a saved question is opened or run, **then** it fails meaningfully with repair/copy actions and never switches sources or metric meanings silently.

### Edge Cases

- A partial week is compared with a full week; daylight-saving changes or tenant timezone affect period boundaries; identical local dates resolve to different instants.
- Failure means current outcome of runs started in a period rather than historical failure events; the UI must identify the distinction.
- A workflow moves folder after it ran; current-folder grouping differs from historical folder attribution.
- Legacy run kind is unknown; excluding explicit tests cannot promise every remaining run is production.
- Missing or inverted completion timestamps exclude duration eligibility; hidden records/fields cannot affect visible aggregate results or error detail.
- Dataset discovery fails versus returning no available datasets; duplicate dataset ownership is a configuration failure.
- Query bounds are exceeded, the browser navigates away, a second refresh supersedes the first, or a module unloads during execution.
- A question edit affects reused dashboard tiles; a dashboard is shared while contained questions have different audiences.
- An owner deletes a referenced question; history, copy and conflict recovery must not reveal inaccessible revisions.
- A model suggests unsupported operators, fields, executable code, fabricated interpretation or an unauthorized drill target.

## Requirements

### Functional Requirements

- **FR-001**: Discover authorized module-owned datasets with typed fields, explicit business dimensions, authoritative metrics/units/eligibility/denominator rules, supported query operations, time semantics, coverage capabilities, declared relationships and version identities.
- **FR-002**: Support a bounded validated question definition with typed filters and parameters, grouping, supported aggregation/metrics, sorting, absolute/relative time windows, comparable periods and supported post-aggregation/comparison predicates such as increased failure rate. Reject unsupported operations and undeclared relationships; never execute model-generated SQL, scripts or arbitrary expressions.
- **FR-003**: Compose genuinely new supported questions beyond the prebuilt widget catalog. Preserve dataset and metric ownership with contributing modules.
- **FR-004**: Use Weaver's resource-agnostic extension path for ask, clarification, refinement and proposals. Dashboard/module owners supply context, tools, schemas, result rendering and apply semantics. Do not create a competing conversation engine or central dataset-specific assistant branches.
- **FR-005**: Preview actual authorized results and a human-readable interpretation before explicit save. Authoritative query facts remain distinct from optional generated explanation.
- **FR-006**: Persist named saved questions with query, visualization, typed parameters, owner, tenant, concurrency revision, bounded definition history and source/schema/metric references. History contains definitions and audit metadata, not reusable privileged result sets or shared conversation logs.
- **FR-007**: Persist named dashboards with independently identified ordered/sized tiles, question references and presentation/parameter overrides. References follow the current saved question revision after current access checks; each execution binds one exact question revision. Show reuse before updating a question, offer Save a copy, and mark visible tiles outdated until refreshed after a definition change. Revalidate tile overrides; incompatible overrides require repair rather than silent parameter/presentation substitution. Historical restore creates a new revision and revalidates current source compatibility and access.
- **FR-008**: Provide ordinary editing of supported fields, filters, grouping, metrics/aggregation, sorting, time ranges/comparison, parameters and presentation independently of provider availability. A refresh executes the saved query without a model invocation; compatible saved definitions remain usable if Weaver is disabled. Support configurable bar/line charts, tables, lists and single-value metrics, with result-compatible choices and accessible data alternatives.
- **FR-009**: Support personal and optionally shared question/dashboard definitions, selected tenant users/roles, owner/editor/viewer capabilities and explicit copy rights. Content editing never grants ownership or audience-management authority. A copied dashboard is private and retains clearly identified question references; independently copying accessible questions is a separate authorized action, never an implicit result of copying a dashboard.
- **FR-010**: Evaluate current definition access and the authenticated viewer's tenant/record/field/aggregate access on every discovery, preview, save, read, refresh, history, drill and supported export. Apply source authorization before computing counts, aggregates, order or pagination.
- **FR-011**: Sharing transfers no elevated data access, author result cache or protected conversation evidence. Sharing a dashboard does not implicitly widen a referenced question's audience. Prevent cross-tenant audiences and existence leakage.
- **FR-012**: Respect current identity-provider permission propagation. Explicit dashboard ACL changes apply to the next operation; evidence must distinguish live IAM reevaluation from token-refresh-bound data grants.
- **FR-013**: Validate source availability and exact compatible schema/metric meaning on each execution and save. Data-module removal, duplicate ownership, missing fields or incompatible metric revisions produce actionable states without silent substitution. AI-provider removal affects conversational authoring availability only; it does not invalidate a compatible saved query, ordinary save or refresh.
- **FR-014**: Bound query shape, filters, source traversal, rows, groups, execution time and concurrent refreshes. Propagate cancellation. Never present a truncated sample as a complete aggregate.
- **FR-015**: Distinguish availability, coverage and refresh outcome, including observed zero, no matching records, no eligible denominator, unavailable, incomplete/unknown coverage, stale, failed and cancelled. Show last successful refresh, effective windows and authoritative metric meaning.
- **FR-016**: Support authorized drill-down using the same question, parameters, group constraints and source meaning. Only offer exports that enforce the same current authorization, scope and cost bounds.
- **FR-017**: Use no shared privileged result cache. Any cache must be partitioned and invalidated for viewer, tenant, definition/query/schema and authorization changes. Clear protected client results on access/scope changes; do not silently fall back to local persistence for shared definitions.
- **FR-018**: Re-admit retained context/results before provider disclosure. If current authority for retained evidence cannot be established, withhold it and require fresh evidence rather than redisclosing previously broader aggregates.
- **FR-019**: Preserve existing Home contributed widgets, feature/permission gates, saved preferences, hidden/missing contribution references and administrator defaults.
- **FR-020**: Demonstrate a real workflow dataset and a deliberately exposed independent business dataset/dimension through the same public extension path, with independently checked numerical results.
- **FR-021**: Integrate the existing Copilot SDK adapter through backend options/configuration (appsettings/environment) and Weaver ownership, as selected by the owner on 6 November. Preserve credential secrecy, configured provider availability and lifecycle. Record actual supported provider behavior separately from deterministic fixtures.
- **FR-022**: Provide practical user and integrator documentation covering authoring, interpretation, sharing, access, extension contracts, source coverage, limits and recovery.
- **FR-023**: Qualify the actual composed Studio ask → clarify/refine → preview → save → arrange → reopen → refresh → share → drill-down journey; retain current-commit downloadable screenshots/traces and obtain independent UX acceptance under the existing review loop.

### Key Entities

- **Dataset declaration**: A module-owned discoverable source with explicit fields, business dimensions, metrics, time/coverage semantics, supported operations and version identity.
- **Question definition**: A named owned/shared reusable query and visualization with typed parameters and revision history; it does not confer source-data access.
- **Dashboard definition**: A named owned/shared ordered composition of tiles and their layout.
- **Dashboard tile**: An independently identified question reference with supported parameter and presentation overrides.
- **Audience grant**: A tenant-scoped user/role reference with definition capabilities; separate from data permissions.
- **Query result**: Bounded viewer-scoped observed values with authoritative interpretation, availability, coverage, refresh provenance and authorized drill options.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A real-provider composed journey completes all ten authoring/reuse/sharing/drill steps in FR-023 with the resulting saved definitions surviving reopening and host restart.
- **SC-002**: At least one saved workflow question absent from the prebuilt catalog and one independent business-dimension question match independently calculated results, including period comparison and grouped duration.
- **SC-003**: After disabling the provider, ordinary edit/save/reopen/refresh remains successful and the measured provider-call count increases by zero.
- **SC-004**: Different authorized viewers obtain their independently expected records and aggregates; cross-tenant, revoked audience, unauthorized field/aggregate/history/drill/export and protected cache cases disclose zero forbidden values or metadata.
- **SC-005**: All named edge-state journeys retain their distinct meanings, including unknown coverage, no denominator, zero, empty, unavailable, stale, failed, cancelled and incompatible source changes.
- **SC-006**: Existing Home journeys remain successful, and an independently packaged module contributes its dataset and rendering/drill behavior without generic orchestration changes.
- **SC-007**: Current-head required checks and independent correctness/standards/spec/UX reviews pass, implementation merges under current repository rules, and resulting-main qualification verifies the delivered feature. Missing/skipped/unavailable evidence remains open rather than counted as success.

## Assumptions

- Owner-confirmed navigation, 6 November 2026: a separate Dashboards area with Mine and Shared with me, alongside unchanged Home. Existing widgets, preferences and administrator defaults remain with Home.
- First-profile recommendations to review: one module-owned dataset per question; declared module-owned enrichments only; current-folder grouping; started-cohort current-status failure rates; explicit comparison of equal elapsed durations from local-week starts, refusing DST overflow rather than silently clipping; role/user audiences within one tenant; owners alone manage sharing/delete, editors change content, authorized viewers may copy definitions.
- Current retained runtime records do not prove complete collection history. Unknown coverage remains explicit until the owning source can attest a range.
- No server result cache is proposed initially. Fresh viewer-scoped execution is the default; this reduces an otherwise substantial permission-revocation boundary.
- The owner selected the existing Copilot SDK adapter with backend options/configuration. The configured credential/entitlement passed explicit live qualification on owned disposable hosts; credentials remain local and are absent from retained evidence. No OpenAI API implementation is selected.
- The referenced Home/Weaver decisions and module/domain ownership rules remain authoritative. This feature does not ratify draft constitution sections, amend unrelated execution semantics, resume paused streams or depend on unmerged Weaver M4 effects.
- Custom widget code generation, external database/business-system queries, runtime-mutating actions, general BI scope and proactive monitoring remain separate follow-ups. No deployment or package release is authorized.
