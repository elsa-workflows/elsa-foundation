# Groundwork Core

Groundwork Core defines provider-neutral persistence intent: manifests, storage units, workload classification, indexes, portable queries, provider capabilities, materialization plan concepts, schema-history records, and validation diagnostics.

This package is generic infrastructure. It does not reference Elsa packages or provider-specific database libraries.

## Extension Points

- Storage manifests describe durable intent.
- Provider capability reports describe what a provider can materialize.
- Validators return structured diagnostics for preview and startup checks.
- Materialization concepts provide the shared language for later provider packages.
