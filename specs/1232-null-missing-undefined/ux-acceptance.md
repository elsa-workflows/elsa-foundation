# Studio acceptance: nullability, defaults and diagnostics

This record applies to the visible changes in FR-001a, FR-033 and FR-034. Policies are set through engineer APIs
and converter configuration; Studio presents no policy control or label.

## User goal and reference

A workflow or activity-definition author declares a member's type, requiredness and default, saves the definition,
reopens it, and test-drives it with values consistent with the published contract. An operator inspecting a run can
understand a boundary warning or substituted value from the existing activity execution diagnostics.

Use the current activity-definition contract editor, workflow properties and test-drive panels as the interaction
references, and [shared Studio controls](../../Studio/docs/module-ui.md) for fields, hints, disabled states and focus.
Use the existing activity execution warnings as the diagnostics reference. No new control styling is needed.

## Observable states and smallest composed journeys

| Journey | States and expected result |
|---|---|
| Edit an activity-definition input | Begin with an empty default and Required on. Set a default: Required turns off, remains disabled while the default exists, and the hint explains why. Save and reopen: type, default and optional state persist. Clear the default: Required can be chosen again. Cancel a further edit: persisted values remain unchanged and focus returns coherently. |
| Nullability follows type | For workflow inputs/outputs and authored activity-definition contracts, String/Any accept null and Int32/Boolean reject it without default-on-null. No Allow null editor or contradictory label remains. Change type, save and reopen: test-drive validation follows the saved type. A required nullable input accepts an explicitly supplied null; omission is still refused. |
| Workflow and activity-definition test drives | Run optional nullable omission, supplied nullable null, omitted required input and invalid non-nullable null. Valid runs succeed; refused runs name the member and allow correcting the input. Verify the refusal schedules no workflow step through the run/API evidence. .NET activity metadata continues to use its C# declaration. |
| Inspect boundary diagnostics | Run the FR-016/016a/017 examples and a non-nullable activity-input substitution under default-on-null. Open the execution warnings: codes, permitted paths, severity and guidance remain readable. A warning after protected data was read names only the root. Admission substitution needs no separate warning (FR-013). |

Inspect the changed editor, test-drive and warning surfaces in the composed host at desktop and narrow widths in
light and dark themes. Use keyboard-only navigation for setting/clearing the default, disabled Required state,
validation recovery, save/cancel and reopening the test drive. Check focus, readable hints and absence of clipped
labels or actions. Exercise both empty and populated contracts. Existing read-only views must not offer editing.

Loading, denied-access administration and graph wiring are excluded: S-2 changes neither their data loading,
authorization nor graph interactions. If implementation changes any of those paths, extend this record before
claiming acceptance. API/definition tests separately prove that the policies round-trip without a Studio control.

## Evidence and independent acceptance

T035a runs these journeys on an owned Workbench/Studio host built from the committed implementation revision,
following [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md). Extend the composed suite where
its existing journeys do not cover the rows above; isolated client fixtures provide supporting evidence only.

The implementation PR links the exact Actions run and downloadable artifact, identifies tested source/merge SHA
and PR head, and retains screenshots, accessibility snapshots and traces. An independent reviewer opens the
screenshots, inspects the complete affected experience and records the guide's acceptance receipt on that PR.
Failures, omissions, skips and uninspected states remain failed or incomplete. Fix and rerun the affected whole
suites and journeys on the final head, then refresh the independent inspection before delivery is accepted.

## Implementation journey

`nullability defaults and execution diagnostics` extends the owned composed runner across all six desktop/narrow
light/dark projects. It retains exact-source screenshots, accessibility snapshots, traces and `null-policies-proof.json`.
The recovery fixture changes one newly created draft in the runner-owned disposable database into an old
required-with-default contract, recording before/after revision and hashes. This is explicitly a legacy fixture,
not a claim that the current API accepts that invalid declaration. No HTTP response or UI is substituted.

Nullable String input controls distinguish explicit null, the text "null", an empty string and omission. The published
Run dialog obtains nullability from its exact executable input contract through existing runtime read permission;
its metadata loading/refusal path is included in the acceptance scope. Client-side requiredness refusals send no
start request, and an independent API refusal verifies non-nullable null admission. Policies remain engineer JSON,
without a Studio policy control. A locally downloadable owned-run archive is used while CI is explicitly deferred.
