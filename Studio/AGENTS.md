# Global Codex Instructions

## Code style

- **DRY by default.** After implementing something, proactively check for repetition and extract helpers, shared setup, or abstractions without waiting to be asked.
- **Clean tests.** Prefer instance fields and constructor setup over repeating arrange blocks. Extract `ActivateAsync()`-style helpers. Use `IAsyncDisposable` for test class teardown where appropriate.
- **No unnecessary boilerplate.** Inline trivially small helpers rather than wrapping them in extra methods.

<!-- SPECKIT START -->
For additional context about the current agentic Studio experience
specification, implementation plan, and design contracts, read:

- `specs/003-agentic-studio-experience/spec.md`
- `specs/003-agentic-studio-experience/plan.md`
- `specs/003-agentic-studio-experience/research.md`
- `specs/003-agentic-studio-experience/data-model.md`
- `specs/003-agentic-studio-experience/contracts/agent-backend-api.md`
- `specs/003-agentic-studio-experience/contracts/studio-agent-sdk.md`
- `specs/003-agentic-studio-experience/contracts/workflow-agent-contract.md`
<!-- SPECKIT END -->

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `elsa-workflows/elsa-foundation-studio`; external PRs are not a triage request surface. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the default triage label vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, and `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Use a single-context domain documentation layout. See `docs/agents/domain.md`.

### Design tokens

Module CSS styles against the `--studio-*` token contract, never raw colour literals or
another module's private namespace. The versioned contract — the stable tokens, the
`--studio-material-*` surface-role ladder, the `--studio-glass-*` variant, and the module
rules — is `docs/design-tokens.md`. It is enforced by `elsa/no-raw-color-literal`
(`pnpm lint:css`, wired into `pnpm lint`).
