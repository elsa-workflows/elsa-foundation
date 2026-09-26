# Data model: diagnostics group publication

The existing [selection documents](../174-profile-selection-planner/contracts/selection-planner-v1.md) remain unchanged.

- **Catalog snapshot**: Foundation publisher, schema version 1, catalog ID, immutable catalog version/digest, profile definitions, and group definitions. Bundled versions 1 and 2 coexist. Version 2 contains the unchanged `embedded-runtime@1` profile plus one `diagnostics-ef@1` group.
- **Group definition**: Flat kind `group`, ID/version/digest, exact stable feature members, human-readable rationale/title/description, and two required dependency explanations. It has no configuration values or nested group references.
- **Authored composition**: One catalog pin, one profile pin, zero or more group pins, explicit additions/removals, and an accepted exact feature set. An old catalog pin identifies old bundled content rather than a request to upgrade.
- **Selection plan**: Existing exact IDs, per-member profile/group reasons, reviewed required-edge evidence, and unresolved host/persistence findings. A missing required target remains absent; the plan never repairs it silently.

Validation reuses the strict document reader and canonical digest calculation from spec 174. Catalog ID/version/digest must all match for bundled resolution; a changed published definition with the same ID/version is rejected by digest validation.
