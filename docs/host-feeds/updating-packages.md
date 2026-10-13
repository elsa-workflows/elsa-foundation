# Updating packages on a running host

**Part of:** [Elsa.Foundation.Host package feeds](../foundation-host-feeds.md).

When a new or updated package arrives on a feed, the host **reconciles** (downloads and loads the desired package set) and then **reloads** its shells so they use it. This page explains when that happens on each host, what the reload endpoint answers when a shell refuses, and how to trigger a reconcile on demand.

| | `Elsa.Foundation.Host` | `Elsa.Workbench` |
|---|---|---|
| Reloads shells after a package change | Yes, by default (`Elsa:Shells:ReloadOnPackageChange` defaults to `true`) | No, by default (its `appsettings.json` sets it to `false`); reload by hand |

## Hot reload after a package change

`Elsa.Foundation.Host` picks up a newly reconciled package without a restart. `Elsa.Workbench` refreshes its feature
catalog when a package arrives and switches at the next shell reload (below).
`CShells.Nuplane` is configured through the host-local `NuplaneIntegrationOptionsSetup` and registered
with `WithNuplaneFeatureDiscovery()` after Nuplane's auto-loader. When an eligible reconciliation cycle
completes — not when packages land on disk, which is before their assemblies are loaded — the integration
refreshes the CShells runtime feature catalog and reloads active shells. It is on unless
`Elsa:Shells:ReloadOnPackageChange` is set to `false`. Before any shell is active, it records catalog freshness for
the next shell build and does not schedule an automatic reload. A reload failure is logged and retained work is retried
at the next eligible package completion. The integration performs no compatibility check of its own: it reloads whatever the
cycle applied, which is why the refusal above has to happen at reconciliation — a refused package is
never applied, so there is nothing for the reload to pick up.

CShells does not throw when a shell's reload fails: it keeps the previous generation active and reports the
failure in that shell's `ReloadResult.Error`. The bridge logs each such shell at `Warning` (an EF module's
refusal) or `Error` (anything else), and `POST /_module-management/reload` answers with a problem document instead
of `200`:

- `409 Conflict` when every shell that stayed on its old generation was refused by an EF module: pending migrations
  under `Migrate:Policy=Validate`, a contracting migration that may not be applied yet, or the schema finalization
  gate. The operator resolves that outside the request and repeats it.
- `500 Internal Server Error` when any failed shell was not such a refusal. Only the exception's type name is
  reported for it; its message stays in the host's log.

The body is `application/problem+json` in both cases, with the standard `type`, `title`, `status` and `detail`
(one sentence per shell) and these fields:

| Field | Meaning |
| --- | --- |
| `features` | The number of feature descriptors in the refreshed runtime catalog. |
| `reloaded` | The number of shells that reloaded. |
| `shells` | One entry per shell that stayed on its previous generation, in the order CShells reported them. |

Each entry of `shells`:

| Field | Meaning |
| --- | --- |
| `shell` | The shell's name. |
| `error` | What went wrong. For a refusal, the module's own message; otherwise the exception's type name and a pointer to the host log. |
| `code` | For a refusal, one of the codes below; `null` otherwise. |
| `module` | For a refusal, the EF module; `null` otherwise. |
| `pendingMigrations` | For a refusal, the ids of the migrations it concerns (empty for `schema-activation-refused`); `null` otherwise. |
| `command` | For a refusal, the exact command that resolves it, when there is one; `null` otherwise. |

`code` values:

- `pending-migrations`: the module's database has migrations that are not applied. `command` applies them.
- `contracting-migration-refused`: the pending batch holds a contracting migration that may not be applied yet. `error`
  says which and what it waits for; there is no `command`.
- `schema-activation-refused`: the schema finalization gate refuses to activate the module. `error` names the remedy;
  there is no `command`.

For `pending-migrations` the command is runnable as it stands, with `--host` the directory of the running host (the one
holding its build output and its `.nuplane` state, not its content root), which is where the tool reads the package set
the host last reconciled:

```
dotnet elsa persistence apply --host "<host directory>" --modules <module> --provider <p> --connection-env ELSA_EF_CONNECTION
```

The host recognises the refusal by `IEfModuleRefusal` (`Elsa.Persistence.Schema`, a shared assembly) and names no EF
type: it carries and shares `Elsa.Persistence.EntityFramework`, which the module binds, so the tool finds the module
through the host's own copy in the deployed directory. A module whose exception was built against a private copy of `Elsa.Persistence.Schema` too is
recognised by the full name of the interface it implements. Once the command has run, the same `POST` answers `200`
and the shell's generation advances, with no restart.

`Elsa.Workbench` refreshes its feature catalog when a package arrives, and its shells switch at the next shell reload.
The same `CShells.Nuplane` integration uses the host-local Workbench `NuplaneIntegrationOptionsSetup` and is registered
after Nuplane's auto-loader so that it runs once the cycle's assemblies are loaded. After an eligible cycle that added,
updated or removed a package, it refreshes the CShells runtime feature catalog when a shell is active; before the first
activation it records the catalog work for the next shell build. A cycle that changed nothing is skipped, and a change it
could not act on (the refresh failed, or a reload it started left a shell on its previous generation) is retried at
the next eligible package completion. The running shells keep the feature set they were built with until a shell is reloaded, which composes the
refreshed catalog:

- by hand, with `POST /_admin/shells/reload/{name}` or `POST /_admin/shells/reload-all` and the
  `X-Elsa-Module-Management-Key` header;
- by the observer itself, after the cycle, when `Elsa:Shells:ReloadOnPackageChange` is `true`. The Workbench's
  `appsettings.json` sets it to `false`, which is also the Workbench fallback when it is unset; on
  `Elsa.Foundation.Host` the option defaults to `true`. Changing the key alone schedules nothing: the profile is captured
  on the next eligible package completion. A shell whose reload failed keeps its previous generation, and the
  integration logs it as Foundation.Host's does: at `Warning` for an EF module's refusal, with its command and the
  Workbench's directory as `--host`, and at `Error` for anything else.

The `/_elsa/module-management` upload and reconcile endpoints answer `"RequiresReload": true` for the same reason: the
package is reconciled and loaded and the catalog refreshed, but no shell has switched yet. The host-level
`IShellReloader` is `NullShellReloader` (`Essentials/apps/Elsa.Workbench/Modularity/NullShellReloader.cs`), a no-op that
reports zero shells reloaded. The one exception is a shell that enables the `ModularityApi` feature: it replaces
`NullShellReloader` inside that shell with a reloader that does reload it, so a feature change applied through that
shell's module API also refreshes the feature catalog and rebuilds that one shell. That is a side effect of applying
feature configuration, not a response to reconciliation.

After an in-place upgrade, the previous release of a package-loaded activity stays loaded, and a reloaded shell registers
the class of the release it composes under the activity's alias; see
[Activity versions after an in-place upgrade](../../Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/README.md#activity-versions-after-an-in-place-upgrade)
for what that means for a workflow pinned to an earlier version.
## Reconciling on demand

`POST /_module-management/reconcile` (with `Elsa:ModuleManagement:Enabled` and the `X-Elsa-Module-Management-Key`
header, like `reload`) runs one reconcile cycle now, the same cycle the folder watcher and the poll interval run, and
answers once it has finished. A wrong or missing key is answered `401`.

Whether a package the cycle added is live in the running shells when the request returns depends on the hot-reload
integration, described in the section [Hot reload after a package change](#hot-reload-after-a-package-change): it reloads active
shells at the end of the cycle only when `Elsa:Shells:ReloadOnPackageChange` is on and a shell is already active. With it
off, an active shell keeps the feature set it was built with until `POST /_module-management/reload` (or a restart). Before
the first activation, the next shell build consumes the pending catalog freshness without an automatic reload. A shell
whose reload was refused after the cycle is not reported by this request: the host logs it, and `reload` answers it
with the `409` documented in that same section.

The answer carries Nuplane's `ManualReconcileOutcome`, with its code by name. The status says whether the cycle ran:

| Outcome | Status | Meaning |
| --- | --- | --- |
| `Completed` | `200` | The cycle ran to its end. `runResult.skipped` is `false`. |
| `Accepted` | `200` | Nuplane's code for a cycle still in progress. This request waits for the cycle, so the pinned Nuplane does not produce it here. |
| `Rejected` | `409` | The cycle did not run, because another cycle is already running in this host (`single-flight-active`) or another process owns the store (`store-lock-unavailable`). Repeat the request once that has cleared. |
| `Unavailable` | `503` | The reconcile service failed to run the cycle. `reasonCode` is `reconcile-service-failed`; the failure's message is logged at `Error` with the `correlationId`, never returned, because it can hold paths, feed addresses or connection details. |
| any other code | `500` | A Nuplane newer than this host's reported an outcome code it does not map, so it cannot say whether the cycle ran. `outcomeCode` is the number, `reasonCode` is `reconcile-outcome-unrecognized`, and the host logs it at `Error` with the `correlationId`. |

A `200` is the outcome itself, in camelCase JSON:

```json
{
  "outcomeCode": "Completed",
  "correlationId": "6a1d09e5dc654ded863555321a055673",
  "reasonCode": null,
  "runResult": {
    "skipped": false,
    "changeSet": {
      "added": [ { "id": "Acme.Widgets", "version": "2.0.0", "feedName": "local-packages", "sourceName": "local-packages", "installedAt": "2026-09-30T10:15:59.101+00:00" } ],
      "updated": [],
      "removed": [],
      "correlationId": "6a1d09e5dc654ded863555321a055673",
      "timestamp": "2026-09-30T10:15:59.419047+00:00"
    },
    "failedPackages": [],
    "isDegraded": false,
    "skipReason": 0
  }
}
```

| Field | Meaning |
| --- | --- |
| `outcomeCode` | The outcome's name: `Completed`, `Accepted`, `Rejected` or `Unavailable`; the number for a code this host does not know. |
| `correlationId` | Ties the request to the cycle's log lines. |
| `reasonCode` | For a rejected outcome, `single-flight-active` or `store-lock-unavailable`; for an unavailable one, `reconcile-service-failed`; `null` for the others. |
| `runResult` | The cycle's result. `skipped` is `true` when the cycle did nothing, and `skipReason` says why: `0` not skipped, `1` another cycle was already running in this host, `2` another process owns the store. |
| `runResult.changeSet` | The packages the cycle `added` and `updated` (id, version, feed, source and when it was installed; not where on the host's disk), and the ids it `removed`. An empty feed, or one that changed nothing, has all three empty. Packages acquired as dependencies or by capability selection are listed with the ones asked for; `sourceName` says which. |
| `runResult.failedPackages` / `isDegraded` | The ids of packages that failed to load, and whether the cycle finished degraded. |

A `409`, `503` or `500` is an `application/problem+json` document, as for a refused `reload`, with the standard `type`, `title`,
`status` and `detail` and these fields: `outcomeCode` (`Rejected`, `Unavailable` or the unknown code's number), `reasonCode` (as above) and
`correlationId`.

The request runs through the host's own Nuplane operations, not the ones its shell holds. CShells copies every host
registration into every shell, and a registration made by instance is shared by all of them, but one made by type or by
factory is copied and instantiated again, so a shell has its own instance of Nuplane's trigger queue, and a reconcile
queued on that copy is one no dispatcher reads (#2159). The host takes the operations from its root provider for that
reason, and its composition shares the operations, the coordinator and the trigger ingress with every shell, so a shell
that resolves them gets the host's instance. `HostOwnedServicesAreSharedWithShellsTests` holds that for every Elsa and Nuplane
singleton the host registers: it builds both hosts, activates a shell, and fails on any that the shell holds a second
instance of and that the test does not list with a reason a shell's own copy is right or unreachable. `FoundationHostReconcileTests` pins the answers on the built host, and
`FoundationHostReconcileEndpointTests` the statuses of each outcome.

---

All pages: [Overview](../foundation-host-feeds.md) · [Point the host at a feed](feeds.md) · [Pinning and closures](pinning-and-closures.md) · [Updating packages](updating-packages.md) · [When things fail](failure-modes.md) · [Clustering](clustering.md)
