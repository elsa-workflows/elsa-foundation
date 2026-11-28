# Make Weaver tools and proposals resource-agnostic

Weaver is a Studio-wide assistant, not a workflow-only assistant. Modules contribute Weaver Tools into Tool Slots for their own Resource Targets, and mutating Tool Results are governed through resource-agnostic Proposals, Review Shells, Result Renderers, permissions, and policy. Workflow Operation Batches are one payload type within this broader model, not the general proposal model.
