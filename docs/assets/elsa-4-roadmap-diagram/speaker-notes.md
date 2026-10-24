# Speaker notes: One platform. Multiple execution models.

About 45 seconds.

Elsa 4 is one platform with several execution models.

At the top, Elsa Studio is one place to design, inspect and operate, with an editor that fits each model.

Below are the execution models. Native workflows compose with Sequence, Flowchart or agent collaboration, and each composition owns its own routing and joins. BPMN keeps its own token and gateway semantics, and runs directly or embedded in a workflow on the same interpreter. ETL pipelines own partitions and checkpoints, and a workflow can invoke one as an activity too. The dashed slot leaves room for more.

They all share one durable foundation for identity, state, timers and recovery.

Hosting places and coordinates executions, one actor per execution, while workers do the work. Orleans and Proto.Actor are possible adapters. This is the target, not today's release.
