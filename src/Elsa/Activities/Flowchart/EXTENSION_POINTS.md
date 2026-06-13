# Elsa.Activities.Flowchart Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `Flowchart.Activities` child slot
- `elsa.flowchart.structure` structure payload with schema version `1.0.0`
- `FlowchartStructure.Connections` containing `FlowchartConnection[]`
- `FlowchartStructure.StartNodeId` optional start-node selection
