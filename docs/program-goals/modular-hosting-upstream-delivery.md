# Modular Hosting Upstream Delivery

Area: reusable modular-host infrastructure across CShells, Nuplane, and Elsa Foundation. Status: active delivery for issue [#2500](https://github.com/elsa-workflows/elsa-foundation/issues/2500); no milestone is complete. Scheduling surface: [Project 58](https://github.com/orgs/elsa-workflows/projects/58).

Stewards: Sipke and the program delivery lead. CShells [#144 ownership](https://github.com/valence-works/cshells/issues/144), [#146 catalog notifications](https://github.com/valence-works/cshells/issues/146), [#147 generation leases](https://github.com/valence-works/cshells/issues/147) and [#148 activation settlement](https://github.com/valence-works/cshells/issues/148) are locally implemented and root-reviewed. The latest combined tree passed all 707 CShells.Tests at `f42a487` in `/tmp/hosting-cshells-settlement`; external review/publication remains outstanding. The single active implementation leaf is [#149 activation runner](https://github.com/valence-works/cshells/issues/149), under Feature #143, in `/tmp/hosting-cshells-runner` from that verified base. Pushing and opening PRs await the repository-required Git/session preference answer. Exact evidence is in the [register](../plans/modular-hosting-upstream/evidence.md).

Deliver and adopt explicit host-owned services, committed CShells catalog notifications, and an optional Nuplane-to-CShells adapter. Add a Nuplane package-store pruning feature only after a bounded safety spike proves that package files are unused by active, candidate, and sibling generations. Preserve the host and Workbench policies described in [decisions](../plans/modular-hosting-upstream/decisions.md).

Locally qualified #148 prevents provisional activation success while preserving routing identity. Active #149 provides explicitly registered generic activation execution; host adapters remain adoption work. Optional Nuplane integration remains a draft design, with no second implementation leaf active.

Use the canonical [framework glossary](../glossary/root.md) and [Elsa glossary](../glossary/elsa.md) for Host, Shell, feature composition, and Nuplane terms.

## Milestones

- **M1 — CShells primitives:** explicit host-owned singleton sharing, sole disposal ownership, notifications after a feature catalog snapshot commits, and catalog protection from shell build start through disposal. Prove behavior across overlapping shell generations, reload, and teardown.
- **M2 — optional integration and host adoption:** adopt Foundation issue [#2314](https://github.com/elsa-workflows/elsa-foundation/issues/2314) for one Nuplane observer/provider implementation in optional `CShells.Nuplane`, with Elsa policy adapters kept in `Elsa.Modularity.Nuplane`. Preserve per-host reload defaults and show package delivery through shell promotion and recovery. Deliver configurable generic startup/retry/readiness support with Elsa policy adapters.
- **M3 — Nuplane pruning:** blocked from readiness until the bounded store-safety spike in the [roadmap](../plans/modular-hosting-upstream/roadmap.md) proves safe deletion under lock, LKG, in-use package graphs, and concurrent hosts. A lock by itself is insufficient.
- **M4 — Foundation adoption:** consume preview packages, remove superseded local helpers where appropriate, and prove existing behavior, architecture, package, integration, and resulting-main gates.

See the [PRD](../plans/modular-hosting-upstream/prd.md), [decision ledger](../plans/modular-hosting-upstream/decisions.md), [roadmap and acceptance proofs](../plans/modular-hosting-upstream/roadmap.md), and [baseline evidence](../plans/modular-hosting-upstream/evidence.md).

## Ownership and existing work

CShells owns shell container lifetime, host-owned service resolution, and catalog snapshot notifications. An optional `CShells.Nuplane` adapter in the CShells repository will bridge package assemblies and catalog refresh; it must remain outside Nuplane core. Nuplane owns package-store inventory and mutations. Elsa retains EF refusal interpretation, Attention, cluster readability/reporting, route policy, and Studio projections.

Adopt #2314 for shared observer/provider work. Coordinate with Foundation [#2354](https://github.com/elsa-workflows/elsa-foundation/issues/2354), which is claimed to extract host composition. Foundation [#2362](https://github.com/elsa-workflows/elsa-foundation/issues/2362) owns actual assembly collection upstream in Nuplane; this program must not duplicate unloading. Foundation [#2164](https://github.com/elsa-workflows/elsa-foundation/issues/2164) tracks the catalog/build-start evidence needed to close one readability pinning gap.

Preview publication uses each repository's existing pipeline. No stable release tag, production deployment, or mutation of a customer package store is authorized by this program.
