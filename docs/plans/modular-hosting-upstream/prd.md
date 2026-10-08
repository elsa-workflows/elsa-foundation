# Modular Hosting Upstream Delivery PRD

**State:** active delivery contract for Foundation issue [#2500](https://github.com/elsa-workflows/elsa-foundation/issues/2500). The [decision ledger](decisions.md) owns settled boundaries; the [roadmap](roadmap.md) owns sequencing and proof. This PRD avoids redefining framework terms; see the [root glossary](../../glossary/root.md) and [Elsa glossary](../../glossary/elsa.md).

## Problem

CShells creates separate containers for shell generations by copying host registrations. Elsa currently needs local mechanisms to preserve host-wide state across those containers and to coordinate package-loaded assemblies with shell catalog refresh/reload. Those mechanisms are specific, difficult to reuse, and some depend on internal lifecycle ordering. The Workbench and Foundation.Host also have near-duplicate Nuplane package observer/provider code with different deliberate defaults.

A second concern is package-store cleanup. A package absent from the desired set may still be needed by a loaded generation, a candidate shell build, or a sibling package graph. A process/store lock only excludes concurrent mutation; it does not prove absence of those users.

## Outcome

Publish preview CShells primitives that let a host explicitly share a selected singleton while keeping ownership/disposal at the root, and notify integrations after a catalog snapshot commits. Provide an optional adapter for Nuplane assembly discovery and reconciled catalog refresh. Adopt those APIs in Foundation and Workbench without changing their defaults. Treat Nuplane pruning as a separate gated milestone, not as implied by a successful lock acquisition.

## Requirements

1. **Explicit ownership.** Ordinary `AddSingleton` registrations remain provider-local. A host must opt in per closed service type (all its unkeyed registrations must be singleton). The root is the sole disposal owner for services constructed for host sharing; shell containers may borrow the same instance and must not dispose it. Instance descriptors retain their caller/DI ownership contract and are never disposed by a shell.
2. **Descriptor correctness.** Support type and factory registrations, caller-supplied instances, sync- and async-disposable services, multiple registrations and `IEnumerable<T>` resolution. Preserve registration order and multiplicity for the selected type. Reject mixed unkeyed lifetimes and open generics; leave keyed registrations unchanged and require separate opt-in for aliases. Define and test reload overlap while the old shell is draining and the next shell is active.
3. **Catalog signal.** Expose an observable event or subscription for a successfully committed catalog snapshot/generation. Consumers must not need periodic polling and must not observe a pre-commit catalog as committed. The signal itself does not force a shell reload.
4. **Optional integration.** Keep package-to-feature assembly discovery and post-reconcile catalog refresh in an optional CShells.Nuplane integration package in the CShells repository; the existing Elsa Nuplane module keeps Elsa policy. Nuplane core must not depend on CShells.
5. **Host policy preservation.** Workbench refreshes after actual package changes, but automatic shell reload remains opt-in and defaults to `false`. Foundation.Host retains its current auto-reload default. Preserve cancellation, retry after unsuccessful promotion, refusal reporting, and root-owned Nuplane coordinator services.
6. **Startup behavior.** Any generic startup/retry/readiness primitive must be opt-in/configurable and must leave Elsa's health contracts and retry/refusal policy in Elsa adapters. Do not duplicate existing host behavior without an explicit upstream boundary and integration proof.
7. **Safe store pruning.** M3 cannot become ready until a bounded spike demonstrates, with concurrent and failure scenarios, that an unreferenced package is not needed by active shells, shell builds using an older catalog snapshot, sibling package graphs, or last-known-good recovery. Define lock ordering and revalidation at deletion time. If proof cannot be made, defer pruning and use a documented diagnostic/restart path.
8. **Release lifecycle.** Use previews for interim integration proof. After the upstream work lands and passes its gates, publish new GitHub releases through the existing owner-repository workflows, incrementing versions only as needed according to the existing pattern. Update Foundation to the exact released package versions and repeat the host proof. Follow the [release plan](../../reports/modular-hosting-release-plan.md); production deployment remains outside scope.
9. **Candidate generation protection.** Provide generic lifecycle evidence early enough to protect the catalog used by a shell before its first initializer, through failure, promotion, drain and disposal. Adopt Foundation #2164 for the Elsa integration; a post-build generation property or post-refresh event alone must not be represented as closing that race.

## Success measures

- A shell drain cannot dispose a host-owned service; its next and overlapping generation resolves the same host instance.
- Unshared registrations stay generation-local, including their `IEnumerable<T>` position and multiplicity.
- A catalog consumer receives a committed snapshot signal without polling.
- Package changes refresh the catalog after Nuplane has loaded assemblies; explicit reload works; Workbench does not automatically reload unless configured true.
- Failed observer refresh/reload work remains pending for retry on a later delivered eligible reconciliation completion. An explicit shell reload/build can recover outstanding catalog freshness; no background retry timer is promised when empty unchanged cycles stay silent. A refused shell keeps serving its prior generation and remains visibly not promoted.
- Pruning is either proven safe under the stated boundary or explicitly deferred. A lock-only test does not count.
- Interim previews and final release packages have verified source/version identities. Final GitHub releases are published through existing workflows, and Foundation consumes the released package versions with passing adoption proof.

## Out of scope

Production deployments, wholesale migration of Elsa schema/readability or route/security policy, an all-purpose Nuplane/CShells dependency, and package-store deletion without the M3 proof gate.
