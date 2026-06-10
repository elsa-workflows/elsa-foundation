# Groundwork.MongoDb

`Groundwork.MongoDb` provides MongoDB materialization and document storage for portable Groundwork document workloads.

## Current Scope

- Creates one MongoDB collection per storage unit.
- Creates native MongoDB indexes for declared one-field indexes.
- Saves, loads, updates, deletes, and equality-queries JSON document envelopes.
- Enforces unique declared indexes with MongoDB unique indexes.
- Uses optimistic concurrency through expected document versions.

## Deliberate Limits

- Equality queries only.
- One-field indexes only.
- JSON content is stored as native BSON under a `content` field.
- No Entity Framework dependency.
- No Elsa dependency.
