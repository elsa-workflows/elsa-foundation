# Roadmap and Acceptance Proofs

Program issue: [#2500](https://github.com/elsa-workflows/elsa-foundation/issues/2500); schedule on [Project 58](https://github.com/orgs/elsa-workflows/projects/58). Keep one active implementation leaf and a small ready buffer. Claims, dependency links, review evidence, publication identities, and current-head results belong on the owning issues. See the [decision ledger](decisions.md) before changing scope.

## Delivery hierarchy

| Epic | Features and current task |
|---|---|
| [Shell infrastructure #2506](https://github.com/elsa-workflows/elsa-foundation/issues/2506) | CShells [ownership #140](https://github.com/valence-works/cshells/issues/140) with active [task #144](https://github.com/valence-works/cshells/issues/144); [catalog #141](https://github.com/valence-works/cshells/issues/141); [optional integration #142](https://github.com/valence-works/cshells/issues/142); [activation #143](https://github.com/valence-works/cshells/issues/143) |
| [Store maintenance #2507](https://github.com/elsa-workflows/elsa-foundation/issues/2507) | Nuplane [safe pruning #108](https://github.com/valence-works/nuplane/issues/108), not implementation-ready before the safety spike |
| [Foundation adoption #2508](https://github.com/elsa-workflows/elsa-foundation/issues/2508) | [Qualification #2509](https://github.com/elsa-workflows/elsa-foundation/issues/2509), including existing [observer adoption #2314](https://github.com/elsa-workflows/elsa-foundation/issues/2314) |

Native parent relations connect this hierarchy. Native dependencies block integration on ownership/catalog, and final qualification on integration, activation and safe pruning. Later tasks are refined after their contracts stabilize. Related #2354/#2362 remain with their existing program and claims; they are not duplicate child work here.

## M0 — ownership and API design

Record the CShells API and ownership model before coding. Reconcile the exact descriptor/`IEnumerable<T>` behavior, root disposal ownership, committed catalog notification timing, cancellation, and overlapping-generation semantics. Inspect the current upstream issue/PR state before claiming work. No implementation, review, or publication is claimed by these drafts.

**Ready when:** acceptance cases are linked to the CShells-owned issue and its tests; issue dependencies and cross-repository ownership are explicit.

## M1 — CShells primitives and preview

Implement explicit host-owned service descriptors and committed catalog notifications in CShells. Publish a preview using the CShells pipeline.

**Acceptance and measurable proof:**

- Type and factory descriptors resolve one root-owned instance from root and all active shell generations.
- Caller-supplied instance descriptors preserve their documented ownership; disposing a shell never disposes that instance.
- Root shutdown disposes factory/type-created shared objects exactly once, including async-only disposal; shell disposal does not.
- Disposable and async-disposable type/factory services remain usable through shell drain and are disposed only by the owner.
- Ordinary singleton descriptors remain provider-local by default.
- Multiple registrations and `IEnumerable<T>` preserve descriptor order and multiplicity for the selected unkeyed singleton set; unselected service types remain provider-local.
- Exercise overlapping reload: old generation drains after the next generation starts; neither lifetime is truncated and neither shell can dispose root-owned services.
- Catalog refresh emits one committed-snapshot notification after commit; cancellation/failure emits no success notification; subscribers can read the announced generation without polling.
- Run focused CShells tests, full required owner-repository gates, exact-head review, publish the preview, and record a consumer identity plus the executable overlapping-generation proof. No result is implied here.

## M2 — integration, startup/readiness boundary, and Foundation adoption

Adopt Foundation #2314 for one package observer/provider implementation. Keep Nuplane core independent of CShells. Deliver generic discovery/refresh/reload support in optional `CShells.Nuplane` in the CShells repository; retain Elsa refusal/reporting adapters in `Elsa.Modularity.Nuplane`. Coordinate composition changes with claimed Foundation #2354.

**Acceptance and measurable proof:**

- The shared observer runs after Nuplane autoload/reconcile completion and refreshes only after package changes; committed catalog notification is used where possible.
- A Workbench setting absent/false does not reload active shells; `true` does. Foundation.Host retains its current enabled default. Tests cover current package-change vs unchanged-cycle behavior and refusal/failure retry.
- A package added/updated by reconcile reaches the catalog and a subsequent explicit reload; verify a shell serving the new generation while the old one drains.
- Existing host-owned Nuplane coordinator/trigger services remain shared at root; shell copies cannot enqueue on a queue no dispatcher reads.
- Startup/retry/readiness APIs remain opt-in/configurable. Demonstrate failure then recovery and preserve Workbench's separate warm-default-shell behavior.
- Foundation preview consumers pass the applicable modularity, readability, host process, cluster/EF, Workbench, package-lock, architecture, maps and affected end-to-end suites. Review exact current head and resulting main; record exact package versions and proof. No tests have been run for this plan.

## M3 — package-store pruning safety spike, then decision

**Not ready for implementation.** First run a bounded, non-destructive spike with an isolated store and explicit generation/sibling-use instrumentation. A lock serializes mutation but does not prove a package file is no longer required.

The spike must establish exact package-generation identities and atomic revalidation at deletion, and cover: live old shell; shell build from old catalog snapshot; sibling graph sharing a package; LKG/rollback; concurrent reconcile/prune; cancellation/process crash; repeated prune. Record which component owns leases and the store lock, and demonstrate that restart/recovery can still materialize every protected package. Do not touch a user package store during this spike.

**Gate:** M3 becomes ready only when all cases have bounded proof and an API/design review accepts the deletion invariant. If not proven, defer destructive pruning; deliver only a read-only inventory/retention report and documented recovery guidance if separately accepted. Actual collectible assembly unloading remains Foundation #2362's upstream Nuplane scope.

The report-only fallback does not satisfy #108 or unblock final qualification #2509 under the current delivery scope. It requires an explicit owner decision amending the program outcome and dependencies. Until then, retain the hard dependency and keep safe execution outstanding.

## M4 — integrated delivery and resulting-main proof

After previews are published, adopt exact versions, remove only superseded local glue, and prove the composed result.

**Acceptance:** focused and required CShells/Nuplane/Foundation suites pass; relevant real-process package/reload and shell overlap proofs pass; architecture and generated maps are current; project and runtime lock files match; package publication artifacts identify exact versions; current-main checks pass after integration. Report unavailable checks as unavailable, never passed. No stable tag or production rollout is included.
