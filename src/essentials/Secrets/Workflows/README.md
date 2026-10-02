# Elsa.Secrets.Workflows

The workflow runtime adapter for the Secrets module. Feature `SecretsWorkflows` resolves an activity input bound to a
`Secret` reference through the Secrets module's `ISecretValueResolver`, each time the activity is activated, for the
tenant the runtime hands over: the execution partition read at activation. On the background drain path that is the
host's persistence scope, so the two-tenant scenario is proven with one host per tenant over one secret store; a drain
whose dispatch options carry ambient services was not verified for several tenants (research R2). The design is [spec 188](../../../../specs/188-workflow-secret-safety/), research R1, R2
and R4, and [the resolution contract](../../../../specs/188-workflow-secret-safety/contracts/runtime-secret-resolution.md).

## What it registers

`SecretsWorkflowsFeature` (`DependsOn` `Secrets` and `ActivitiesRuntime`, no settings) calls `AddSecretsWorkflows`,
which registers `SecretValueRuntimeResolver` as the container's `IRuntimeSecretResolver`, scoped like the
`ISecretValueResolver` it reads through. It registers nothing else: no event handler, no task and no other runtime
contract.

`IRuntimeSecretResolver` is a replacement contract with at most one implementation per container, and the runtime
registers none. `AddSecretsWorkflows` refuses to register beside another one, as an early diagnostic: when a different
`IRuntimeSecretResolver` is already registered it throws an `InvalidOperationException` naming both. Calling it again
where it already registered the bridge adds nothing. A resolver registered after the bridge is not seen by that
registration; the activities runtime's startup check refuses it instead: shell activation fails with
`MultipleRuntimeSecretResolversException`, naming both. A host that supplies its own resolver composes it instead of
`SecretsWorkflows`, not beside it.

A shell without `SecretsWorkflows` cannot resolve secrets at all: a secret-bound activity waits with an
activation-failure incident (`MissingSecretResolver`) until the feature is composed, instead of faulting. The Workbench
enables it in `shells.json`, `shells.baseline.json` and the compose shells file.

## What a resolution does

- **Passes through.** The tenant is the one the runtime hands over, the execution partition read at activation, and the
  reference's name, type name and scope reach `ISecretValueResolver` unchanged. The bridge chooses no tenant and no
  default.
- **Classifies.** A success returns the value. A failure returns the Secrets failure code by name, one mapping arm per
  member, retryable only for `StoreUnavailable`; every other code is permanent. The default resolver reports
  `StoreUnavailable` only for an outage of the payload store or the secret repository (a timeout, I/O or socket
  failure; a database failure the provider calls transient, in SQLSTATE class `08`, or carrying a timeout, I/O or
  operating system error; SQLite's busy or locked database), and
  `CorruptState` for what will not clear on its own: a store that returns no payload, any other database failure, a row it cannot serve, a store the
  host does not register, a payload that does not decrypt. A name the name validator refuses is `NotFound`, without a
  repository read. The full table is in spec 188's `contracts/runtime-secret-resolution.md`. A failed result without a
  code or with a value that has no arm (undefined, or a member added later), a success without a value, and no result
  at all are reported as `CorruptState`, permanent. The runtime copies the classification into the fault unchanged, so
  this mapping is what FR-003's rule rests on; the compiler does not catch a member added later, the mapping test does.
- **Never the store's text.** `ResolvedSecret.Error` is dropped. The default resolver reports a fixed error for a failed
  read, but a replacement `ISecretValueResolver` can put a store's message there, which can name a connection target or
  a credential. What `ISecretValueResolver` throws is not caught; the activation drops it and faults the activity with
  `ResolverFailed`, permanent.

## Cross-domain contributions

This feature implements a replacement contract from another domain:

- **`IRuntimeSecretResolver`** *(Core, `Elsa.Workflows.Runtime.Core`)*: `SecretValueRuntimeResolver` resolves a
  workflow activity's secret reference over the Secrets module's `ISecretValueResolver`. Called by `ActivityActivator`,
  through `ActivitySecretInputResolver`, in `Elsa.Activities.Runtime`.
  - Known impl: `SecretValueRuntimeResolver`.
  - Catalog: [`Elsa.Workflows.Runtime/EXTENSION_POINTS.md`](../../Workflows/Runtime/EXTENSION_POINTS.md)

## Why a separate project

It is a cross-domain contribution seam, well under a hundred lines. Folding it into either side would create the
dependency it exists to avoid: the runtime would reference the Secrets module, or the Secrets module would reference the
workflow runtime. It references `Elsa.Secrets.Core` and `Elsa.Workflows.Runtime.Core` only, as
`Elsa.Secrets.Nuplane` references `Elsa.Secrets.Core` and Nuplane.
