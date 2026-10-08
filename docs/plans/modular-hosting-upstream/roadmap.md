# Roadmap and Acceptance Proofs

Program issue: [#2500](https://github.com/elsa-workflows/elsa-foundation/issues/2500); schedule on [Project 58](https://github.com/orgs/elsa-workflows/projects/58). Keep one active implementation leaf and a small ready buffer. Claims, dependency links, review evidence, publication identities, and current-head results belong on the owning issues. See the [decision ledger](decisions.md) before changing scope.

## Delivery hierarchy

| Epic | Features and current task |
|---|---|
| [Shell infrastructure #2506](https://github.com/elsa-workflows/elsa-foundation/issues/2506) | CShells ownership #144 and catalog #146 are Done/Passed from preview.161; generation primitive #147 is Done/Passed after PR #152 merged as `0f25bba`, Packages `37795690347` published all nine preview.162 packages and the public generation consumer passed on .NET 8/9/10. The separate Foundation #2164 proof keeps Feature #145 open. Nuplane #109 remains Done/Passed from preview.99. #148 and #149 are Done/Passed after normal merges and public preview.163/.165 qualification on all three runtimes. Optional adapter #156 and Feature #142 are Done/Passed after PR #157 and complete public `.166` qualification on .NET 8/9/10. CShells [#158](https://github.com/valence-works/cshells/issues/158) is the single active implementation leaf under Spec Kit 022, delivering nonactivating settled-current observation for readiness. Foundation #2164 source preparation is published at `c021fa591` with 110 tests/four mutation bites; stable actual-host acceptance remains pending and the leaf is Blocked on release/adoption. No milestone is declared complete. |
| [Store maintenance #2507](https://github.com/elsa-workflows/elsa-foundation/issues/2507) | Nuplane [safe pruning #108](https://github.com/valence-works/nuplane/issues/108), not implementation-ready before the safety spike |
| [Foundation adoption #2508](https://github.com/elsa-workflows/elsa-foundation/issues/2508) | [Qualification #2509](https://github.com/elsa-workflows/elsa-foundation/issues/2509), including existing [observer adoption #2314](https://github.com/elsa-workflows/elsa-foundation/issues/2314) |

Native parent relations connect this hierarchy. Native dependencies block integration on ownership/catalog/build lifetime, and final qualification on integration, activation and safe pruning. The #145 build-lifetime prerequisite supplies Begin-time refresh for retained inactive changes. Later tasks are refined after their contracts stabilize. Related #2354/#2362 remain with their existing program and claims; they are not duplicate child work here.

[CShells #145](https://github.com/valence-works/cshells/issues/145) owns the generic catalog/build lifetime prerequisite for adopted [Foundation #2164](https://github.com/elsa-workflows/elsa-foundation/issues/2164), under qualification #2509. Its synchronization design was delivered in #147 and qualified from public preview.162. A post-commit notification alone cannot protect a candidate that selected the old catalog before reaching its first initializer. Upstream primitive evidence is in the [register](evidence.md); Feature #145's Foundation #2164 consumer proof remains mandatory and unfulfilled under final adoption. Closing the upstream task does not close the Feature.

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
- A deterministic race holds a candidate before its first initializer while a new catalog commits and other old pins release. The candidate's exact old snapshot stays protected until build failure/cancellation or actual shell disposal. Verify successful promotion and overlapping drain, and define callback ordering without arbitrary subscriber work under a catalog lock.
- Run focused CShells tests, full required owner-repository gates, exact-head review, publish the preview, and record a consumer identity plus the executable overlapping-generation proof. No result is implied here.

**Checkpoint (2026-10-08):** ownership/catalog passed their public preview.161 consumer. The generation primitive merged as `0f25bba`; all nine preview.162 packages match the owner pipeline and the public Feedz consumer passed six generation-lease groups on .NET 8/9/10. The merge used the explicitly recorded external-review fallback; Greptile has no fresh approval. Source, review and runtime proof are in the [evidence register](evidence.md). Feature #145 remains open for Foundation #2164 consumer proof. No milestone is declared complete. #148/#149 and optional adapter #156 are delivered and publicly qualified. Foundation #2164 is preparing its consumer against public `.166`/`.99`; stable actual-host proof remains required. Foundation pins remain unchanged until final release-family adoption.

## M2 — integration, startup/readiness boundary, and Foundation adoption

Adopt Foundation #2314 for one package observer/provider implementation. Keep Nuplane core independent of CShells. Deliver generic discovery/refresh/reload support in optional `CShells.Nuplane` in the CShells repository; retain Elsa refusal/reporting adapters in `Elsa.Modularity.Nuplane`. Coordinate composition changes with claimed Foundation #2354.

**Acceptance and measurable proof:**

- The shared observer runs after Nuplane autoload/reconcile completion. Separate enablement, refresh trigger and reload policy preserve Foundation.Host's every-enabled-cycle behavior and Workbench's changed/pending refresh behavior. Committed catalog notification is used where possible; resolve inactive/failed-first-activation recovery explicitly in the adapter task.
- Nuplane #109's existing-event correction is delivered in merged main `eb2cf6c` and preview `0.0.11-preview.99`. The actual-package consumer proof establishes removal-to-empty notification, empty payload, event ordering/isolation and quiet unchanged-cycle behavior. This is notification eligibility, not duplicate #2362 unloading work; it does not prove package installation or unloading.
- A Workbench setting absent/false does not reload active shells; `true` does. Foundation.Host retains its current enabled default. Tests cover current package-change vs unchanged-cycle behavior and refusal/failure retry on a later delivered eligible completion. After last-removal failure, prove quiet empty cycles do not replay completion, failed work remains pending, explicit build recovers outstanding freshness, and a later eligible package event retries observer work; no background timer is promised.
- A package added/updated by reconcile reaches the catalog and a subsequent explicit reload; verify a shell serving the new generation while the old one drains.
- Existing host-owned Nuplane coordinator/trigger services remain shared at root; shell copies cannot enqueue on a queue no dispatcher reads.
- Startup/retry/readiness APIs remain opt-in/configurable. Demonstrate failure then recovery and preserve Workbench's separate warm-default-shell behavior.
- #148 is delivered and publicly qualified: early candidate publication remains available to routing, but `GetOrActivateAsync` waits for Commit/rollback settlement. Prove gated initial/reload success and failure, old-generation reuse before publication and independent waiter cancellation. The delivered #149 runner also passed public preview.165 proof for lazy opt-in, serial initial work, retries after InitialPass, cancellation, committed external satisfaction and Stop fencing. Actual host adoption remains required; it must never stop recovery based on an Active notification or provisional candidate.
- [#158](https://github.com/valence-works/cshells/issues/158) closes a newly reproduced observation gap: the delivered internal settlement marker and runner cannot by themselves certify later current readiness without activation. The optional query must reject provisional replacement publication, return the exact settled current generation after success or rollback, and remain independent of blocked activation callbacks. It grants no use lease and does not change routing visibility or host readiness policy. Complete callback errors remain diagnostic-only. Required public consumer proof spans .NET 8/9/10.
- Foundation preview consumers must pass the applicable modularity, readability, host process, cluster/EF, Workbench, package-lock, architecture, maps and affected end-to-end suites. Review exact current head and resulting main; record exact package versions and proof. The Foundation runtime/adoption consumer remains outstanding.

**Current boundary:** Nuplane `.99` and CShells `.166` provide the delivered primitives and optional `CShells.Nuplane` adapter. The actual public-package adapter consumer proves package-to-serving-generation behavior and removal to zero; it does not prove Foundation host adoption, readability, unloading or safe pruning. Foundation #2164 prepares readability separately in an isolated public-preview copy. Keep committed Foundation pins/locks unchanged until final Nuplane `0.0.11` and CShells `0.0.30` release-family packages are available and their adoption gate is scheduled. Workbench automatic reload remains opt-in.

## M3 — package-store pruning safety spike, then decision

**Not ready for implementation.** First run a bounded, non-destructive spike with an isolated store and explicit generation/sibling-use instrumentation. A lock serializes mutation but does not prove a package file is no longer required.

The spike must establish exact package-generation identities and atomic revalidation at deletion, and cover: live old shell; shell build from old catalog snapshot; sibling graph sharing a package; LKG/rollback; concurrent reconcile/prune; cancellation/process crash; repeated prune. The published-package identity counterexample also requires an enforceable shared-install-root authority or complete multi-state protection, including offline active/LKG state and filesystem aliases; independent per-state locks are insufficient. Record which component owns leases and the store lock, and demonstrate that restart/recovery can still materialize every protected package. Do not touch a user package store during this spike.

**Gate:** M3 becomes ready only when all cases have bounded proof and an API/design review accepts the deletion invariant. If not proven, defer destructive pruning; deliver only a read-only inventory/retention report and documented recovery guidance if separately accepted. Actual collectible assembly unloading remains Foundation #2362's upstream Nuplane scope.

The report-only fallback does not satisfy #108 or unblock final qualification #2509 under the current delivery scope. It requires an explicit owner decision amending the program outcome and dependencies. Until then, retain the hard dependency and keep safe execution outstanding.

## M4 — integrated delivery and resulting-main proof

Use interim previews to qualify integration. After all required upstream work lands and passes its gates, publish final GitHub releases and their package families, then adopt those exact released versions in Foundation, remove only superseded local glue, and prove the composed result. Follow the [release plan](../../reports/modular-hosting-release-plan.md); current intended versions are Nuplane `0.0.11` then CShells `0.0.30`, refreshed before tagging.

**Acceptance:** focused and required CShells/Nuplane/Foundation suites pass; relevant real-process package/reload and shell overlap proofs pass; architecture and generated maps are current; project and runtime lock files match; package publication artifacts identify exact versions; current-main checks pass after integration. Report unavailable checks as unavailable, never passed. Final acceptance includes published GitHub releases, complete package families and Foundation adoption of those release versions. No production rollout is included.
