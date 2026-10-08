# Roadmap and Acceptance Proofs

Program issue: [#2500](https://github.com/elsa-workflows/elsa-foundation/issues/2500); schedule on [Project 58](https://github.com/orgs/elsa-workflows/projects/58). Keep one active implementation leaf and a small ready buffer. Claims, dependency links, review evidence, publication identities, and current-head results belong on the owning issues. See the [decision ledger](decisions.md) before changing scope.

## Delivery hierarchy

| Epic | Features and current task |
|---|---|
| [Shell infrastructure #2506](https://github.com/elsa-workflows/elsa-foundation/issues/2506) | CShells [ownership #140](https://github.com/valence-works/cshells/issues/140)/[#144](https://github.com/valence-works/cshells/issues/144) and [catalog #141](https://github.com/valence-works/cshells/issues/141)/[#146](https://github.com/valence-works/cshells/issues/146) are submitted as PRs [#150](https://github.com/valence-works/cshells/pull/150) and [#151](https://github.com/valence-works/cshells/pull/151): both CI runs passed; ownership follow-up `4067b0f` and catalog follow-up `0b63743` address the remaining style findings and require new-head review. Both new-head CI runs passed; Greptile convergence remains required. Dependent [build lifetime #145](https://github.com/valence-works/cshells/issues/145)/[#147](https://github.com/valence-works/cshells/issues/147) and [activation #143](https://github.com/valence-works/cshells/issues/148)/[#149](https://github.com/valence-works/cshells/issues/149) remain locally qualified pending integration/publication. [Optional integration #142](https://github.com/valence-works/cshells/issues/142) awaits reviewed/published CShells prerequisites. Nuplane [removal-completion #109](https://github.com/valence-works/nuplane/issues/109) merged as PR #110 and published preview `0.0.11-preview.99`; the external package consumer qualified its callback behavior on .NET 8/9/10. No product implementation leaf is active at this checkpoint. The adapter still depends on released CShells primitives; the preview is an interim qualification input, not the final Foundation pin. |
| [Store maintenance #2507](https://github.com/elsa-workflows/elsa-foundation/issues/2507) | Nuplane [safe pruning #108](https://github.com/valence-works/nuplane/issues/108), not implementation-ready before the safety spike |
| [Foundation adoption #2508](https://github.com/elsa-workflows/elsa-foundation/issues/2508) | [Qualification #2509](https://github.com/elsa-workflows/elsa-foundation/issues/2509), including existing [observer adoption #2314](https://github.com/elsa-workflows/elsa-foundation/issues/2314) |

Native parent relations connect this hierarchy. Native dependencies block integration on ownership/catalog/build lifetime, and final qualification on integration, activation and safe pruning. The #145 build-lifetime prerequisite supplies Begin-time refresh for retained inactive changes. Later tasks are refined after their contracts stabilize. Related #2354/#2362 remain with their existing program and claims; they are not duplicate child work here.

[CShells #145](https://github.com/valence-works/cshells/issues/145) owns the generic catalog/build lifetime prerequisite for adopted [Foundation #2164](https://github.com/elsa-workflows/elsa-foundation/issues/2164), under qualification #2509. Its synchronization design was resolved in #147, now locally qualified with the combined prerequisites. A post-commit notification alone cannot protect a candidate that selected the old catalog before reaching its first initializer. Local evidence is in the [register](evidence.md); remote review/publication and consumer proof remain outstanding.

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

**Checkpoint (2026-10-08):** Nuplane #109 merged at `eb2cf6c2ee1f79dc2c45fb83cc415bbe4856d0d4`; main validation run `37777197684` passed 1,324 tests and three-OS jobs, and owner Publish run `37777044387` published all eight `0.0.11-preview.99` packages. The external consumer proof covers persisted removal-to-empty, completion ordering/isolation and a quiet second cycle. It does not prove physical pruning or CShells integration. CShells #150/#151 are under review (current heads and exact review state are in the [evidence register](evidence.md)); #147-#149 are not yet integrated. Foundation remains on its existing `.94` Nuplane package baseline pending final release adoption.

## M2 — integration, startup/readiness boundary, and Foundation adoption

Adopt Foundation #2314 for one package observer/provider implementation. Keep Nuplane core independent of CShells. Deliver generic discovery/refresh/reload support in optional `CShells.Nuplane` in the CShells repository; retain Elsa refusal/reporting adapters in `Elsa.Modularity.Nuplane`. Coordinate composition changes with claimed Foundation #2354.

**Acceptance and measurable proof:**

- The shared observer runs after Nuplane autoload/reconcile completion. Separate enablement, refresh trigger and reload policy preserve Foundation.Host's every-enabled-cycle behavior and Workbench's changed/pending refresh behavior. Committed catalog notification is used where possible; resolve inactive/failed-first-activation recovery explicitly in the adapter task.
- Nuplane #109's existing-event correction is delivered in merged main `eb2cf6c` and preview `0.0.11-preview.99`. The actual-package consumer proof establishes removal-to-empty notification, empty payload, event ordering/isolation and quiet unchanged-cycle behavior. This is notification eligibility, not duplicate #2362 unloading work; it does not prove package installation or unloading.
- A Workbench setting absent/false does not reload active shells; `true` does. Foundation.Host retains its current enabled default. Tests cover current package-change vs unchanged-cycle behavior and refusal/failure retry on a later delivered eligible completion. After last-removal failure, prove quiet empty cycles do not replay completion, failed work remains pending, explicit build recovers outstanding freshness, and a later eligible package event retries observer work; no background timer is promised.
- A package added/updated by reconcile reaches the catalog and a subsequent explicit reload; verify a shell serving the new generation while the old one drains.
- Existing host-owned Nuplane coordinator/trigger services remain shared at root; shell copies cannot enqueue on a queue no dispatcher reads.
- Startup/retry/readiness APIs remain opt-in/configurable. Demonstrate failure then recovery and preserve Workbench's separate warm-default-shell behavior.
- First deliver #148: early candidate publication remains available to routing, but `GetOrActivateAsync` waits for Commit/rollback settlement. Prove gated initial/reload success and failure, old-generation reuse before publication and independent waiter cancellation. The runner must never stop recovery based on an Active notification or provisional candidate.
- Foundation preview consumers pass the applicable modularity, readability, host process, cluster/EF, Workbench, package-lock, architecture, maps and affected end-to-end suites. Review exact current head and resulting main; record exact package versions and proof. No tests have been run for this plan.

**Current boundary:** Nuplane `.99` now removes the prior completion-notification blocker for interim adapter qualification. It does not establish the optional `CShells.Nuplane` package, the CShells serving-generation journey, or Foundation adoption. Keep the central Foundation pins/locks unchanged until final Nuplane `0.0.11` and CShells `0.0.30` release-family packages are available and their adoption gate is scheduled. Workbench automatic reload remains opt-in.

## M3 — package-store pruning safety spike, then decision

**Not ready for implementation.** First run a bounded, non-destructive spike with an isolated store and explicit generation/sibling-use instrumentation. A lock serializes mutation but does not prove a package file is no longer required.

The spike must establish exact package-generation identities and atomic revalidation at deletion, and cover: live old shell; shell build from old catalog snapshot; sibling graph sharing a package; LKG/rollback; concurrent reconcile/prune; cancellation/process crash; repeated prune. The published-package identity counterexample also requires an enforceable shared-install-root authority or complete multi-state protection, including offline active/LKG state and filesystem aliases; independent per-state locks are insufficient. Record which component owns leases and the store lock, and demonstrate that restart/recovery can still materialize every protected package. Do not touch a user package store during this spike.

**Gate:** M3 becomes ready only when all cases have bounded proof and an API/design review accepts the deletion invariant. If not proven, defer destructive pruning; deliver only a read-only inventory/retention report and documented recovery guidance if separately accepted. Actual collectible assembly unloading remains Foundation #2362's upstream Nuplane scope.

The report-only fallback does not satisfy #108 or unblock final qualification #2509 under the current delivery scope. It requires an explicit owner decision amending the program outcome and dependencies. Until then, retain the hard dependency and keep safe execution outstanding.

## M4 — integrated delivery and resulting-main proof

Use interim previews to qualify integration. After all required upstream work lands and passes its gates, publish final GitHub releases and their package families, then adopt those exact released versions in Foundation, remove only superseded local glue, and prove the composed result. Follow the [release plan](../../reports/modular-hosting-release-plan.md); current intended versions are Nuplane `0.0.11` then CShells `0.0.30`, refreshed before tagging.

**Acceptance:** focused and required CShells/Nuplane/Foundation suites pass; relevant real-process package/reload and shell overlap proofs pass; architecture and generated maps are current; project and runtime lock files match; package publication artifacts identify exact versions; current-main checks pass after integration. Report unavailable checks as unavailable, never passed. Final acceptance includes published GitHub releases, complete package families and Foundation adoption of those release versions. No production rollout is included.
