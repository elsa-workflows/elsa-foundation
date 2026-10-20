# ETL workflow publication

`EtlWorkflowsDesign` contributes an `IExecutableCompilationSource` for `RunEtl`. It resolves a literal `EtlPublishedSelection` in the trusted tenant/persistence scope, verifies the exact published artifact and embeds the bounded envelope in the executable node. It contributes no workflow dependency edges and changes no generic workflow hash format.

The selection token includes the full ETL content hash. Because ordinary literal inputs are already hashed, changing the selected artifact changes the parent executable identity even though node metadata itself is not behavioral hash input. Runtime validates the metadata against this literal and executes without the design store.

The structured Studio picker and its paginated published-definition source remain part of [spec 255](../../../../../specs/255-finite-file-etl/tasks.md). The current contributor accepts exact canonical literal selections; it refuses dynamic and secret bindings.
