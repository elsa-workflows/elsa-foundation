# Research: Workflow Publication Review UX

## Decision 1 — Use a three-region modal shell

**Decision**: The publication dialog will have a persistent header, one independently scrollable body, and a persistent footer containing the current actions. Success, validation, conflict, and recovery content belongs in the body or replaces it; no result appends after the footer.

**Rationale**: The current `PublicationReviewDialog` places header, alert/output, review content, and `.wf-dialog-actions` in one form. The dialog caps its height and makes that whole form scrollable. Any added alert pushes Close/Publish below the viewport, producing the observed overflow. A structural shell gives every state one reliable control location.

**Alternatives considered**:

- Let the whole dialog grow: rejected because small viewports still lose controls.
- Keep a single scroller and make actions sticky: rejected because it leaves form layout/stacking and dynamic-result edge cases coupled.
- Move publication to a wizard/full page: rejected because routine publication is a single bounded decision and would become heavier.

## Decision 2 — Make the channel, effect, version, readiness, baseline, and compact changes the default hierarchy

**Decision**: The default review presents a small decision summary first: Publication channel, derived effect, version, readiness, comparison baseline, and change summary. Activity/input/output/trigger breakdown moves to a Changes disclosure; policy/revision, trigger claims, IDs, token/concurrency facts, and raw server evidence move to Advanced details.

**Rationale**: The current eight-cell facts grid gives internal evidence equal visual weight with the author’s decision. The user needs to know what will happen and whether it can happen before they need the evidence explaining why.

**Alternatives considered**:

- Retain the facts grid but reorder it: rejected because the grid's density and equal-weight cells remain intimidating.
- Hide all details: rejected because authors need access to policy and diagnostic evidence when publication is blocked or surprising.
- Show a semantic-bump explanation based on the change summary: rejected because workflow publication currently does not infer a SemVer bump from those changes.

## Decision 3 — Replace Slot/action controls with a user-facing Publication channel selection

**Decision**: Keep `slotName` and action resolution on the wire, but label the UI **Publication channel**. Offer existing channels in a selector, clearly identify `default` as the normal channel, and provide a separate “Create new channel” path that alone reveals a name field. The chosen/authoritatively resolved channel derives the effect.

**Rationale**: `slot` is an implementation term in the current dialog; free text plus replace/side-by-side radios requires the user to understand policy mechanics before understanding the outcome. Existing channel data already includes publication occupancy, and preflight remains the authority for the final result.

**Alternatives considered**:

- Rename the current input only: rejected because it leaves the confusing free-text and radio interaction intact.
- Allow arbitrary free text in the selector: rejected because it hides existing-channel replacement risk and causes spelling variants.
- Present action as a second user choice: rejected because effect is determined by the selected channel and policy, not by a routine author preference.

## Decision 4 — Automatically refresh authoritative review and keep Publish stable

**Decision**: Changing the channel or requested exact version immediately invalidates the previous preflight, shows an inline “Checking…” state, disables Publish, and starts a new latest-only authoritative preflight. Publish remains the primary label; it never becomes “Review target.”

**Rationale**: The current operation flow correctly requires a fresh preflight for a changed intent, but asks the author to press a separate “Review target” button. This turns a technical safety step into a second action and makes the primary action label unstable. The authoritative token and expected-publication ID continue to protect the mutation.

**Alternatives considered**:

- Keep a “Review target” primary button: rejected because it obscures the normal next action and duplicates a system-required operation.
- Reuse prior preflight while target/version changes: rejected because it permits stale conflict/version evidence.
- Disable Publish without feedback: rejected because authors cannot tell whether validation is pending or broken.

## Decision 5 — State truthfully that the version is policy-assigned

**Decision**: In the automatic path, label the displayed value “Assigned automatically” or equivalent and describe it as policy-assigned. Do not describe it as a proposed semantic bump derived from workflow changes. The server-confirmed value becomes the outcome value.

**Rationale**: `createPublicationReview` currently calculates `nextVersionLabel` by incrementing the major component when it can parse a stable SemVer. That is an optimistic client label, not change-aware semantic analysis, and `promoteDraft` is the server mutation that determines the confirmed version.

**Alternatives considered**:

- Explain semantic impact from the compact diff: rejected because the diff is not an authoritative versioning rule.
- Omit a pre-publication version entirely: rejected because users need a concise expected outcome.
- Treat the client's label as final: rejected because the server remains authoritative.

## Decision 6 — Exact-version support is an additive discovered capability

**Decision**: Studio renders “Edit version” only when the Workflow Design capability advertises both the promotion-version preflight and exact-promotion relations. The selection remains automatic by default. The relations are absent on existing Foundation deployments and Studio sends no requested-version field there.

**Rationale**: The existing workflow `PublicationIntent` carries target/action/concurrency data and current preflight has no exact-version capability or requested-version field. A presentation-only input would falsely promise control the server cannot honor. The client already resolves Publishing links from the API capability document, providing a backwards-compatible discovery mechanism.

**Alternatives considered**:

- Always show an exact-version input and let old servers reject it: rejected because it gives users an unusable control and violates graceful compatibility.
- Add a Studio-only configuration flag: rejected because capability must represent actual deployed server support.
- Implement only a custom label after promotion: rejected because version selection must be validated before promotion.

## Decision 7 — Preserve save/promote/activate and model outcome as replacement states

**Decision**: Keep the established captured-snapshot sequence: save the captured draft, promote once, then activate/publish with the server preflight token. On success, replace the review body with a compact confirmed outcome. When promotion succeeds but activation fails, replace it with a retained-version recovery body offering activation retry and Close.

**Rationale**: `useWorkflowOperations` already preserves the promoted ID after activation failure and retries publish without a second save/promote. The current dialog merely appends messages to the full review, making the state visually unclear and causing the footer bug.

**Alternatives considered**:

- Close automatically after success: rejected because the author loses confirmation and the optional executable-inspection action.
- Retry the full flow after partial failure: rejected because it can produce an unnecessary second promoted version.
- Make activation failure a generic error: rejected because retention is material information needed for safe recovery.

## Decision 8 — Reuse existing Workflows test seams and prove height behavior in a browser

**Decision**: Extend `publicationReview.test.ts`, `publicationSlots.test.tsx`, and `workflowPublicationOperations.test.tsx`; add/extend a focused Playwright Workflows publication fixture for visual/interaction proof.

**Rationale**: Existing tests already cover snapshot preflight, expected publication IDs, stale preflight, validation, retained-promotion retries, and slot behavior. They are the natural regression boundary. DOM tests cannot prove a persistent footer under constrained heights, so browser proof is mandatory.

**Alternatives considered**:

- Create parallel publication logic/tests: rejected because it would duplicate mature concurrency and recovery coverage.
- Browser tests only: rejected because latest-only preflight state transitions and request shapes need deterministic fast coverage.
- Unit tests only: rejected because they cannot prove the overflow fix or actual focus/scroll behavior.
