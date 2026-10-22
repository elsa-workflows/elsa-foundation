# Feature Specification: Incident investigation handoff

**Feature Branch**: `agent/codex/3067-incident-handoff`

**Created**: 2026-10-22

**Status**: Implemented

**Input**: [#3067](https://github.com/elsa-workflows/elsa-foundation/issues/3067), Studio UX epic #3057 / Elsa 4 Simplification #2559. Explain the selected incident and supported next step after investigation without inventing a recovery action.

## User Scenarios & Testing

### User Story 1 - Understand what to do next (Priority: P1)

An operator investigating an incident sees a short next-step explanation grounded in that incident and the current run, rather than having to infer recovery from technical details or a missing button.

**Why this priority**: Inspection alone leaves the everyday incident-triage journey without a safe handoff.

**Independent Test**: Select active blocking, active nonblocking and historical incidents, including unavailable evidence, and verify the explanation changes with the selected incident and current run.

**Acceptance Scenarios**:

1. **Given** an active blocking incident, **When** the operator investigates it, **Then** the explanation identifies the need for intervention without promising that an unsupported action will work.
2. **Given** an active nonblocking or historical incident, **When** it is selected, **Then** the explanation distinguishes current attention from history and never treats health as the run's lifecycle status.
3. **Given** an automatic retry is scheduled, **When** the relevant recovery evidence is available, **Then** the explanation identifies the scheduled attempt and recorded due time without offering a duplicate operator action.
4. **Given** evidence is still loading, partial, absent or denied, **When** the incident is inspected, **Then** the explanation states the limit without claiming the relevant state or handler does not exist.
5. **Given** the selected incident or current run changes, **When** the view updates, **Then** the explanation stays bound to the current exact target rather than another incident's earlier state.

### User Story 2 - Take a permitted handoff (Priority: P1)

An operator can use an existing supported recovery control or pass the investigation to the appropriate author/operator, with the limits of definition editing made clear.

**Why this priority**: A clear next step must preserve authority and the effect of existing controls.

**Independent Test**: Exercise supported, unsupported, unknown and denied recovery states, read-only/design-authority cases and a pending action without accidental mutation.

**Acceptance Scenarios**:

1. **Given** a supported action is allowed for this selected incident, **When** the operator reviews it, **Then** the existing canonical control retains its exact target, consequence, confirmation and authorization behavior.
2. **Given** no applicable action is available, **When** the incident is inspected, **Then** the user receives a useful inspection/share/operator handoff and no ineffective Retry.
3. **Given** capability discovery is loading or failed, **When** no action appears, **Then** copy does not misstate that as proven unsupported functionality or proven lack of permission.
4. **Given** the caller has only runtime inspection authority, **When** handoff options are shown, **Then** the view does not promise unauthorized design editing or publishing.
5. **Given** definition inspection/authoring navigation is permitted, **When** it is offered, **Then** the exact definition identity is retained and copy explains that changes affect future executions, not this run's pinned executable.
6. **Given** an exact source is still Scheduled/non-Faulted, a recorded parent is not Running, or the run/incident has ended, **When** handoff is shown, **Then** it does not offer a Retry the existing runtime preflight rejects. Missing source/parent evidence remains unknown and prompts inspection rather than a promise of applicability.

### User Story 3 - Keep evidence and navigation intact (Priority: P2)

The operator can still reach the affected activity/input and return to the original Runs view, with readable, keyboard-accessible guidance in the existing visual language.

**Why this priority**: Guidance must complete the investigation path without losing its evidence or context.

**Independent Test**: Follow a real blocked run from a filtered/paged list through the selected incident and exact activity/input and back; inspect both laptop themes and narrow keyboard use.

**Acceptance Scenarios**:

1. **Given** an exact activity/input association exists, **When** the operator follows it, **Then** the existing exact occurrence/input focus and original list query/page are preserved.
2. **Given** association or input values are unavailable or withheld, **When** evidence is inspected, **Then** the existing honest limits remain; no other occurrence or value is silently substituted.
3. **Given** a long incident and keyboard/narrow use, **When** guidance and technical details are inspected, **Then** the primary next step remains readable, advanced detail remains discoverable, and modal focus/Escape/return behavior is preserved.

### Edge Cases

- A run's management permission does not mean the selected incident has an applicable action.
- A pending/followed action or canceled requester can suppress otherwise supported canonical controls.
- Missing server-projected permission information is not proof of a particular denied grant.
- Partial activity paging or an unloaded compensation journal is not proof that no recovery state exists.
- A scheduled attempt whose due time has passed does not authorize a duplicate manual retry.
- Runtime management, activity-value inspection and workflow-design authority are distinct.
- Broad wildcard/subtree/manage-to-read grants and auth-disabled operation must not be mistaken for denial from an absent exact client key.
- Historical/resolved/suppressed incidents and an ended run must not acquire a new mutation path through guidance.

## Requirements

### Functional Requirements

- **FR-001**: Investigation MUST show a concise next-step explanation for the selected incident in the context of the current run.
- **FR-002**: Active blocking, active nonblocking, history and unavailable/no-evidence outcomes MUST remain distinct without conflating incident health and run lifecycle.
- **FR-003**: Scheduled automatic retry and incomplete recovery evidence MUST be represented honestly; guidance MUST NOT offer a duplicate or invented action.
  When an exact compensation requester is known but its journal cannot confirm retry scheduling, a read-only availability check MUST remain usable without opening Retry confirmation. Only a successful matching current-context/run/requester observation with valid consumed nested journal structure may establish that schedule evidence; failed or malformed reads remain unknown.
- **FR-004**: Existing supported recovery controls MUST retain their target, consequences, confirmation, authority, pending-state and cancellation guards. No new recovery command or generic Retry is introduced.
  The UI MUST suppress locally known ineligible Retry and historical/terminal action controls using the existing runtime preflight contract. Retry review requires the exact loaded Faulted source and, when a parent identity is recorded, a confirmed Running parent. A known active blocking incident on an exact loaded descendant MUST suppress source Retry, matching the existing preflight refusal. Unknown or partial evidence MUST NOT be promoted to confirmed applicability or proof of an absent descendant. Dynamic concurrency, final authorization and execution remain server-owned; canonical payload/target mapping is unchanged.
- **FR-005**: An absent action MUST NOT by itself be described as unsupported functionality or denied permission; guidance MUST preserve known versus unknown availability.
- **FR-006**: When recovery is unavailable, the view MUST provide a supported inspection/share/operator handoff, with definition navigation only where appropriate authority is established.
- **FR-007**: Definition handoff MUST retain exact identity and explain that editing/publishing affects future executions rather than replacing the executable pinned to this run.
- **FR-008**: Existing exact activity/input association, protected/unavailable value states and list return context MUST remain intact. Navigation alone MUST NOT be represented as proof of resolved input values.
- **FR-009**: Read-only investigation/navigation MUST perform zero run/recovery/design mutations; existing explicit confirmed operator actions remain unchanged.
- **FR-010**: Guidance MUST follow existing Studio tokens, disclosure and focus conventions, remain readable at both laptop sizes in light/dark and reflow for narrow/zoom and keyboard use.
- **FR-011**: Guidance MUST update for the currently selected incident/run and endpoint/auth context and never show another target's stale explanation after selection or evidence changes. Incident-action availability reads, journal observations and open confirmations MUST retain that origin; replacing the context with the same run ID MUST invalidate old reads and confirmations before submission.

### Key Entities

- **Selected incident**: exact identity, health/history, blocking status, affected occurrence/input and available evidence.
- **Current run**: lifecycle, pinned executable and definition identity; separate from incident health.
- **Recovery availability**: known permission/capability and applicable canonical action state, including pending/canceled and unknown/unavailable evidence.
- **Handoff**: a permitted inspection, sharing, author/operator or existing recovery-control destination with an explicit effect.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every specified incident/evidence case produces the correct selected-target explanation, with zero invented or duplicate recovery actions.
- **SC-002**: Supported-action cases retain all existing target/consequence/confirmation/authorization behavior; read-only cases produce zero unintended mutations.
- **SC-003**: Exact affected activity/input navigation and the original filtered/paged Runs return both succeed on the normal composition using disposable data.
- **SC-004**: Guidance remains readable and controls reachable at 1280×800 and 1440×900 in light/dark, plus narrow/zoom and keyboard/modal checks.
- **SC-005**: The live read-only handoff and permission/capability matrix establish their own evidence; neither the prior administrator recovery proof nor the Runs loading screenshot is counted as that proof.

## Assumptions

- Runs3062 is qualified on main5d1; Home3063 is not a dependency. This leaf is the sole implementation selected by the 22 October scheduling decision6329077033.
- Canonical compensation controls from PR3140 and M5 scheduled-retry state are already delivered. Reuse them; #2747/#3035/#3047 retain runtime/recovery ownership.
- Persisted and already-following operator-plan ownership retains its existing contract. Source-confirmed cross-context gaps are triaged separately in #3184; this leaf's new origin guards cover incident availability reads, journal observations and confirmations.
- No backend, permission policy, runtime semantics, new module references, optional provider setup or production deployment is included.
- Existing valid tests stay valid. Changed guidance/selection/availability behavior receives focused observable regression and red/green evidence, followed by normal-host and exact-head/resulting-main qualification.
- A failed required base gate stops implementation until reconciled. Final3075's nine composed acceptance rows remain separate.

## Implementation and acceptance mapping

Root integrated the isolated implementation and reviewed its production, test, style and documentation diff. `IncidentNextStep` renders current-target guidance; `useOperatorActions` shares exact-source Retry eligibility with the canonical controls; the toolbar and dialog share the exact-definition read observation. Existing action payloads and server admission remain unchanged.

- Next-step clarity and permitted handoff: focused selected-incident/authority/race/state tests, canonical operator-action tests and the complete affected Workflows suite.
- Evidence and return context: existing Workbench integration coverage remains intact; normal-composition proof must independently exercise exact failed-input inspection and filtered/paged return. Selection alone does not establish resolved input values.
- Readability and disclosure: both-theme browser cases at 390/1280/1440 CSS pixels, effective 200% zoom, keyboard dismissal/focus return and serious/critical axe checks.

Worker evidence records 2,330 listed/executed Workflows tests passed, 227 final focused tests passed, seven browser cases passed, typecheck, lint (zero errors; 56 unrelated warnings), client budget and library command exit 0. The library logs contain existing TS2742 declaration portability errors; root independently reproduced the same four diagnostics with unchanged main5d1 client/lock inputs and tracked them in [#3183](https://github.com/elsa-workflows/elsa-foundation/issues/3183). The library output is not described as diagnostic-free. Root independently verified a meaningful Scheduled-source guard mutation fails and the restored implementation passes. These implementation receipts do not replace independent candidate review, final local gates, normal-host/read-only/no-mutation proof, GitHub checks or resulting-main qualification. Those remain public issue/PR evidence required before acceptance. No deployment is claimed.
