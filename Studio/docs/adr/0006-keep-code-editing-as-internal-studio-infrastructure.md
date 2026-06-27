# Keep code editing as internal Studio infrastructure

Studio now has public Expression Editor Contributions for workflow property editing. JavaScript expression support is provided by an external expression editor module, but Studio also needs code editing for non-expression surfaces such as global JavaScript functions and Extension Builder class library files.

Code editing should be shared internal Studio infrastructure, not a public SDK registry yet. The first shared code editor package is client-only and exposes editor-neutral document, diagnostic, language-adapter, and React component types for Studio-owned client modules. It is consumed as a normal package dependency, not loaded as a runtime Studio module manifest.

The public Studio SDK continues to expose expression-specific extension points through `api.expressionEditors`. A public `api.codeEditors` registry is deferred until non-Studio-owned modules need to contribute editor engines or language tooling. Monaco and CodeMirror are implementation choices behind the internal host, not architecture-level contracts.
