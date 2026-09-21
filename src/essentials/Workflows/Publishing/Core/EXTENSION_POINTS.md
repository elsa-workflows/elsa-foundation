# Workflows.Publishing extension points moved

The Publishing-domain contracts live in `Elsa.Workflows.Publishing.Core`, but their defaults and obligations
are cataloged beside the two composition roots that own them:

- **Engine** (compiler, publication authority stores, policy/preflight/activation/projection, compilation
  fan-in, activity-template registries): [the Publishing engine catalog](../EXTENSION_POINTS.md).
- **Transport + activity-draft** (HTTP endpoints, transport authorization, activity-draft publish/test-run):
  [the Publishing API catalog](../Api/EXTENSION_POINTS.md).

These pointers avoid maintaining a second copy of the defaults and obligations.
