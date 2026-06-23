# Studio SDK Weaver contributions

Studio modules can enrich Weaver through `api.agent` without calling an external
agent provider from the browser. Weaver is the visible assistant name; `agent`
is retained for provider-neutral profiles and contribution registries. Provider
access, policy, redaction, approvals, execution, and audit stay in the
Elsa.Server backend.

Register only declarative, scoped contributions:

- `contextProviders` collect minimized context for matching surfaces.
- `promptStarters` offer route-aware suggested prompts.
- `capabilities` describe what the module can help with, including risk and
  permission metadata.
- `actions` describe reviewable proposal shapes for mutating work.

Use stable IDs, set `moduleId` when the contribution belongs to a module, scope
`surfaces` narrowly, and include `requiredPermissions` for anything not available
to every authenticated user. Mutating capabilities should use `review-required`,
`destructive`, or `admin` risk and must be executed through backend proposals.
