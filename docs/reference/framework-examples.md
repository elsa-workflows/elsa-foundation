# Framework Examples

These examples support the framework constitution without forming the enforceable gate surface. The enforceable rules remain in `.specify/memory/constitution-framework.md`.

## Integration vs consumption-shape examples

Supports framework §2.14.

For a message-broker integration:

- `<App>.Messaging.<Broker>` is the integration; it depends on the broker SDK and on `<App>.Messaging.Core`.
- `<App>.Messaging.<Broker>.<Consumer>` is the consumption-shape; it exposes broker behavior as the consumer's primitives and depends on the integration plus the consumer's `.Core`.

A consumer who wants broker messaging without exposing it as the consumer's specific primitive references only `<App>.Messaging.<Broker>`.

A `SyncContactToCrmActivity` does not live in either `<App>.Integrations.<SystemA>` or `<App>.Integrations.<SystemB>`. It ships as a dedicated synchronization/orchestration module that depends on both, such as `<App>.Integrations.<SystemA>To<SystemB>Sync`, or under a fresh orchestration domain.

## Extension-points catalog example

Supports framework §2.22.1 and §2.22.2.

Unit 1 (2026-06-03) created a set of per-domain catalogs at composition-root feature projects, covering domains with extension points.

Representative examples:

- `src/Elsa/Workflows/Design/Api/EXTENSION_POINTS.md` - draft mutation events, lookup/command/diff-engine override seams, and the `WorkflowsDesignApiFeature` composition root.
- `src/Elsa/Workflows/Design/Validations/EXTENSION_POINTS.md` - `DraftValidating`, `DraftValidated`, and the `IDraftValidator` contributor with intra-domain defaults.
- `src/Elsa/Persistence/EFCore/EXTENSION_POINTS.md` - `EntitySaving`, `EntityLoading`, contributor interfaces, and override contracts.

The repo-root `EXTENSION_POINTS.md` links every source catalog grouped by domain family. The root index is pure links; authoritative extension-point detail remains in each local catalog.

Generated maps are now the review surface for catalog/index drift:

- `docs/maps/extension-point-map.md`
- `docs/reports/maps-v2-findings.md`

