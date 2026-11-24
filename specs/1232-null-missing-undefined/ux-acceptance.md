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

General loading, denied-access administration and graph wiring are excluded: S-2 changes neither their data loading,
authorization nor graph interactions. The published-Run executable-contract loading/refusal path is included below.
If implementation changes other such paths, extend this record before claiming acceptance. API/definition tests
separately prove that the policies round-trip without a Studio control.

## Evidence and independent acceptance

T035a runs these journeys on an owned Workbench/Studio host built from the committed implementation revision,
following [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md). Extend the composed suite where
its existing journeys do not cover the rows above; isolated client fixtures provide supporting evidence only.

With CI explicitly deferred, the implementation PR links the source-bound downloadable local proof, identifies
the tested source SHA and PR head, and retains screenshots, accessibility snapshots and traces. Any later Actions
evidence identifies its own exact run and artifact. An independent reviewer opens the
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


The expanded journey changes an existing activity input from Int32 to String and back through the editor, saves and
reopens each state, and verifies that the test drive follows the persisted type. It inspects an actual non-nullable
Publish Event input substitution. A deliberately held then aborted metadata read qualifies only the published-Run
loading/refusal experience; the subsequent recovery and admission use the owned server's real responses. Those
controlled transport captures do not establish a server outage or a successful workflow admission.

The final capture inventory contains 43 named states per appearance. The journey and proof inventory includes
empty activity Inputs/Outputs and an empty workflow Test Run, populated workflow/activity contracts, persisted
Any/Boolean/Int32 contracts, omitted nullable activity input, required/null refusal and correction,
published/historical/read-only authored contracts and actual C# nullability. Named viewport captures
retain lower contract rows, the beginning and end of warning lists, pre-recovery legacy required/default state, and
published explicit null versus the text "null" versus an explicitly supplied empty string. The journey scrolls actual
controls into view; no injected styles or resized canvas substitute for the narrow experience.

Keyboard qualification starts by focusing a known control, then uses native Home/ArrowDown, Tab/Shift+Tab, Space
and Enter through default editing, cancellation, recovery, opening/reopening and execution. It does not claim complete
application-wide Tab navigation. Shared output creation is exercised through S-2's actual Add output/save boundary.
The broader User Tasks authoring journey's earlier Form ID focus failure remains failed under the existing
[#4929 baseline disposition](https://github.com/elsa-workflows/elsa-foundation/issues/4929#issuecomment-7088529039);
S-2 does not claim that broader journey passed.
