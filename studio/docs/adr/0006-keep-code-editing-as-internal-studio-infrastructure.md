# Keep code editing as internal Studio infrastructure

Studio now has public Expression Editor Contributions for workflow property editing. JavaScript expression support is provided by an external expression editor module, but Studio also needs code editing for non-expression surfaces such as global JavaScript functions and Extension Builder class library files.

Code editing should be shared internal Studio infrastructure, not a public SDK registry yet. The first shared code editor package is client-only and exposes editor-neutral document, diagnostic, language-adapter, and React component types for Studio-owned client modules. It is consumed as a normal package dependency, not loaded as a runtime Studio module manifest.

The public Studio SDK continues to expose expression-specific extension points through `api.expressionEditors`. A public `api.codeEditors` registry is deferred until non-Studio-owned modules need to contribute editor engines or language tooling. CodeMirror 6 is the default engine for compact and expanded Expression Editor Surfaces because its modular loading, small-field composition, Liquid support, and LSP integration fit Studio's requirements. The engine remains behind the internal host contract; Monaco may be reconsidered if a future requirement cannot reasonably be delivered through CodeMirror or LSP.

Each Expression Document has a stable identity scoped by workflow draft, activity, property, and Expression Type. Compact and expanded surfaces reuse that identity and editor session so document state, diagnostics, caches, and language-server models cannot collide or reset when the presentation changes.

The internal `StudioCodeEditor` owns editor profiles, Expression Document sessions, engine loading, and compact-versus-expanded mechanics. Expression Modules own their language extensions and the projection from Expression Authoring Context and Expression Symbol Catalog into language-specific tooling.
