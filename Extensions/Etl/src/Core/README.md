# Elsa ETL Core

`Elsa.Etl.Core` contains the shared shapes for finite ETL pipelines and their published artifact envelopes. The original v1 CSV-to-JSONL model and canonical codec remain unchanged; v2 adds explicit HTTP/file connector variants. It has no non-BCL dependency. Design-time validation, artifact hashing, runtime execution rules, and persistence policy belong to their owning packages.

`EtlPipeline` is a transport/authoring DTO. Its `IReadOnlyList` properties describe how consumers read collections; they do not freeze caller-owned lists. `EtlArtifactJson.Write` uses the typed DTO's declared property order, compact camel-case property names, and camel-case string enums. Design validates the pipeline before calling it. `EtlArtifactJson.Hash` hashes that compact serialization as lowercase SHA-256; it does not sort arbitrary JSON properties. `EtlArtifactJson.Read` rejects unknown and non-camel-case properties and numeric enum values. Design supplies the resulting document and hash to `EtlArtifact`, which clones the JSON snapshot so it does not depend on caller-owned lists or a disposed `JsonDocument`. The envelope persists its required format version as supplied so runtime can reject versions it does not support. Core accepts the supplied content hash as metadata and does not verify it.

Definitions refer only to host-configured binding aliases. The alias fields in this package are names, never arbitrary file paths or credentials. The first topology and bounded defaults are specified in [spec 255](../../../../specs/255-finite-file-etl/spec.md); the runtime and design packages own validation and enforcement.

`EtlPipelineV2` and `EtlArtifactV2Json` are separate versioned contracts and a bounded, strict codec. Their
source, pagination and destination variants are explicit; a valid decoded artifact is not evidence that a host
can execute it. Design owns compilation and runtime owns admission, retained binding validation and execution.
See [Core extension points](EXTENSION_POINTS.md#connector-artifact-format) for provider compatibility and
the [HTTP profile](../../../../specs/996-http-etl/contracts/http-profile.md) for supported connector semantics.
