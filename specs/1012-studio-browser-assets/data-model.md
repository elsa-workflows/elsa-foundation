# Asset and qualification records

No new domain entity or persisted application state is introduced.

- Browser payload: relative archive path, public URL, exact bytes/digest and MIME/cache expectations. Hosting owns standard payload; each module owns its existing namespace.
- Import map: existing bare-specifier-to-URL mapping; every target must resolve to the published payload and maintain shared React/SDK/UI runtime semantics.
- Candidate provenance: committed source revision, producer inputs, package ID/version/archive hash, restored package type/cache hash and published file/DLL hashes.
- Consumer profile: standard, separately named fallback, rejected same-path overlay or custom-frontend opt-out. Records own inputs, commands, denied-tool audit, HTTP/browser outcomes and owned process lifecycle.

Only candidate/report data moves from planned to packed, restored, published and qualified. Negative controls intentionally enter failed state and require restored qualification. A local qualified artifact is not a merged/main-delivered outcome.
