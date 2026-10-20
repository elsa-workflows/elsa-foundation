# Research: HTTP inspector disclosure

Source: selected base `825aa98dd624295655576f7ed299f6146b991bc8`. Graph discovery used existing Foundation/Studio indexes first; stale symbol paths were checked against this checkout. Relevant HTTP/panel/SDK source is unchanged from the readiness snapshot `2394948`.

## Decisions

- Use the existing catalog contract. `ActivityAuthoringCatalogReader.ToView` emits normalized tier and requiredness; `ActivityInputTiers` canonicalizes essential/optional/setting and returns null for unknown backend values. A second API or HTTP-specific field-name classifier is unnecessary. Studio's descriptor conversion passes inputs through.
- SDK fields stay optional and forward-compatible. Requiredness is separate from presentation. Only optional inputs that are not required enter secondary disclosure. Setting, unknown and missing tiers stay visible; this work does not design a Settings-tab workflow.
- Promote only HTTP Connection's tier. The declaration already has the connection-picker contract and no Required attribute. Body stays Essential and content type stays Optional. The runtime adds nonempty content regardless of Method. Method-specific clearing/hiding would invent a new applicability contract and risk existing bodies.
- Shared AdvancedSection keeps children mounted and forces open for setCount/errors. Reuse it rather than copying a new accordion or importing #3062's unmerged shared-component change. Generic “More options” avoids mislabeling non-HTTP activities.
- Count authored state before default synthesis. Catalog authoring templates currently contain empty input/output dictionaries; descriptor defaults are separate. `createActivityNode` clones templates, and the wire adapter also preserves foreign/seeded wire entries. Stored default/bound/conversion/passthrough state must remain discoverable without mutation. `readWrappedInputValue` is an editor view, not an authored-presence predicate.
- Aggregate current host-owned errors only. Existing row signals include inline/semantic error diagnostics and dynamic-options failure. Source/document-revision matching and the current row scope prevent stale errors from leaking. Warnings, loading, unavailable tooling and conversion-profile suggestion fallback are not input validation. Custom property editors have no private-error callback; do not infer it from DOM. Preserve access conservatively if such a concrete case exists.
- Preserve grouping and order through the existing groupInputs authority. Partitioning tiers must not alphabetize primary inputs or discard custom labels.

## Alternatives rejected

Hiding all nonrequired fields would lose metadata intent. Comparing displayed values only with defaults would hide explicitly authored defaults and miscount synthesized values. Unmounting collapsed rows would reset editor state and abort/restart lookup/validation hooks. A field-name HTTP adapter would duplicate the catalog. A new global validation/editor contract is not needed for demonstrated host-owned error signals. No external technology choice or dependency is introduced.

## Evidence limits

Panel/fixture tests prove presentation and lossless interactions. The rebuilt-server HTTP status-outcomes E2E proves authored publication/runtime routing, not UI disclosure or authenticated Connection use. Actual composed Studio screenshots and keyboard checks remain required. No implementation, test pass or runtime result is claimed by this research file.
