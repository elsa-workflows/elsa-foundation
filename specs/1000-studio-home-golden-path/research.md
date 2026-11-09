# Research: Home navigation

Source pin: `5d1df7097db3a731330735a94c2b6a5fb7f9a476` (whole tree equals reviewed4f). Canonical graph returned no relevant Studio symbols; current source fallback was used. No unresolved product choice remains.

| Decision | Rationale / source | Alternative rejected |
|---|---|---|
| Durable group before grid | HomePage controls widget order/hide and Get Started auto-hide; task entry must survive those. | Fixed widget that can be hidden/reordered. |
| Existing complete definitions read | homeData.ts requests take=8 but ListDefinitions has no take/page field; endpoint returns all authorized matches. Preserve draftId and determine uniqueness before slicing. | Inferring uniqueness from eight preview rows; new server paging API. |
| Current-definition route | pages.tsx selects definition; useWorkflowEditorData loads current draft. | Historical draft URL or silent draft creation. |
| Consumed client intent | WorkflowDefinitions owns openCreateDialog/catalog/readiness/POST; Home only navigates. | Home POST or generic list destination labelled Create. |
| Scoped permission affordance | Current ClaimsPermissionEvaluator supports wildcard/subtree/manage→read; ClaimsAuthSessionService returns raw grants while usePermissions checks exact strings. | Missing exact key treated as denial; global permission rewrite. |
| Existing module-safe modal/focus | UI shared.ts explicitly excludes host-only DialogHost. Dashboard owns useHomeDialogFocus and settings-dialog composition. | Deep host import or another focus trap. |
| Explicit recovery/identity | WorkflowEditor currently falls back to Loading when details/draft absent; route can reuse editor on ID change. Verify no-draft and late-selection cases without changing lifecycle. | Loading forever or silently picking another definition. |

Backend permission keys remain workflows/design:read/manage; activity catalog has its own read gate. Capability discovery is not an authorization grant. Existing Workflows create/catalog helpers remain authoritative. Frontend feature gating uses the existing `workflows` ↔ `Elsa.Studio.Workflows` matcher.

Relevant sources: Dashboard HomePage/homeLayout/widgets/homeData; Workflows pages/WorkflowDefinitions/CreateWorkflowDialog/useWorkflowEditorData/WorkflowEditor/api/workflowDesign/api/capabilities; Workflow Design Definitions List/Get/Add endpoints and authorization; Foundation Identity AuthorizationServices/PermissionPolicyCodec/ClaimsAuthSessionService; app usePermissions/runtime/useHostControlAccess. No external technology selection or new dependency is required.
