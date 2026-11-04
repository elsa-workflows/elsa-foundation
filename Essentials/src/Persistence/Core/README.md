# Source revision contracts

Provider-neutral working-copy, snapshot and retained-reference contracts shared by typed authoring
adapters. A source concurrency revision is not a release version; a snapshot is not executable material.
The scope is trusted adapter context, never authored JSON. Domain adapters retain permissions,
serialization profiles, validation and editing/cardinality policy.

The EF implementation and consumer-owned context composition are documented in
[Persistence.EntityFramework](../EntityFramework/EXTENSION_POINTS.md#shared-source-revisions).
Workflow and Agent placement, publication successor and pre-GA reset boundaries live in
[spec 1179](../../../../specs/1179-specialist-agents/plan.md#3790-source-slice-placement-and-transition).
