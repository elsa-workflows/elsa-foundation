# Activity catalog disclosure

`ActivityCatalogDisclosure` supplies bounded requirement canonicalization, current disclosure policy
evaluation and trusted producer admission. It depends on `Events` and `Tasks`; hosts install this package
and compose the feature explicitly. Implementer values and ports live in `Elsa.Activities.Design.Core`.

This foundation does not yet connect existing catalog persistence, reconciliation or HTTP/assistant
readers. It enables no private-source production profile. Retention, write integration, lineage,
selection, receipts and complete projection/history qualification remain in the
[catalog plan](../../../../../specs/1219-entity-aware-integrations/plan.md) and
[tasks](../../../../../specs/1219-entity-aware-integrations/tasks.md).

The [extension-point catalog](EXTENSION_POINTS.md) describes registrations, replacements and lifecycle.
Public requirements must be explicit and valid. Protected requirements form a bounded conjunction;
missing identity, owner, policy or supported schema cannot grant access. Provisioning admission does not
grant interactive disclosure or source invocation.

Core values and registration metadata are public extension contracts. Implementations stay internal;
the feature is public and unsealed. The package tracks its public surface and uses computed preview
versioning. This introduces preview contracts; there is no compatibility overload that supplies Public
or Allowed for an unspecified requirement.

## Cross-domain contributions

`InitializeCatalogDisclosure` implements the Tasks startup contract and publishes the Core registration
event through the Events inline publisher. It awaits the single registration aggregator before freezing
the registry. No recurring/background task, HTTP endpoint, source credential or Runtime-to-Design lookup
is registered.
