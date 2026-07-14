# Elsa Foundation Studio agent instructions

## Project conventions

For Spec Kit feature work, read the plan in the feature directory identified by the request, ticket, or branch. Do not assume one feature plan applies repository-wide.

### Issue tracker

Issues are tracked in GitHub Issues for `elsa-workflows/elsa-foundation-studio`; external PRs are not a triage request surface. See `docs/agents/issue-tracker.md`.

### Triage labels

Use only labels that currently exist in the repository. See `docs/agents/triage-labels.md` for the workflow-specific labels and verification command.

### Domain docs

Before domain-sensitive exploration, follow `docs/agents/domain.md`: read the root `CONTEXT.md`, or the relevant contexts from `CONTEXT-MAP.md` when present, plus applicable ADRs.

### Design tokens

Module CSS styles against the `--studio-*` token contract, never raw colour literals or
another module's private namespace. The versioned contract — the stable tokens, the
`--studio-material-*` surface-role ladder, the `--studio-glass-*` variant, and the module
rules — is `docs/design-tokens.md`. It is enforced by `elsa/no-raw-color-literal`
(`pnpm lint:css`, wired into `pnpm lint`).
