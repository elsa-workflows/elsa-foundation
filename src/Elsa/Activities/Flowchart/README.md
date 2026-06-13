# Elsa.Activities.Flowchart

Runtime-only Flowchart activity module. The activity owns its executable child graph through the
`elsa.flowchart.structure` payload and the `Flowchart.Activities` child projection, then schedules
child executable nodes through the runtime composite-activity seam.
