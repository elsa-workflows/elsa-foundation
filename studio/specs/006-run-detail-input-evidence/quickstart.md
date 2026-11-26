# Quickstart: Run Detail Input Evidence Workbench

## Prerequisites

- .NET SDK required by `/Users/sipke/Projects/Elsa/elsa-foundation/global.json`
- Node and pnpm versions required by `/Users/sipke/Projects/Elsa/elsa-foundation-studio`
- Both repositories on `codex/run-detail-input-evidence`
- Do not modify or remove the unrelated Groundwork schema lock in the Foundation worktree

## Delivery order

1. Implement and test the Source Reference Authored Inputs Sidecar separately from content-addressed executable bindings.
2. Implement Foundation compiler/materialization/capture/projection propagation and replay-safe evaluation identity.
3. Generate or update any checked-in client contract only through the repository’s established mechanism; otherwise update Studio types in the same change.
4. Implement Studio row derivation and evidence/source rendering.
5. Implement shell-aware and container-responsive layout.
6. Run integration verification and real-route browser proof.

## Focused validation

From Foundation, run the smallest affected test projects first, then build affected projects and the normal repository validation required by changed code.

From Studio:

```bash
pnpm --filter @elsa-workflows/studio-workflows test
pnpm --filter @elsa-workflows/studio-workflows typecheck
pnpm lint
pnpm build
```

Run browser verification for `/workflows/instances/:id` at:

- Desktop with 400px inspector and console at min/default/max.
- Medium with overlay inspector.
- Narrow with mutually exclusive canvas/inspector.
- Container transition while an activity and expanded input remain selected.

## Security fixtures

Verify all four permission combinations:

| `workflow-publishing.read` | `workflow-runtime.read` | Expected |
|---|---|---|
| allowed | allowed | Both sections show permitted bounded data. |
| allowed | denied | Source visible; evidence permission-hidden. |
| denied | allowed | Evidence visible; source permission-hidden. |
| denied | denied | Both sections independently protected. |

Also inspect network responses and rendered DOM to confirm protected values are absent, not merely hidden.

## Compatibility fixtures

- Legacy source without `inputKey` or authored record.
- Legacy evidence without phase/sequence.
- Unknown authored expression and compiled binding discriminator.
- Duplicate key, missing binding, orphan evidence, unavailable snapshot, and failed evaluation.
