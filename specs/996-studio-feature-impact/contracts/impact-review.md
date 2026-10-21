# Impact Review Interaction Contract

This is the user-visible behavior contract for Feature Management's pre-Apply review. It does not add or change an external API or backend request shape.

## Staging and impact

- Single and bulk enable/disable continue using the current required dependency/dependant cascade. Optional dependencies do not create automatic changes.
- The review is for the currently selected Studio or Server host and lists every net difference between that host's loaded state and complete desired draft, even when the affected feature is filtered from the list.
- Every changed feature is identified as a direct edit or automatic effect. Direct setting edits appear as changed fields under the current sensitive-setting presentation.
- Each automatic effect names all effective enabled-state roots that require it and the required dependency/dependant path for each root. These roots span the host's full pending draft, not only the most recent toggle or bulk action. A later cascade that overwrites a root's requested state makes that root inapplicable; it cannot classify or explain current impact. Still-changed direct setting edits remain represented independently from enabled-state roots.
- Explanations are recalculated as edits change. A feature that returns to its loaded baseline disappears as net-zero; a reverted action is not shown as pending.
- With baseline `A=off`, `B=on`, and required edge `A → B`, disabling B then enabling A leaves `A=on`, `B=on`. B is omitted as net-zero, and the earlier `(B, off)` root cannot explain any surviving impact. A still-changed direct setting field on B remains visible independently.
- If the existing cascade leaves an automatic change after its initiating edit is reverted, keep that net difference and explain its earlier dependency/dependant cause as history. Example: baseline A=off/B=off, A requires B; enable A then disable A leaves B on. B is an automatic retained effect, A is omitted as net-zero, and no direct B edit or pending/current A requirement is asserted. Reset clears that retained cause.
- Show a cycle-safe explanation path for each applicable root; the review need not enumerate every equivalent route through the same dependency graph.
- Optional dependency relationships are excluded from reason paths and automatic changes.

## Review snapshot and actions

- The review names the exact target host and describes the complete desired state that will be applied.
- It is valid for the host, full desired state, loaded catalog snapshot, and current feature-configuration revision it represents.
- Any draft mutation, selected-host change, catalog refresh, or catalog replacement invalidates the open review. The operator must review the current state before Apply.
- Cancel closes the review, returns to the same local draft, and performs no host configuration change.
- Reset returns only the selected host's draft to its loaded state and clears a review of the previous draft.
- Apply uses the same target, complete state, and configuration revision shown by the current review. A failed request leaves a visible failure and never a success message. Some post-save refresh/reload failures can follow persisted configuration; an error alone is not proof that the host configuration stayed unchanged, so read the host again before describing its persisted state.

## Errors, reload information, and permissions

- Backend-provided reasons for conflicts and other failures remain visible. A conflict status alone does not establish that the cause was a stale revision; an activation refusal is not mislabeled as one.
- Show only reload implications supported by existing information. If no implication is known, say it is unknown. Do not promise hot reload, activation, or rollback.
- Authenticated users with inspect access but without existing management access can view feature information but cannot stage, bulk-edit, change settings, Reset, or Apply. Backend authorization remains authoritative.
- Anonymous and authentication-disabled demo behavior remains writable as before.

## Revision boundary

The review describes the catalog/dependency snapshot loaded by Studio and the existing feature-configuration revision. Refreshing or replacing the catalog invalidates the review. The existing revision does not atomically detect package or dependency metadata changed out of band after catalog load; the review makes no stronger guarantee.
