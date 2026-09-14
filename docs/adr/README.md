# Architecture decision records

Index of every ADR in this folder. Generated from each file's H1 and status line; the ADR files are the source of truth.
Numbers are not unique: the folder holds two independent sequences (an early Extension Builder series and the main series), so link by file name.

| Number | Title | Status |
|---|---|---|
| 0001 | [Checkpoint-Gated Activity Execution Inspection](0001-checkpoint-gated-activity-execution-inspection.md) | Accepted |
| 0001 | [Extension Builder Git identity](0001-extension-builder-git-identity.md) | Accepted |
| 0001 | [Mediate agent harness capabilities through Elsa tools](0001-mediate-agent-harness-capabilities.md) | (none) |
| 0002 | [Direct Weaver workflow edits target designer working state](0002-direct-weaver-workflow-edits-target-designer-working-state.md) | (none) |
| 0002 | [Extension Builder managed repository remotes](0002-extension-builder-managed-repository-remotes.md) | Accepted |
| 0003 | [Extension Builder build and promotion source](0003-extension-builder-build-and-promotion-source.md) | Accepted |
| 0003 | [Weaver direct-apply risk classification is Elsa-owned](0003-weaver-direct-apply-risk-classification-is-elsa-owned.md) | (none) |
| 0004 | [Extension Builder Git safety envelope](0004-extension-builder-git-safety-envelope.md) | Accepted |
| 0005 | [Extension Builder pack and promote scope](0005-extension-builder-pack-and-promote-scope.md) | Accepted |
| 0006 | [Extension Builder workspace storage](0006-extension-builder-workspace-storage.md) | Accepted |
| 0007 | [Extension Builder collaboration through working copies](0007-extension-builder-collaboration-working-copies.md) | Accepted |
| 0008 | [Extension Builder working-copy branch model](0008-extension-builder-working-copy-branch-model.md) | Accepted |
| 0009 | [Extension Builder build execution boundary](0009-extension-builder-build-execution-boundary.md) | Accepted |
| 0010 | [Extension Builder repository workbench UX](0010-extension-builder-repository-workbench-ux.md) | Accepted |
| 0011 | [Extension Builder repository entry model](0011-extension-builder-repository-entry-model.md) | Accepted |
| 0012 | [Extension Builder v1 editor scope](0012-extension-builder-v1-editor-scope.md) | Accepted |
| 0013 | [Extension Builder source persistence](0013-extension-builder-source-persistence.md) | Accepted |
| 0014 | [Extension Builder module and package boundary](0014-extension-builder-module-package-boundary.md) | Accepted |
| 0015 | [Extension Builder API capability split](0015-extension-builder-api-capability-split.md) | Accepted |
| 0016 | [Extension Builder conflict handling in v1](0016-extension-builder-conflict-handling-v1.md) | Accepted |
| 0017 | [Extension Builder repository access control](0017-extension-builder-repository-access-control.md) | Accepted |
| 0018 | [Extension Builder managed repository initial commit](0018-extension-builder-managed-repo-initial-commit.md) | Accepted |
| 0019 | [Extension Builder template governance](0019-extension-builder-template-governance.md) | Accepted |
| 0020 | [Runtime checkpoint commit records post-commit work without inline delivery](0020-runtime-checkpoint-commit-post-commit-work.md) | Accepted |
| 0021 | [Copilot provider keeps tool mutation Elsa-owned](0021-copilot-provider-keeps-tool-mutation-elsa-owned.md) | (none) |
| 0022 | [Copilot sessions use Elsa session identity](0022-copilot-sessions-use-elsa-session-identity.md) | (none) |
| 0023 | [Copilot provider is explicitly enabled](0023-copilot-provider-is-explicitly-enabled.md) | (none) |
| 0024 | [Copilot provider uses backend-owned identity](0024-copilot-provider-uses-backend-owned-identity.md) | (none) |
| 0025 | [Copilot context is prompt material not filesystem access](0025-copilot-context-is-prompt-material-not-filesystem-access.md) | (none) |
| 0026 | [Activity Availability Uses A Layered Policy Stack](0026-activity-availability-policy-stack.md) | (none) |
| 0027 | [Scoped Variable References Include Declaring Scope](0027-scoped-variable-references-include-declaring-scope.md) | Proposed |
| 0028 | [Loop Body Runs In A Per-Iteration Variable Scope](0028-loop-body-runs-in-a-per-iteration-variable-scope.md) | Accepted |
| 0029 | [Runtime Execution Flows Through The Workflow And Activity Pipelines](0029-runtime-execution-flows-through-the-pipelines.md) | Accepted |
| 0030 | [Runtime Expression Evaluation Uses A Parameter-Threaded Live Carrier](0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md) | Superseded by ADR 0045 and spec 095 |
| 0031 | [Runtime Burst Execution Uses A Sticky Single-Writer Drain With An In-Process Fast Path](0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md) | Accepted |
| 0032 | [Runtime Checkpoint Cadence Is Policy-Driven Per Workflow](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) | Accepted |
| 0033 | [Runtime.Core Splits Contracts From The Engine](0033-runtime-core-splits-contracts-from-engine.md) | Accepted |
| 0034 | [Workflow Definitions Reconcile From And Export To Git](0034-workflow-definitions-reconcile-from-and-export-to-git.md) | Proposed |
| 0035 | [Serialization Unifies On The Alias Registry And Retires Open-Object Polymorphism](0035-serialization-unifies-on-the-alias-registry-and-retires-open-object-polymorphism.md) | Accepted |
| 0036 | [Dynamic (`Any`) Expression Values Materialize As `JsonNode` With Per-Engine Adapters](0036-dynamic-any-expression-values-materialize-as-jsonnode-with-per-engine-adapters.md) | Accepted |
| 0037 | [Studio Management Bridge Keeps Host Management Keys Server-Side](0037-studio-management-bridge-keeps-host-management-key-server-side.md) | Accepted |
| 0038 | [Artifact Hash Is Purely Behavioral and Executables Are Content-Addressed](0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md) | Accepted |
| 0039 | [Layout Sidecar Lives on the Source Reference](0039-layout-sidecar-lives-on-the-source-reference.md) | Accepted |
| 0040 | [One Artifact Store with Reference-Derived Lifetime](0040-one-artifact-store-with-reference-derived-lifetime.md) | Accepted |
| 0041 | [Workflow Management Advertises Optional Authoring Capabilities](0041-workflow-management-advertises-optional-authoring-capabilities.md) | Superseded |
| 0042 | [Elsa Foundation Ships Only Groundwork Persistence Implementations](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md) | Superseded |
| 0043 | [Publication Slots Define Start Authority](0043-publication-slots-define-start-authority.md) | Accepted |
| 0044 | [`sourceKind` is canonical on executable inspection contracts](0044-source-kind-is-canonical-on-executable-inspection.md) | Accepted |
| 0045 | [Workflow Value Flow Uses Role-Owned Bindings And Immutable Invocation Records](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md) | Accepted |
| 0046 | [Output Binding Coercion Uses Pinned Value Representations](0046-output-binding-coercion-uses-pinned-value-representations.md) | Proposed |
| 0047 | [ReplaySafe Activities Execute As Fused Hops With Precomputed Routing](0047-replaysafe-activities-execute-as-fused-hops-with-precomputed-routing.md) | Accepted |
| 0048 | [Container Output Captures Resolve Through Producer-Visible Variable Frames](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md) | Accepted |
| 0049 | [Runtime alterations use snapshotted atomic jobs](0049-runtime-alterations-use-snapshotted-atomic-jobs.md) | Accepted |
| 0050 | [Activity Presentation Is Source-Owned Metadata](0050-activity-presentation-is-source-owned-metadata.md) | Accepted |
| 0050 | [Author-requested forward workflow versions preserve automatic assignment](0050-author-requested-forward-workflow-versions.md) | Proposed |
| 0051 | [Interactive design commands recover from authoritative state](0051-interactive-design-commands-recover-from-authoritative-state.md) | Accepted |
| 0052 | [Execution evidence is checkpoint-atomic and delivered at least once](0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md) | Proposed |
| 0053 | [Execution evidence capture is explicitly session-scoped](0053-execution-evidence-capture-is-explicitly-session-scoped.md) | Proposed |
| 0054 | [Execution evidence kinds form a governed extensible catalog](0054-execution-evidence-kinds-form-a-governed-extensible-catalog.md) | Proposed |
| 0055 | [Execution Evidence integrates through domain-owned adapters](0055-execution-evidence-integrates-through-domain-owned-adapters.md) | Proposed |
| 0056 | [Execution evidence value capture is explicit and lean](0056-execution-evidence-value-capture-is-explicit-and-lean.md) | Proposed |
| 0057 | [Execution Evidence API exposes neutral verification primitives](0057-execution-evidence-api-exposes-neutral-verification-primitives.md) | Proposed |
| 0058 | [Negative evidence claims require a complete gap-free range](0058-negative-evidence-claims-require-a-complete-gap-free-range.md) | Proposed |
| 0059 | [Execution evidence retention is session-centric](0059-execution-evidence-retention-is-session-centric.md) | Proposed |
| 0060 | [Execution evidence ordering is workflow-local and causal](0060-execution-evidence-ordering-is-workflow-local-and-causal.md) | Proposed |
| 0061 | [Baseline execution evidence records committed semantic transitions](0061-baseline-execution-evidence-records-committed-semantic-transitions.md) | Proposed |
| 0062 | [Execution Evidence starts in memory and adds Groundwork durability](0062-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md) | Proposed |
| 0062 | [JavaScript Binding Grammar Is Selected by Host Policy and Pinned at Publish](0062-javascript-binding-grammar-is-pinned-at-publish.md) | Proposed |
| 0063 | [BPMN moves to a host-agnostic library](0063-bpmn-moves-to-a-host-agnostic-library.md) | Proposed |
| 0064 | [Flowchart Infers Joins From Propagated Dead Paths](0064-flowchart-infers-joins-from-propagated-dead-paths.md) | Proposed |
| 0065 | [Groundwork Persistence Targets Are Named and Lanes Bind to Them](0065-groundwork-persistence-targets-are-named-and-lanes-bind-to-them.md) | Superseded |
| 0066 | [Reusable-Activity Publication Orders Its Writes Instead of Requiring One Transaction](0066-reusable-activity-publication-orders-writes-instead-of-one-transaction.md) | Accepted |
| 0067 | [Package versioning uses two version lines with a computed patch digit](0067-package-versioning-uses-two-lines-with-computed-patch.md) | Proposed |
| 0068 | [First-party REST APIs use ASP.NET Core Minimal APIs](0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md) | Accepted |
| 0069 | [OpenAPI contract types use stable API Core assemblies](0069-openapi-contract-types-use-stable-api-core.md) | Superseded |
| 0070 | [REST API contracts ship in one assembly per domain](0070-rest-api-contracts-ship-in-one-assembly-per-domain.md) | Proposed |
| 0071 | [First-party REST APIs use endpoint classes over Minimal APIs](0071-first-party-rest-apis-use-endpoint-classes.md) | Accepted |
| 0072 | [EF-first relational persistence with provider-derived contexts](0072-ef-first-relational-persistence-with-provider-derived-contexts.md) | Superseded |
| 0073 | [EF Core is the only first-party persistence family](0073-ef-core-is-the-only-first-party-persistence-family.md) | Accepted |
