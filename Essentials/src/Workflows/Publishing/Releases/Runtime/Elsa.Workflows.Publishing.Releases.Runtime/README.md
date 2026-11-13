# Compiler-free Runtime release bridge

This optional package bridges captured Runtime models to the release semantic verifier. It depends on the release
verification contracts and Runtime Core models; the two-package transport foundation remains independent of Runtime.

The fixed Web JSON codec uses a **new release wire format**: `{"formatVersion":1,"content":...}` around a typed source
scope and workflow executable, activity template, neutral runtime model, selected workflow association or reusable
activity-version projection. This is not the historic naked `WorkflowExecutable` serialization. Computed workflow
`Nodes`/`NodesById` and template `NodesById` are excluded. The original supplied blob remains immutable in a captured
release; decoding never replaces it with a reserialized blob. CLR aliases and opaque JSON are preserved without type
loading, and unsupported aliases, invalid Unicode, duplicate properties and lossy projections are refused.

Encoding checks object/JSON depth and byte budgets before and during serialization. Decoding checks the input byte
budget before copying. Stable refusal codes omit payload values and exception messages.

The artifact verifier checks native hashes using the selected `IWorkflowExecutableHasher`, template hashes using the
Runtime-owned `ActivityTemplateHasher`, neutral model hashes, exact retained pins and captured generic dependency
inventories. A native selected-source projection must match its captured artifact association before allowing that
exact content with `ActivateServing`. Original selected and producer IDs remain independent; this association does
not authenticate publication origin or prove authored-to-compiled equivalence.

Current input member selections preserve literal serialized path segments and known/deferred facts. Their selected
type, exact activity-value profile revision when present, and engine schema become independently discovered
prerequisites. Target readiness must check those requirements; semantic verification grants no compatibility waiver.

Unknown activity consumers, CLR activity semantics, structures, metadata, provider/public contracts, expression
languages, conversions and model protocols require their exact owner inspector. Opaque or dynamic references are
unsupported when their complete dependency/resource meaning is unavailable. A model body has no root capability.
The current native selection codec does not describe a direct-BPMN publication receipt.

Compose `WorkflowReleaseRuntimeFeature` alongside `WorkflowReleaseVerificationFeature` and exactly one Runtime-owned
`IWorkflowExecutableHasher`. The feature does not start Runtime, import owner stores, assess target capabilities,
activate slots or execute material. Passing controlled framework tests is not complete native/BPMN/ETL release
acceptance. See [extension points](EXTENSION_POINTS.md) for replacement and owner-inspection seams.
