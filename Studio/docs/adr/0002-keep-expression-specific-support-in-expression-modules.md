# Keep expression-specific support in expression modules

Workflow authoring exposes Slots for activity property editing and consumes expression descriptors, but expression-specific behavior belongs to expression modules. JavaScript expression support is provided by a JavaScript Expression Module that contributes editors, metadata, validation diagnostics, Weaver Tools, and help instead of embedding JavaScript-specific behavior in the Workflows module. This keeps Workflows focused on workflow authoring while allowing expression languages to evolve independently.
