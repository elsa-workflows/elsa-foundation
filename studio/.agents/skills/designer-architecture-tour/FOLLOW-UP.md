# Follow-up task seed: repair the architecture-tour documentation

Create a follow-up task to update the `designer-architecture-tour` skill and its documentation references.

The skill currently points to `docs/skills/catalog.md#architecture-tour` and
`docs/architecture-tour.md`, but neither file exists in this repository. The
follow-up should identify the canonical current architecture documentation,
restore or replace the missing documents as appropriate, and update the skill's
name, description, compatibility statement, and links to match the current
`elsa-foundation-studio` repository.

When dashboard terminology is covered, keep contributions optional and
value-driven. Do not introduce a requirement that every module contribute a
dashboard widget.

Acceptance criteria:

- Every path referenced by the skill exists and describes the current shell,
  module, routing, API, and authentication architecture.
- The skill no longer refers to the repository as `elsa-foundation-designer`.
- Dashboard documentation describes the slot and admission criteria without a
  one-widget-per-module cardinality rule.
