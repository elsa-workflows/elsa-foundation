# Workflow run incidents in Studio

This guide covers incident and activity evidence in the Workflows module's run history and run inspector. The cross-repository product contract is maintained in [Foundation spec 192](https://github.com/elsa-workflows/elsa-foundation/tree/main/specs/192-incident-troubleshooting), tracked by [Foundation program 2334](https://github.com/elsa-workflows/elsa-foundation/issues/2334).

## Current health and lifecycle

Run history keeps the historical incident total separate from current health. Current counts include incidents whose status is neither `Resolved` nor `Suppressed`; a blocking count indicates that intervention is needed. A run can retain a non-zero historical total after all of its incidents are resolved or suppressed.

Current incident health and the runtime's reported lifecycle status are separate evidence. A node can still be reported as `Faulted` after an incident is resolved or suppressed; that status remains visible as reported, while current-health cues and counts reflect only active incidents.

The incident-health filter is available only when the Runtime host advertises the health-filter capability. On older hosts, Studio disables that filter and explains that current health cannot be used to select runs. Missing current-health counts are shown as unavailable; they are not inferred from the historical total.

For hosts with forward-only paging, Studio remembers visited page cursors so **Previous** returns to the preceding page, including the first page. Changing filters or page size starts a new history. A cursor opened directly without visited history cannot provide a previous page unless the host supplies a reverse cursor. Counts and filtering remain authoritative server results.

## Following an incident to runtime evidence

Open **Issues** from the run header or select an incident marker on the canvas. Studio associates incidents with their exact activity execution when the Runtime provides that identity, so repeated executions of the same activity remain distinct. Selecting an affected activity opens its incident; **View input evidence** opens the input snapshot for that execution when one was recorded.

When an affected activity is nested inside a visible container, the container shows an **inside** cue that opens the child incident. The cue preserves the container's own runtime status and does not describe the container as failed.

An input can have a recorded evaluation failure, a recorded successful evaluation, or no evaluation evidence. When no snapshot was recorded, Studio says so and does not imply that the input was evaluated. Technical failure details are available in the incident's expandable details.

Runtime summaries are paged. Studio loads a bounded batch of exact activity associations for active incidents and offers **Load more affected activities** when additional associations remain. Until those are loaded, the canvas shows that affected activities are still being resolved. Hosts that do not expose exact activity inspection show an unavailable-evidence state. Selecting an older incident can request its exact activity inspection separately.

The BPMN run canvas uses the pinned executable’s compact element/flow topology and exact child bindings. Layout and activity labels stay separate from execution evidence. Older hosts that omit BPMN topology cannot supply a complete BPMN canvas; Studio does not invent activity associations from names or identifiers.
