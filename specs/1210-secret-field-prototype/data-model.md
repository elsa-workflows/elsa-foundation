# Data and state

No API or persistence shape changes. Existing CreateSecretRequest remains authoritative.

The module-local field shell receives label, current value, supporting/error content, disabled state, a native control renderer and optional trailing action. Visual states derive from focus, value/native autofill, explicit invalid/disabled/read-only attributes and control kind. No label text is inferred from placeholders.

Create lifecycle remains draft → submitting → dismissed on success or editable error on failure. Submission disables editing to preserve the submitted snapshot. Scope catalog loading/error affects suggestions and refresh, while existing/custom values stay available.
