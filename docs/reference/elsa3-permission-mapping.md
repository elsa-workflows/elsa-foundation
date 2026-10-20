# Elsa 3.9 to Elsa 4 permission mapping

> **Audience:** integrators moving Elsa 3.9 roles to Elsa 4, and the authors of the Elsa 3 import.
> **Purpose:** state Elsa 4's permission vocabulary and matching rules, and map every built-in Elsa 3.9
> permission to its Elsa 4 equivalent.
> **Decisions:** [ADR 0085](../adr/0085-elsa-3-parity-identity-administration-and-scope.md) D2 (vocabulary) and D8 (the
> one-shot import rewrites roles through this table). Introduced by #2773.

## The shape of a permission

Elsa 4 uses Elsa 3.9's shape. A permission is `{resource}:{verb}`:

- `:` separates the resource from the verb. A permission has exactly one.
- `/` separates a parent resource from a child resource, as in `workflows/runtime` and `workflows/runtime/javascript`.
- `-` joins words inside one resource segment or one verb, as in `execution-evidence` and `inspect-values`.
- Resource segments and verbs are lowercase. Elsa 4 compares permissions case-insensitively, after Unicode NFC
  normalization, so `IDENTITY/USERS:READ` and `identity/users:read` are the same permission.

This is Elsa 3.9's separator rule (`Permission.cs` and `PermissionMatcher.cs` in 3.9, listed under
[Sources](#sources)), so a 3.9 resource path such as `diagnostics/structured-logs` reads the same in both products. A
resource sits under a parent only where Elsa groups several resources under one area: `identity`, `workflows`,
`activities`, `diagnostics`, `studio`, `expressions` and `agent`. A resource with no siblings keeps a single segment,
such as `secrets` or `module-management`.

## How a grant matches

A role or a principal holds grants. A grant satisfies a required permission when both its resource and its verb
match:

| Grant | Matches |
|---|---|
| `identity/users:read` | exactly `identity/users:read` |
| `identity/*:read` | `identity:read`, `identity/users:read`, `identity/roles/members:read`: the named resource and every resource beneath it, at any depth |
| `identity/users:*` | every verb on `identity/users` |
| `*:read` | `read` on every resource |
| `*` | everything; it is the superuser grant and means the same as `*:*` |

A `*` has meaning only as the whole resource, as a trailing resource segment (`workflows/*`), or as the whole verb. A
grant such as `identity/*` (no verb), `identity*:read` or `*/users:read` matches nothing. Wildcards reach permissions
registered later, so `workflows/*:read` covers a workflows module installed after the role was written.

Elsa 4 adds one rule Elsa 3.9 does not have: **`manage` satisfies `read` on the same resource**. A holder of
`identity/users:manage` may read users, and a holder of `identity/*:manage` may read every identity resource. No
other verb satisfies another, and the permission catalog carries no implications.

A catalog permission is always one concrete `{resource}:{verb}` without wildcards. A required permission, as an
endpoint declares it, is one too, or the bare `*`, which only the superuser grant satisfies. A catalog contribution or
an endpoint registration with any other shape fails at startup.

## Unknown grants

A host that stores roles (the ASP.NET Core Identity features) logs one warning at startup for each role and grant
where the grant matches no registered permission, naming the role and the grant. A pre-rename name, an Elsa 3 name, a
typo, a malformed pattern, and a grant for a feature the host does not enable all produce the warning, because each one
authorizes nothing. The check reads the roles of the identity default tenant and asks the same evaluator that
authorizes requests whether the grant reaches any registered permission.

## Elsa 4 permissions

| Area | Permissions | Owner |
|---|---|---|
| Identity | `identity/users:read`, `identity/users:manage`, `identity/roles:read`, `identity/roles:manage`, `identity/permissions:read`, `identity/applications:read`, `identity/applications:manage`, `identity/providers:read`, `identity/providers:manage`, `identity/credentials:manage` | `Elsa.Foundation.Identity` |
| Workflow design | `workflows/design:read`, `workflows/design:manage` | `Elsa.Workflows.Design.Api` |
| Workflow publishing | `workflows/publishing:read`, `workflows/publishing:manage` | `Elsa.Workflows.Publishing.Api` |
| Workflow runtime | `workflows/runtime:read`, `workflows/runtime:execute`, `workflows/runtime:manage`, `workflows/activity-executions:inspect`, `workflows/activity-executions:inspect-values`, `workflows/activity-executions:resolve-value-payloads` | `Elsa.Workflows.Runtime.Api` |
| Runtime JavaScript | `workflows/runtime/javascript:execute` | `Elsa.Workflows.Runtime.JavaScript` |
| Workflow dashboard | `workflows/dashboard:read` | `Elsa.Workflows.Dashboard` |
| Execution evidence | `workflows/execution-evidence:read`, `workflows/execution-evidence:delete`, `workflows/execution-evidence:manage` | `Elsa.Workflows.ExecutionEvidence` |
| Activity design | `activities/design:read`, `activities/design:manage`, `activities/design:author`, `activities/design/provider-payload:read` | `Elsa.Activities.Design.Api` |
| BPMN interchange | `bpmn-interchange:read`, `bpmn-interchange:manage` | `Elsa.Activities.Bpmn.Interchange` |
| Expressions | `expressions:read`, `expressions/javascript:render` | `Elsa.Expressions.Api`, `Elsa.Expressions.JavaScript.Rendering` |
| Diagnostics | `diagnostics/opentelemetry:read`, `diagnostics/structured-logs:read` | `Elsa.Diagnostics.OpenTelemetry`, `Elsa.Diagnostics.StructuredLogs` |
| Secrets | `secrets:read`, `secrets:write`, `secrets:update-value`, `secrets:delete`, `secrets:test`, `secrets:use`, `secrets:import`, `secrets:export` | `Elsa.Secrets.Api` |
| Connections | `connections:manage`, `connections:bind-secret`, `connections:test` | `Elsa.Connections.Api` |
| Studio preferences | `studio/preferences:read`, `studio/preferences:write`, `studio/preferences/defaults:manage` | `Elsa.Studio.Preferences.Api` |
| Platform | `attention:read`, `api-capabilities:read`, `module-management:read`, `module-management:manage` | `Elsa.Attention.Api`, `Elsa.Api.Capabilities`, `Elsa.Modularity.Api` |
| Extensions | `elsa3-import:read`, `elsa3-import:manage`, `extension-builder:read`, `extension-builder:manage`, `agent:use`, `agent/proposals:manage`, `agent/audit:read` | `Elsa3.Activities.Design.Import`, `Elsa.ExtensionBuilder.Api`, `Elsa.Agent.Api` |

The execution evidence delete endpoint accepts `workflows/execution-evidence:delete` or
`workflows/execution-evidence:manage`.

Connections has no read or use permission of its own. Listing connections, their types and their metadata, never a
secret value, requires `workflows/design:read` or `connections:manage`. Binding a connection in a workflow requires
`workflows/design:manage`, which every route that writes or promotes workflow state already requires; draft validation
never checks the caller, so every viewer sees the same findings. Otherwise the three Connections permissions guard
administration.

## Elsa 3.9 to Elsa 4

The table covers every permission an Elsa 3.9 module registers, and the `*` grant. Elsa 3.9 calls the read verb
`view`; Elsa 4 calls it `read`. Where one 3.9 permission maps to several Elsa 4 permissions, the import must grant all
of them, or the role loses access. "None" means Elsa 4 has no first-party endpoint for what the permission guarded in
3.9; the import should report such grants rather than invent a target.

### Workflows

Source: `src/modules/Elsa.Workflows.Api/Permissions/WorkflowPermissions.cs`, plus
`src/modules/Elsa.Bpmn.Interchange/Permissions/BpmnPermissions.cs` for the BPMN endpoints that reuse
`workflows/definitions`.

| Elsa 3.9 | Elsa 4 | Notes |
|---|---|---|
| `workflows/definitions:view` | `workflows/design:read`, `bpmn-interchange:read` | 3.9 BPMN analyze and export require this permission. |
| `workflows/definitions:write` | `workflows/design:manage`, `bpmn-interchange:manage` | 3.9 BPMN import requires this permission. |
| `workflows/definitions:delete` | `workflows/design:manage` | |
| `workflows/definitions:execute` | `workflows/runtime:execute` | |
| `workflows/definitions:publish` | `workflows/publishing:manage` | |
| `workflows/definitions:retract` | `workflows/publishing:manage` | Unpublishing a slot. |
| `workflows/definitions:refresh` | None | Elsa 4 has no definition refresh endpoint. |
| `workflows/definitions:reload` | None | Elsa 4 has no definition reload endpoint. |
| `workflows/definitions/versions:view` | `workflows/design:read` | |
| `workflows/definitions/versions:delete` | None | Elsa 4 design versions have no delete endpoint. |
| `workflows/definitions/versions:revert` | `workflows/publishing:manage` | Closest: restoring a retired publication to its slot. |
| `workflows/instances:view` | `workflows/runtime:read` | |
| `workflows/instances:write` | None | Elsa 4 has no instance import endpoint. |
| `workflows/instances:delete` | None | Elsa 4 has no instance delete endpoint. |
| `workflows/instances:cancel` | `workflows/runtime:manage` | Elsa 4 cancels through alteration plans. |
| `workflows/activity-executions:view` | `workflows/runtime:read`, `workflows/activity-executions:inspect` | Captured values also need `workflows/activity-executions:inspect-values`, which this row deliberately does not grant. |
| `workflows/runtime:view` | `workflows/runtime:read` | |
| `workflows/runtime:control` | None | Elsa 4 has no runtime pause, resume or drain endpoint. |
| `workflows/bookmark-queue/dead-letters:view` | `workflows/runtime:read` | Dispatch list and detail. |
| `workflows/bookmark-queue/dead-letters:replay` | `workflows/runtime:manage` | Dispatch redrive. |
| `workflows/bookmark-queue/dead-letters:delete` | None | |
| `workflows/events:trigger` | `workflows/runtime:execute` | Stimulus dispatch. |
| `workflows/tasks:complete` | `workflows/runtime:execute` | Closest: stimulus dispatch. |
| `workflows/tests:execute` | `workflows/publishing:manage` | Test runs. |
| `workflows/descriptors/activities:view` | `activities/design:read` | The activity catalog. |
| `workflows/descriptors/expressions:view` | `expressions:read` | |
| `workflows/descriptors/variables:view` | `expressions:read` | Variable types. |
| `workflows/descriptors/incident-strategies:view` | `workflows/publishing:read` | |
| `workflows/descriptors/output-converters:view` | `workflows/publishing:read` | Closest: value-conversion profiles. |
| `workflows/descriptors/storage-drivers:view` | None | |
| `workflows/descriptors/commit-strategies:view` | None | |
| `workflows/descriptors/log-persistence-strategies:view` | None | |
| `workflows/descriptors/activation-strategies:view` | None | |
| `system/features:view` | `module-management:read` | |

In 3.9 any authenticated caller may read most descriptor catalogs without a permission. In Elsa 4 the activity
catalog needs `activities/design:read`, and expression and variable descriptors need `expressions:read`.

### Labels and JavaScript

Sources: `src/modules/Elsa.Labels/Permissions/LabelPermissions.cs` and
`src/modules/Elsa.Expressions.JavaScript/Permissions/JavaScriptPermissions.cs`.

| Elsa 3.9 | Elsa 4 | Notes |
|---|---|---|
| `labels:view`, `labels:create`, `labels:update`, `labels:delete` | None | Elsa 4 has no labels module. |
| `workflows/definitions/labels:view`, `workflows/definitions/labels:update` | None | |
| `workflows/scripting/javascript:view` | `expressions/javascript:render` | The JavaScript declarations for the editor. |

### Identity and secrets

Sources: `src/modules/Elsa.Identity/Permissions/IdentityPermissions.cs` and
`src/modules/Elsa.Secrets/Permissions/SecretsResourcePermissions.cs`.

| Elsa 3.9 | Elsa 4 | Notes |
|---|---|---|
| `identity/users:view` | `identity/users:read` | |
| `identity/users:create`, `identity/users:update`, `identity/users:delete` | `identity/users:manage` | |
| `identity/roles:view` | `identity/roles:read` | |
| `identity/roles:create`, `identity/roles:update`, `identity/roles:delete` | `identity/roles:manage` | |
| `identity/applications:create` | `identity/applications:manage` | |
| `secrets:view` | `secrets:read` | |
| `secrets:write` | `secrets:write`, `secrets:update-value` | 3.9 write covers rotation; Elsa 4 rotates with `secrets:update-value`. |
| `secrets:delete` | `secrets:delete` | Both cover revocation. |
| `secrets:test` | `secrets:test` | |

### External authentication

Source: `src/modules/Elsa.ExternalAuthentication/Permissions/ExternalAuthenticationResourcePermissions.cs`.

| Elsa 3.9 | Elsa 4 | Notes |
|---|---|---|
| `external-authentication/connections:view` | `identity/providers:read` | |
| `external-authentication/connections:create`, `:update`, `:archive`, `:test`, `:preview` | `identity/providers:manage` | |
| `external-authentication/descriptors:view` | `identity/providers:read` | |
| `external-authentication/identity-links:view` | `identity/users:read` | |
| `external-authentication/identity-links:write`, `:delete` | `identity/users:manage` | Elsa 4 links external identities as part of user management. |
| `external-authentication/sessions:view`, `:revoke` | None | |
| `external-authentication/policies:view` | `identity/providers:read` | Closest. |
| `external-authentication/policies:update` | `identity/providers:manage` | Closest. |
| `external-authentication/policies/default-roles:update` | `identity/providers:manage` | Closest. |
| `external-authentication/provider-trust:override` | None | |
| `external-authentication/permission-grants:delegate`, `:delegate-unrestricted` | None | |

### Operations, platform and AI

Sources: `src/modules/Elsa.UserTasks/Permissions/UserTasksResourcePermissions.cs`,
`src/modules/Elsa.Dashboard.Api/Permissions/DashboardResourcePermissions.cs`,
`src/modules/Elsa.Diagnostics.ConsoleLogs/Permissions/ConsoleLogsResourcePermissions.cs`,
`src/modules/Elsa.Diagnostics.StructuredLogs/Permissions/StructuredLogsResourcePermissions.cs`,
`src/modules/Elsa.Diagnostics.OpenTelemetry/Permissions/OpenTelemetryResourcePermissions.cs`,
`src/modules/Elsa.Resilience/Permissions/ResiliencePermissions.cs`,
`src/modules/Elsa.Alterations/Permissions/AlterationPermissions.cs`,
`src/modules/Elsa.Tenants/Permissions/TenantPermissions.cs`,
`src/modules/Elsa.Shells.Api/Permissions/ShellPermissions.cs` and
`src/modules/Elsa.AI.Host/Permissions/AIResourcePermissions.cs`.

| Elsa 3.9 | Elsa 4 | Notes |
|---|---|---|
| `user-tasks:view`, `:update`, `:claim`, `:complete`, `:assign`, `:cancel`, `:invite`, `:supervise` | None | Elsa 4 has no User Tasks module yet (ADR 0085 D10). |
| `user-tasks/participants:view` | None | |
| `dashboard:view` | `workflows/dashboard:read` | |
| `diagnostics/console-logs:view` | None | Elsa 4 has no console log endpoint. |
| `diagnostics/structured-logs:view` | `diagnostics/structured-logs:read` | |
| `diagnostics/opentelemetry:view` | `diagnostics/opentelemetry:read` | |
| `resilience/retries:view`, `resilience/strategies:view`, `resilience/simulation:execute` | None | |
| `alterations:view` | `workflows/runtime:read` | Alteration plans and jobs. |
| `alterations:execute` | `workflows/runtime:manage` | |
| `tenants:view`, `:write`, `:delete`, `:refresh` | None | |
| `system/shells:reload` | `module-management:manage` | Closest: applying a feature change. |
| `ai/chat:execute`, `ai/tools:view`, `ai/capabilities:view` | `agent:use` | The Agent extension. |
| `*` | `*` | Superuser in both. |

### Wildcard grants

A 3.9 wildcard grant keeps its wildcard and has its verb translated: `*:view` becomes `*:read`, and `*:execute` stays
`*:execute`. A subtree grant such as `workflows/*:view` becomes `workflows/*:read`, but the two products divide the
`workflows` tree differently, so the import must check what a translated subtree reaches in Elsa 4 rather than assume
it matches. The startup warning reports any translated grant that reaches nothing.

## Renamed in Elsa 4 by #2773

Elsa 4 is unreleased, so the old names are not accepted anywhere; this list is for readers of older issues and
specs. Each old name is reported by the startup warning if a stored role still holds it.

| Before | After |
|---|---|
| `identity.users.read`, `identity.users.manage` | `identity/users:read`, `identity/users:manage` |
| `identity.roles.read`, `identity.roles.manage` | `identity/roles:read`, `identity/roles:manage` |
| `identity.permissions.read` | `identity/permissions:read` |
| `identity.applications.read`, `identity.applications.manage` | `identity/applications:read`, `identity/applications:manage` |
| `identity.providers.read`, `identity.providers.manage` | `identity/providers:read`, `identity/providers:manage` |
| `identity.credentials.manage` | `identity/credentials:manage` |
| `workflow-design.read`, `workflow-design.manage` | `workflows/design:read`, `workflows/design:manage` |
| `workflow-publishing.read`, `workflow-publishing.manage` | `workflows/publishing:read`, `workflows/publishing:manage` |
| `workflow-runtime.read`, `.execute`, `.manage` | `workflows/runtime:read`, `:execute`, `:manage` |
| `workflows.runtime.javascript.execute` | `workflows/runtime/javascript:execute` |
| `workflows.activity-executions.inspect`, `.inspect-values`, `.resolve-value-payloads` | `workflows/activity-executions:inspect`, `:inspect-values`, `:resolve-value-payloads` |
| `workflows.dashboard.read` | `workflows/dashboard:read` |
| `execution-evidence.read`, `.delete`, `.manage` | `workflows/execution-evidence:read`, `:delete`, `:manage` |
| `activity-design.read`, `activity-design.manage` | `activities/design:read`, `activities/design:manage` |
| `activities.design.author` | `activities/design:author` |
| `activities.design.provider-payload.read` | `activities/design/provider-payload:read` |
| `bpmn-interchange.read`, `bpmn-interchange.manage` | `bpmn-interchange:read`, `bpmn-interchange:manage` |
| `elsa3-import.read`, `elsa3-import.manage` | `elsa3-import:read`, `elsa3-import:manage` |
| `expressions.read` | `expressions:read` |
| `expressions.javascript.render` | `expressions/javascript:render` |
| `Diagnostics:OpenTelemetry.Read` | `diagnostics/opentelemetry:read` |
| `Diagnostics:OpenTelemetry` | Removed; it was an alias of the read permission. |
| `Diagnostics:StructuredLogs` | `diagnostics/structured-logs:read` |
| `studio.preferences.read`, `studio.preferences.write` | `studio/preferences:read`, `studio/preferences:write` |
| `studio-preferences.manage-defaults` | `studio/preferences/defaults:manage` |
| `attention.read` | `attention:read` |
| `api-capabilities.read` | `api-capabilities:read` |
| `module-management.read`, `module-management.manage` | `module-management:read`, `module-management:manage` |
| `extension-builder.read`, `extension-builder.manage` | `extension-builder:read`, `extension-builder:manage` |
| `agent.use` | `agent:use` |
| `agent.proposals` | `agent/proposals:manage` |
| `agent.audit` | `agent/audit:read` |

The `secrets:*` names already had this shape and are unchanged. The catalog's other implications are gone:
`secrets:write` no longer satisfies `secrets:read`, `studio/preferences:write` no longer satisfies
`studio/preferences:read`, `identity/roles:manage` no longer satisfies `identity/permissions:read`, and
`agent/proposals:manage` no longer satisfies `agent:use`. Grant each permission a role needs.

## Sources

Elsa 3.9 at tag [`3.9.0`](https://github.com/elsa-workflows/elsa-core/tree/3.9.0):

- Grammar and matching: [`src/common/Elsa.Api.Common/Authorization/Permission.cs`](https://github.com/elsa-workflows/elsa-core/blob/3.9.0/src/common/Elsa.Api.Common/Authorization/Permission.cs),
  [`PermissionMatcher.cs`](https://github.com/elsa-workflows/elsa-core/blob/3.9.0/src/common/Elsa.Api.Common/Authorization/PermissionMatcher.cs),
  [`PermissionEvaluator.cs`](https://github.com/elsa-workflows/elsa-core/blob/3.9.0/src/common/Elsa.Api.Common/Authorization/PermissionEvaluator.cs)
  and [`CoreVerbs.cs`](https://github.com/elsa-workflows/elsa-core/blob/3.9.0/src/common/Elsa.Api.Common/Authorization/CoreVerbs.cs).
- Vocabulary: the sixteen `src/modules/*/Permissions/*Permissions.cs` descriptor providers named in the tables above,
  and the `*` constant in [`src/common/Elsa.Api.Common/PermissionNames.cs`](https://github.com/elsa-workflows/elsa-core/blob/3.9.0/src/common/Elsa.Api.Common/PermissionNames.cs).
  That file also declares five older names (`ManageWorkflowRuntime`, `read:workflow-runtime` and three
  `bookmark-queue:dead-letters` names) that no 3.9 endpoint requires; they are left out.
- 3.9's own record of the vocabulary: [`specs/013-rbac-authorization-model/contracts/permissions.md`](https://github.com/elsa-workflows/elsa-core/blob/3.9.0/specs/013-rbac-authorization-model/contracts/permissions.md)
  and [`doc/migrations/authorization-model.md`](https://github.com/elsa-workflows/elsa-core/blob/3.9.0/doc/migrations/authorization-model.md).
