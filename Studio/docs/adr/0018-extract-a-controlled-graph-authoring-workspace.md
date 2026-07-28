# Extract a controlled graph-authoring workspace

Studio shares graph editing by extracting a controlled graph-authoring workspace containing the canvas, palette, inspector, scope navigation, local history, layout, and graph interactions. Workflow Editor and Activity Definition Draft Editor remain separate resource hosts that supply controlled state, persistence, lifecycle actions, resource-scoped Contributions, and surrounding tabs; Studio does not reuse graph editing by threading an Activity Definition mode through the lifecycle-heavy Workflow Editor.
