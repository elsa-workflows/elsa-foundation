# Elsa.Activities.Sequence

Runtime-only Sequence activity module. The activity owns ordered executable child scheduling through
the `Sequence.Activities` child slot and the runtime composite-activity seam.

Every run enters a Sequence at its first child, whichever trigger started it. So a start trigger is valid inside a Sequence only as child 0, and publishing refuses one at any later position (spec 238, the start-path rule, answered by `SequenceStartEntryProvider`). A mid-flow wait later in the Sequence sets `CanStartWorkflow` to false.
