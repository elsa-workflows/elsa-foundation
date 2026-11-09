---
status: accepted
date: 2026-11-04
---

# Agent-owned content uses shared revisions and provider-derived activities

Status: Accepted for implementation of [#3790](https://github.com/elsa-workflows/elsa-foundation/issues/3790) on Sipke’s explicit instruction, 4 November 2026, after PR #3783 merged as `4043cbfb1`. The owner authorized scoped implementation through reviewed PRs, coordination with existing work and waiting for any unfinished foundational capability owned by #2892. This records that action-specific owner authority; it does not assert previously requested reviewer approvals or implementation acceptance. Amended 2026-11-09 (package roots and clean-break scope, below). Pre-GA database recreation is permitted for planned breaking delivery, but no current database reset is part of #3790.

## Context

Spec 1179 currently authors a user-defined Agent as a generic activity definition. The provider compiles its contract and manifest into an immutable execution template. This reuses publication/runtime behavior but gives Activities ownership of Agent source. Elsa 3's provider separation can be retained while freezing behavior at publication.

First-class source ownership must not introduce another independently implemented draft/version subsystem. Workflows and reusable activities already have separate draft/version entities and promotion commands, while sharing activity publication contracts. Agent identity, editable state, reviewed input and published behavior are necessary responsibilities; separate Agent-specific tables, repositories and lifecycle engines for each are not.

Orchard Core and Aster demonstrate reusable revision mechanics with different editing semantics. [Research R10](../../specs/1179-specialist-agents/research.md#r10--shared-revision-management-orchard-core-and-aster) compares them. The [revision contract](../../specs/1179-specialist-agents/contracts/revision-management.md) maps both Workflow and Agent operations, records the extraction decision and gives a failed-publication recovery example.

## Decision

### D1 — Agent is the canonical term

Use Agent, Agent definition, Agent revision, Agent release/version, Agent conversation and Agent turn. Specialist describes a role, not a separate entity kind. Weaver remains the Studio assistant experience; a workflow execution actor remains a runtime mechanism. Meanings live in the [glossary](../glossary/elsa.md).

Pre-GA implementation may remove or rename `Specialist*`, `elsa.specialist`, schemas, APIs and packages in a coordinated clean break; this design does not require compatibility aliases or old readers. Concrete naming/package changes require implementation review and dependency/closure qualification. Keep the pinned `Elsa.Agent` root; do not introduce `Elsa.Agents`. No code or identifier is renamed by this PR.

### D2 — Domains own typed content; revision mechanics are shared

Agent owns instructions, model reference, typed I/O contract, policy, validation and permissions. Workflow owns its graph, semantic mutation/diff, validation and permissions. Both compose one reusable capability for scoped source identity, editable working copies with compare-and-swap, immutable revision snapshots, history/provenance and retention pins. Domain-facing views may still say draft or version; they must not instantiate duplicate persistence/lifecycle engines behind those names.

Prefer reusable mutable working copies plus immutable snapshots at test/review/publication boundaries. Saving a working copy increments its concurrency revision; it does not allocate a public SemVer or require append-only history of every keystroke. An exact snapshot is stored once and referenced by test/publication operations. A candidate is that snapshot in a publication operation, not another Agent-specific content aggregate. Shared revision numbers, public release versions, schema versions and compilation hashes have distinct meanings.

The implementation plan must demonstrate shared mechanics through both Workflow and Agent adapters. Preserve domain-specific commands and Workflow's diff-based mutation; do not replace them with unrestricted generic JSON CRUD. Assess reusable-activity authoring as a further existing consumer during extraction. New activity-producing domains reuse the capability and publisher with their own typed adapter/compiler.

### D3 — Extract a small capability; retain domain boundaries

Select focused extraction of existing Elsa mechanics rather than adoption of a full content-management framework in this unit. Reuse typed payload serialization, source identity, atomic CAS/snapshot operations and publication coordination through owning Core ports. Feature/persistence implementations supply real stores and atomic guarantees. The revision capability has no knowledge of Agent, Workflow graphs, Activities Design DTOs, inference, executable templates or SemVer compatibility policy. Agent and Workflow Core do not depend on one another or concrete persistence implementations.

The revision contract owns proposed responsibilities; logical names are not frozen signatures or permission to create a package hierarchy. A common base class or repository wrapper over duplicated engines does not satisfy reuse. Aster remains a credible alternative, but adoption would additionally choose its resource/aspect/facet and activation semantics. That broader decision needs its own evidence; this proposal neither depends on Aster nor rules it out later.

### D4 — Provider derives one workflow representation

One Agent-owned typed contract generates activity ports and model JSON schemas. Generated content is read-only through generic activity authoring. The Agent collection reads authorized sources/working copies, including unpublished Agents; the native/BPMN picker reads the existing activity catalog. Agent commands own lifecycle intent and delegate shared publication/lifecycle primitives; generic activity management cannot bypass that authority. Configuration-owned content is read-only; an authorized fork creates a new source identity and provenance.

The source-owned bridge currently uses `CompileSourceOwnedRoot`; Agent compilation requires the registered provider compiler's boundary and model-turn child. Extend shared evaluation/publication to use the same provider compiler and preflight for interactive and source-owned input. A catalog row alone is not an executable publication. No second catalog, Agent publisher or runtime is introduced.

### D5 — Publication operates on a retained revision

Persist an immutable source snapshot, then a shared publication operation referencing it, the proposed SemVer, scope/authority, expected heads, compiler/profile and exact review evidence. The operation does not store a second independently authored content copy. Validation, domain checks and compilation precede executable publication; publication makes no inference request.

Review tokens bind that exact revision and environment. Final admission reserves source authority and publication heads through CAS. Ownership/lifecycle/cancellation races serialize against that reservation or return conflict/pending; later draft saves cannot change its snapshot. Shared publication commits the immutable catalog version, template/reference and receipt. That receipt is the sole successful executable-publication authority. Source-side release/status indexes are repairable associations to it, not a second publication engine.

Retries retain operation identity and exact material. A lost reply or failed index update is reconciled from the original receipt. Unknown outcomes remain pending; they cannot release/reuse a version claim or cause a different publication. Definite validation failure leaves the revision unpublished; editing creates a new snapshot and reviewed operation. The [shared protocol](../../specs/1179-specialist-agents/contracts/revision-management.md#publication-and-failed-publication-recovery) applies to both domains without assuming a distributed transaction.

### D6 — Release selection and runtime stay exact

An Agent release such as `1.2.0` associates one immutable source revision with its confirmed primary activity publication. Its activity uses the same user-facing release version; compiler and template hashes remain internal identities. New compiler installations cannot silently regenerate a published release. Recommendation/activation selects already published material; it cannot substitute for compilation or make an unpublished revision runnable. Retirement and execution admission remain separate domain gates.

Workflow publication pins selected retained templates into its runnable artifact. Runtime uses that artifact and deployed interpreters without Agent/Workflow authoring-store or Design/Publishing reads. Connection authorization and Secrets resolve operationally; credentials never enter source snapshots or artifact hashes. A pinned model does not imply deterministic remote output.

Reuse native execution, the background model-turn worker, journal, request reservations and accepted-versus-applied settlement. Playground tests use an exact snapshot and nonpublished execution reference through the same admitted path. A future direct invocation API must use deployed immutable material and the same runtime semantics; it is not delivered here. Inspection leads with business inputs/results and exact release, retaining internal-step diagnostics.

### D7 — Pre-GA clean breaks replace compatibility machinery

The owner's revised instruction permits database recreation when introducing breaking changes. Remove legacy association import, old ID/hash preservation across reset, mixed-binary ownership transfer, migration/backfill, compatibility aliases/readers and old-schema rollback from this amendment's requirements. The earlier preservation instruction is superseded. A coordinated implementation may rename schemas and reset affected source, catalog, artifact and journal stores together; it must declare the reset scope, deployment boundary, affected configuration/packages and recreation/republish procedure.

This permission is not an instruction to delete a current database. The design PR changes no hosts, stores or running demos. A future reset procedure must stop/drain affected hosts, prevent old binaries reopening new stores, and require explicit recreation/republish before admission. Unsupported profiles fail readiness. Cross-break rolling upgrades or historical data recovery are not promised. Within any admitted deployment, immutable version binding, exact scope, source authority, crash-safe publication and accepted-turn replay remain mandatory; resetting data must never become an ordinary retry mechanism.

## Alternatives and consequences

| Alternative | Benefit | Cost / disposition |
|---|---|---|
| Activity-owned Agent source | Reuses current source infrastructure | Domain authority remains generic activity authoring; not the target |
| Separate domain-specific draft/candidate/version engines | Local implementation freedom | Repeats concurrency/history/recovery machinery; rejected |
| Shared working copies/snapshots plus typed domain adapters | Reuses mechanics and keeps domain rules explicit | Selected; requires proving real sharing with Workflow and Agent |
| Append-only snapshot on every save | Uniform immutable source history, as in Aster | Viable alternative; save/history/retention behavior changes are unnecessary for this extraction |
| Adopt Orchard Core/Aster wholesale | Existing general content/resource services | Broader storage/schema/composition/activation choice; not selected in this unit |
| Live latest-source lookup or separate Agent execution engine | Small integration or infrastructure independence | Breaks frozen selection or duplicates execution ownership; rejected |

The target reduces repeated authoring mechanics but retains necessary source-to-publication recovery. It does not claim that an abstraction eliminates cross-store failures. Shared revision storage is not a universal callable-resource runtime. No new independent program, project hierarchy or framework dependency is admitted by this design.

## Agreement, propagation and implementation hold

This ADR, the [revision contract](../../specs/1179-specialist-agents/contracts/revision-management.md) and [spec 1179](../../specs/1179-specialist-agents/spec.md) are proposed together. Workflow mappings describe required future adapters, not an amendment already implemented or approved across other specs. Prior delivery receipts remain historical evidence, not acceptance of this model. Future delivery must reconcile owning Workflow/activity specs and dependencies before changing them.

PR #3783 merged and the owner subsequently authorized #3790 implementation through reviewed PRs. T064–T071 remain delivery tasks; this decision is not a claim they are implemented. Reconcile owning Workflow/activity specifications as their code is adapted. Missing foundational execution work under #2892 is a wait condition, not authority to duplicate its runtime or take over its active lane. Production changes require normal scoped review/test/CI evidence. No database reset, release or deployment is implied. No constitution text is amended or provisional versioning/Model X provision ratified.

## Amendment — 2026-11-09, accepted

**Decision (owner, Sipke Schoorstra, in session; recorded on [#4027](https://github.com/elsa-workflows/elsa-foundation/issues/4027) and [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892)): user-defined agents take the `Elsa.Agent` root; Weaver moves to `Elsa.Weaver`.** D1 names Agent as the canonical term and keeps the pinned `Elsa.Agent` root without introducing `Elsa.Agents`. This amendment fixes what that root holds: the packages, namespaces, shell feature keys, routes, tables and persisted identifiers of user-defined agents, today the `Elsa.Agent.Specialists.*` packages. The Studio assistant layer, today the eight `Elsa.Agent.*` packages, moves to `Elsa.Weaver.*` (features `Weaver*`, routes `weaver/...`, tables `elsa_weaver_*`), so the two layers no longer share a root and "agent" without qualification means the thing users author.

The rename is the pre-GA clean break D1 and D7 already permit, carried out in the order the owner chose: first Weaver to `Elsa.Weaver.*`, then the agent runtime to `Elsa.Agent.*`, then the words in specs, docs and the glossary (where "Specialist" stops being a historical synonym once no code carries it). Persisted identifiers and EF tables are renamed with migrations; old journals and published executables are unsupported; there are no compatibility aliases or old readers; the single pre-GA version line takes one Minor bump with a release note carrying the old-to-new map. Named outcomes: Studio module id `agent` (npm `@elsa-workflows/studio-agent`), provider features `AnthropicAgents`, `OpenAIAgents` and `AzureOpenAIAgents`, compiler fingerprint `elsa.agent/compiler/2.1.0`; Weaver's Studio pieces take `weaver` ids in the first stage; the Weaver effects draft (#3845) is parked and rebases afterwards. The old-to-new map that serves as the release note lives in [docs/reference/package-renames.md](../reference/package-renames.md). The Elsa constitution admits `Elsa.Weaver` to §E2.1 in minor update 6.4.0 → 6.5.0 with its sync-impact report. Each stage is one reviewed PR with the dependency and closure qualification D1 requires, and the retired-term analyzer (ADR 0080 D5) bans each old name with the stage that removes its last use. The agent native-collaboration leaves under #2892 follow the rename so their witnesses are written under the new names.

