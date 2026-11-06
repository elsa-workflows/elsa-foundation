# Jobs API extension points

The [Jobs Core catalog](../Core/EXTENSION_POINTS.md) owns durable commands, queries and meanings.
This optional module adds HTTP projection and current-user access; it adds no execution authority.

## Host composition

`AddJobsApi(Action<JobsApiOptions>?)` adds the module endpoint pipeline, capability and permission
contributions, plus request-scoped operations. `JobsApiFeature` applies the same registration and maps
`JobsApi.MapJobsApi`. Hosts configure their selected Jobs `IJobCommands` / `IJobQueries`, a normalized
Foundation Identity scheme, and a provider for `ICurrentSubjectPermissionAuthorizer` that owns current
users, roles and memberships. The unsupported identity default refuses operations.

`JobsApiOptions.PersistencePartition` is selected by the host. Tenant and subject come from one
normalized identity's exact canonical claims; the request cannot select a partition, tenant or actor.
The API references Jobs Core, API capabilities and Foundation Identity. It does not install an attempt
worker, provider or workflow model.

Pagination uses the host-owned ASP.NET `IDataProtectionProvider`, registered with `AddDataProtection`
without replacing the host's application name or key store. A versioned purpose binds encrypted provider
tokens to the trusted partition, tenant, subject, collection, exact filters and page size. Tokens are
bounded to 16,384 characters; invalid or cross-context tokens refuse the request. Provider tokens never
leave the API in cleartext, including when resource authorization removes a page's candidate rows.

## Current resource authorization

`JobApiResource` is the payload-free resource passed to the owning Identity domain's
`ICurrentSubjectPermissionAuthorizer`. Its kinds are `job`, `submission`, `schedule`, `queue` and
`gate`. The resource's tenant always matches the selected actor. Queue counts and gate collections
are independently authorized aggregate resources. Lists filter each returned resource through current
decisions; read denials are hidden as absence. A directory failure remains unavailable rather than an
empty successful list. The API rechecks current authorization on each HTTP request and before the
corresponding authority command; it stores no permission snapshot.

`JobsPermissions` and the internal `IPermissionContributor` contribution own the module vocabulary.
Fixed module routes carry one Foundation permission policy and the shared API ownership metadata.
The `elsa.api.jobs/1` capability advertises shell-relative Jobs, queues, schedules, holds, capacity and
access links using the existing capability aggregator.

## Disclosure and mutation boundary

Ordinary views omit input/result/progress data, progress messages, raw diagnostics, model continuation
addresses and acknowledgement proofs. Input and result endpoints require their separate current
payload permission as well as current resource read permission. Job detail uses an exact delivery
query; `settled` includes accepted terminal-parent proof and does not claim model consumption.

Public submissions contain a registered invocation only. A caller cannot create a trusted model
association. Schedule edits preserve any previously retained trusted association on the server.
Administrative commands pass current subject, bounded reason, original operation identity and expected
revision into Jobs Core. Provider receipts own durable idempotency and audit retention.
Valid retained schedule definitions with temporary `jobs.recurrence.unavailable` diagnostics still reach
the authorized provider command so a committed save receipt can replay; other unavailable definitions
remain blocked. New commands retain the provider's recurrence validation.

The request boundary limits wire bodies to 4 MiB, pages to 100 items and identifiers/reasons to 256
characters. Per-job hold inspection makes at most four exact-gate reads and returns each continuation;
`complete=false` means additional reasons remain to be read. It never scans every job or delivery.
