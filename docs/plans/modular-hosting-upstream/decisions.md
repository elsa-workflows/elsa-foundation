# Decision Ledger

Program: [Modular Hosting Upstream Delivery](../../program-goals/modular-hosting-upstream-delivery.md), issue [#2500](https://github.com/elsa-workflows/elsa-foundation/issues/2500). These decisions constrain implementation. Unresolved M3 safety is explicitly deferred below.

| ID | Decision | Consequence |
|---|---|---|
| D1 | Host sharing is explicit and additive, selected per closed service type for its entire unkeyed singleton registration set. Ordinary singleton registrations remain local to each service provider. | No ambient sharing rule or behavior change for existing `AddSingleton` calls. |
| D2 | Root is the sole disposal owner of an opt-in host-owned service constructed by type or factory. Shell generations borrow the same object and never dispose it. A supplied instance keeps its caller/DI ownership semantics; shells never dispose it. | Cover `IDisposable` and `IAsyncDisposable`, root shutdown, shell drain, and overlapping reload in tests. |
| D3 | Preserve descriptor multiplicity and order. `IEnumerable<T>` preserves the root registration set and order for a selected type; unselected service types retain existing provider-local behavior. Mixed unkeyed lifetimes and open generics are rejected, and keyed registrations remain unchanged. | Test duplicate service registrations and mixed shared/local enumerables; never replace an enumerable with one “last registration” shortcut. |
| D4 | Catalog notification is emitted after a successful snapshot commit and carries the committed snapshot or generation identity. Refresh notification and shell promotion are separate operations. | Integrations can remove polling while keeping reload policy host-owned. |
| D5 | Nuplane-to-CShells support is optional integration outside Nuplane core. Foundation issue #2314 is the current Elsa adoption outcome for one observer/provider. | Keep Nuplane package APIs usable without CShells and preserve an alternate assembly provider path. |
| D6 | Preserve host defaults: Workbench automatic reload is opt-in (`false` by default); Foundation.Host's current default remains enabled. | Tests assert both defaults and explicit overrides, including a failed promotion followed by retry. |
| D7 | No stable package tag or production deployment is part of this delivery. Publish preview packages through existing pipelines. | Record exact package/version/source and run consumer proof against those packages. |
| D8 | M3 actual package deletion is deferred until a bounded safety spike proves no live, candidate, sibling-graph, or LKG use at the point of deletion. A store lock alone is insufficient. | M3 cannot be marked ready or implemented as destructive cleanup before the gate in the roadmap passes. |
| D9 | Foundation retains Elsa-specific EF refusal interpretation, Attention/readiness projection, cluster schema reporting, endpoint authorization, and Studio behavior. | Upstream packages expose generic events/lifetimes; they do not import Elsa concepts. |

## Open decision gate: M3 deletion safety

**Status: unresolved; M3 deferred.** Foundation #2362 owns assembly unloading and Foundation #2354 owns host-composition extraction. This program must not duplicate either. Before any file deletion implementation is called ready, a bounded spike must establish whether Nuplane can identify an exact immutable package generation and all store consumers, and whether it can revalidate those references atomically with store mutation. It must cover an older shell generation still draining, a candidate shell built from an older catalog snapshot, a sibling package graph sharing a dependency, last-known-good state, concurrent reconcile/prune, process failure, and retry. Evidence must show deletion cannot invalidate an in-flight or recoverable host. If any case cannot be proven, keep actual deletion deferred and define only a non-destructive report/restart fallback.

Deferral keeps safe execution outstanding. A report-only fallback cannot close the pruning feature or final program qualification without an explicit owner scope amendment; the native #2509 dependency on Nuplane #108 remains intentional.
