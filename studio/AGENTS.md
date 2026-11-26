# Elsa Foundation Studio agent instructions

## Project conventions

For Spec Kit feature work, read the plan in the feature directory identified by the request, ticket, or branch. Do not assume one feature plan applies repository-wide.

### Issue tracker

Current Studio work is tracked in `elsa-workflows/elsa-foundation`. Follow the Foundation [issue-tracker guide](../docs/agents/issue-tracker.md) and [concurrent-work claim rules](../AGENTS.md#concurrent-work-claims). Foundation [#2501](https://github.com/elsa-workflows/elsa-foundation/issues/2501) records the Studio consolidation. Existing links to Studio-repository issues remain historical references; this does not reparent them. External PRs are not a triage request surface.

### Triage labels

Use only labels defined by the active Foundation issue or workflow. The standalone Studio label list in `docs/agents/triage-labels.md` is historical; do not assume those labels exist in Foundation.

### Fixes

Follow Foundation's [Quality Fix Routine](../docs/skills/catalog.md#quality-fix-routine), which also takes the `auto-qa` issues the UX QA routine files, and claim work on the Foundation issue using the root rules above.

### Domain docs

Before domain-sensitive exploration, follow `docs/agents/domain.md`: read the root `CONTEXT.md`, or the relevant contexts from `CONTEXT-MAP.md` when present, plus applicable ADRs.

### Design tokens

Module CSS styles against the `--studio-*` token contract, never raw colour literals or
another module's private namespace. The versioned contract — the stable tokens, the
`--studio-material-*` surface-role ladder, and the module
rules — is `docs/design-tokens.md`. It is enforced by `elsa/no-raw-color-literal`
(`pnpm lint:css`, wired into `pnpm lint`).

<!-- SPECKIT START -->
For additional context about technologies, project structure, contracts, and
validation scenarios for the active work unit, read
`specs/094-expression-code-intelligence/plan.md`.
<!-- SPECKIT END -->
