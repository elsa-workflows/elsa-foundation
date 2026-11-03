# Workflow Agent extension points

Owner project: `Elsa.Agent.Workflows`
Domain: `Elsa.Agent.Workflows`

## Cross-domain contributions

The module contributes to [Agent foundation](../Elsa.Agent/EXTENSION_POINTS.md) through contracts in `Elsa.Agent.Core`.

- `IAgentContextProvider`: `DefaultWorkflowAgentContextProvider` (`internal sealed`) admits `workflow.definition` schema `1` for exact `workflow-definition` targets. It whitelists bounded activity identities, connections and positions, stamps caller material as `ClientSnapshot`, and emits installed guidance separately. Property values, caller permission claims and arbitrary diagnostics are excluded. A separate `workflow.request` schema `1` admits only bounded intent/name/description/reference metadata with no target, client binding or tool scopes; it never supplies graph authority. Other workflow kinds and versions produce explicit diagnostics. With admitted workflow context it also contributes two server-produced kinds, described under [Truth seams](#truth-seams-spec-1193); a caller cannot supply either.
- `IAgentCapabilityProvider`: `WorkflowAgentCapabilityProvider` (`internal sealed`) declares explanation of the admitted graph and a reviewed position-change journey. It does not advertise runtime investigation, activity creation, save or publish.
- `IAgentTool`: `WorkflowReadGraphTool` returns a typed `workflows.graph` result. `WorkflowSetDesignerPositionTool` prepares exact `activityId`, finite `x`/`y`, target revision and originating client generation; it requires current `workflows/design:manage` permission and explicit review even under FullAuto.
- `IAgentClientTool`: the same layout tool admits the owning editor's observed result as `workflows.layout-change` schema `1`, renderer `workflows.agent.layout-result`. The receipt proves unsaved local state only. Native editor application and whole-batch undo remain module-owned; save and publish remain explicit.
- `IAgentToolAuthorization`: both installed tools also implement this optional companion. The graph tool asks the owner `IWorkflowDefinitionAuthorizer.CanReadAsync`; layout asks `CanManageAsync`. Generic orchestration combines this with host authority during advertisement, prepared invocation admission, duplicate delivery and reviewed dispatch/completion. No second tool registry is installed. When the owner throws `WorkflowDefinitionAuthorizationException`, the tools and the context provider fail closed as unavailable with the Design API's code `workflow.authorization.unavailable`, distinct from `agent.workflow.permission_denied`, and disclose no cause. The tools report an owner-unavailable violation, so dispatch reports 503. The context provider reports an owner-unavailable required diagnostic, so collection and turn admission, including the re-admission before each tool call, fail with that code and 503 instead of `agent.context.incomplete`. A denial keeps `agent.context.incomplete` with 400. `agent.workflow.authority_unavailable` keeps its meaning: zero or several authorizers are composed.

The named context/capability/tool fan-in and generic review lifecycle are owned by Agent foundation. This module registers contributions, not additional aggregating handlers or provider loops.

This is the accepted M1 workflow subset. The [76ec independent package witness](../../../../specs/1174-weaver-package-witness/evidence/qualification-76ec58d00.json) executes the original and restored position/validation/whole-Undo/explicit-Save journeys through both actual provider adapters. [Final-head qualification/CI, independent review, merge and resulting-main verification](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6539679143) completed through [#3497](https://github.com/elsa-workflows/elsa-foundation/pull/3497); Program M2–M5 and durable recovery remain open. Graph batches, activity creation, expression authoring and troubleshooting remain [#3442](https://github.com/elsa-workflows/elsa-foundation/issues/3442); a position-change receipt does not establish those journeys. Preview package/API migration is documented in the [canonical contribution contract](../../../../specs/1018-weaver-module-contributions/contracts/contributions.md#preview-migration).

## Truth seams (spec 1193)

The module reads two owning domains through their `.Core` contracts only, and registers no fallback for either:

- **Activity catalog.** `workflow.catalog` schema `1` (origin `AuthoritativeRead`, no target, no tool scopes) is the
  [`IActivityAuthoringCatalog`](../../../../Essentials/src/Activities/Design/Api/EXTENSION_POINTS.md#iactivityauthoringcatalog-contract--elsaactivitiesdesigncore)
  answer for the request actor and tenant: every entry the designer lists, addable or not, with its `available` flag and
  reason, activity type and exact version identity, and declared inputs with `isRequired`, `isSensitive` and
  `isCredential`. It is bounded to 28 KiB of entries in the owner's order; when entries do not fit it says so with
  `truncated`, `totalCount` and `includedCount`, and reports `budgetBytes`, `includedBytes` and `nextEntryBytes`, so a
  reader can check that the prefix stopped only because the next entry would not fit. `status` is `available`, `denied` (the actor lacks
  `activities/design:read`; no entries) or `unavailable`. A missing, ambiguous or failing catalog owner is
  `unavailable` with the optional diagnostic `agent.workflow.catalog_unavailable`, whose message names the missing
  capability; explanation still works. An owner failure is logged at warning with its cause; the model sees only the fixed
  text.
- **Permissions.** `workflow.permissions` schema `1` (origin `AuthoritativeRead`) is contributed for each admitted exact
  `workflow-definition` target, with the snapshot's classification. `read` is `allowed` (the context was admitted on the
  owner's read decision); `manage` is `allowed`, `denied` or `unavailable` (the owner threw
  `WorkflowDefinitionAuthorizationException`), from the host `IAgentToolAuthorizer` for the layout tool and the owning
  `IWorkflowDefinitionAuthorizer.CanManageAsync`, the same decisions the engine uses to advertise tools. `tools` and
  `capabilities` follow: manage allowed adds `workflow_set_designer_position` and `workflow.propose-change`; otherwise
  the actor gets explanation only. `tools` follows the engine's advertisement rule for each tool: its own descriptor
  through the host `IAgentToolAuthorizer`, its owner decision, and the session's capability policy for its name
  (`IAgentPolicyEvaluator.EvaluateCapabilityAsync`, which applies `DeniedCapabilityIds`), so it names exactly what the
  engine will offer. `capabilities` reports each capability by its own id under that policy, independent of the tools, as the Agent API
  admits a requested capability; `workflow.propose-change` is reported only when `manage` is `allowed`, which is stricter
  than the API's admission. `manage` combines the host decision for the layout tool with the owner's answer: when the host
  `IAgentToolAuthorizer` refuses the layout tool the owner is not asked and `manage` is `denied`, so `denied` does not by
  itself mean the owner refused. A host that refuses `workflow_read_graph` withholds the whole workflow context with the
  required `agent.workflow.permission_denied` (disclosure is admitted on the read tool's descriptor); a host cannot keep the
  workflow context while withholding only the read tool.

Both server reads respect the session's host context policy. When `IAgentPolicyEvaluator.EvaluateContextAsync` does not
admit a kind (for example a policy whose `AllowedContextKinds` lists only the older workflow kinds), the module does not
contribute it: no read is made, no required failure follows, and the optional diagnostic
`agent.workflow.server_read_withheld` says which kind was left out. Explanation and the reviewed layout change keep
working exactly as before the server reads existed, and re-admission of a disclosure recorded before them adds none.

Both kinds are in the descriptor's `ContextKinds`. `AdmitDisclosureAsync` accepts a retained one only with the module's
own origin. For the current turn it never trusts a retained read: it reads the catalog and each target's permissions
again, under the same attachment identities and in the same positions (redacted items stay where they were), so an unchanged answer is the identical projection (no new disclosure
snapshot) and a changed one is a new snapshot under the session's disclosure limits. Historical admission keeps what was
disclosed and re-checks only the catalog's read gate (`IActivityAuthoringCatalog.CanReadAsync`) when entries were
disclosed: a lost read is the required `agent.workflow.permission_denied`, an owner outage the required
`activities.catalog.unavailable`, marked owner-unavailable. Receipt continuations are unchanged: guidance only.

Revision identity follows FR-005: `workflow.definition` already carries the exact target, the working-state content
revision and the client binding. No persisted revision is read in its place.

## Removed legacy seams

The seven public legacy contracts, their default implementations, their registrations and the models only they used are
removed (spec 1193 D3): `IWorkflowAgentContextProvider`, `IWorkflowActivityCatalogProvider`
(`DefaultWorkflowActivityCatalogProvider`, empty), `IWorkflowRevisionProvider` (`DefaultWorkflowRevisionProvider`,
`unknown`), `IWorkflowChangePermissionEvaluator` (`DenyAllWorkflowChangePermissionEvaluator`),
`IWorkflowChangeProposalService` (`DefaultWorkflowChangeProposalService`), `IWorkflowGraphOperationBatchRiskClassifier`
(`DefaultWorkflowGraphOperationBatchRiskClassifier`, direct apply) and `IWorkflowAuthoringAuditService`
(`DefaultWorkflowAuthoringAuditService`), with `WorkflowAgentContext`, its permission, constraint, selection, diagnostic
and catalog-item records, the operation-batch, risk, clarification, audit and proposal-request records. None had a
production consumer. Their replacements: the catalog and permissions above; Agent's own tool-invocation review for
proposals; Agent's tool-invocation audit for every workflow tool invocation and outcome; and, from slice C, an internal
classifier that never grants direct apply. `WorkflowAgentGraph` and its activity and connection summaries stay.

Placeholder absence (SC-003) is asserted in two places: `WorkflowAgentNativeAuthorityTests` checks the source-composed
host (no legacy type in this assembly or its registrations, the real `HttpActivityAuthoringCatalog` and
`HttpWorkflowDefinitionAuthorizer`, and catalog entries equal to the designer's catalog route), and the conformance
probe's `workflow-truth-seams` case checks the packaged consumer through its generation observation (the loaded package's
type names and the one composed catalog).

**SemVer: Major**, classed as pre-GA cleanup (constitution §4.2, ADR 0080 D4). `DefaultWorkflowAgentContextProvider`
and `WorkflowAgentCapabilityProvider` are now `internal sealed` (ADR 0080 D3), also Major for any Preview consumer that
named them; hosts compose the feature instead. The new context kinds are Minor.


## Current disclosure ownership (#3439)

`DefaultWorkflowAgentContextProvider` implements `AdmitDisclosureAsync` on the existing source
`workflows.context`. Initial collection and retained disclosure use the current native actor/tenant
and the owning read descriptor's `workflows/design:read` permission against the exact target.
An unsaved working-state revision is not compared with a persisted database revision. Both graph
and position tools declare `DisclosureSourceId=workflows.context`; result-only admission still
checks the exact owning workflow target. Historical/receipt authority does not imply a fresh editor
read. Receipt continuation omits the obsolete snapshot while retaining the truthful accepted result;
no save, publish or effect replay is introduced. Generic failure/deadline/provenance rules belong to
[Agent foundation](../Elsa.Agent/EXTENSION_POINTS.md#context-and-result-disclosure-3439-under-qualification).

Concrete context/history/results additionally require the owner domain's
[`IWorkflowDefinitionAuthorizer`](../../../../Essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md#iworkflowdefinitionauthorizer-contract--elsaworkflowsdesigncore).
The workflow contribution references its Core contract; native API resource and identity implementation
types remain in Workflow Design's API adapter. Missing or multiple owner implementations refuse access.
Null-target supplemental caller metadata and public guidance retain their existing generic actor/read
checks and acquire no concrete tool authority. Tool advertisement cannot infer resource authority
from guidance alone.

Preview consumers must rebuild for the added optional owner-authorizer constructor parameter on
`WorkflowReadGraphTool`; `DefaultWorkflowAgentContextProvider` is no longer public. A metadata-only
`new WorkflowReadGraphTool().Descriptor` remains source-compatible, while actual tool authorization
fails closed without a concrete target and one current owner implementation. Workflow Design Core
adds the two-method public owner contract; Workflow Design API supplies its default HTTP adapter.
