# Extension points — Elsa 3 JSON Reconciliation

Imports Elsa 3 workflow files at engine start through the Elsa 3 collection import (spec 235). It exposes no contract
of its own.

---

## Startup task

- `Elsa3FolderImportTask` (internal, `IStartupTask`, `[Order(4)]`): after activity reconciliation (1), workflow
  reconciliation (2) and the Git export (3), before interrupted publications are completed (5). Runs on every node,
  once per persistence scope (`IPersistenceScopeRunner`), and takes no lock.

## Decorated contract

### `IActivityPublishingAuthorizationContext` *(owned by `Elsa.Workflows.Publishing.Api`)*

- **Decorator:** `Elsa3StartupPublishingAuthorization` (internal), registered by `Elsa3JsonReconciliationFeature`
  around the registration it finds (the default is `HttpContextActivityPublishingAuthorizationContext`), with the same
  lifetime.
- **Behaviour:** while the scoped `Elsa3StartupPublishingGrant` of the same DI scope is set, `TenantId` is the granted
  tenant and `CanAccessTenant` accepts that tenant and no tenant; otherwise every call goes to the decorated context.
  Only the startup task sets the grant, inside the operation scope the persistence runner created for it.

## Consumed contracts

- `IReusableActivityCollectionImporter` (analyze and apply, unscoped, with `TenantId` and `OperationKey`).
- `IPersistenceScopeRunner`, `IPublicationPolicyStore`, `IPublicationPolicyResolver`, `IWorkflowActivationAuthority`,
  `IPublicationRecordStore`, and `PublishWorkflow` through `IRequestSender`.

## Feature

- `Elsa3JsonReconciliation` depends on `Elsa3ImportActivitiesPublishing` and `WorkflowsPublishing`. No event handlers,
  contributors or replacement contracts.
