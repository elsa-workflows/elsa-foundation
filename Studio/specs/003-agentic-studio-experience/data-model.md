# Data Model: Elsa Studio Agentic Experience

## AgentSession

Represents one multi-turn interaction between a user and the assistant.

**Fields**

- `id`: Stable session identifier.
- `title`: Optional user-visible title.
- `userId`: Authenticated user that owns the session.
- `tenantId`: Tenant or environment boundary for the session.
- `status`: `active`, `completed`, `cancelled`, `failed`, `expired`.
- `mode`: `explain`, `build`, `troubleshoot`, `operate`, `administer`.
- `activeSurface`: Current Studio route/resource context summary.
- `createdAt`, `updatedAt`, `expiresAt`: Lifecycle timestamps.
- `policySnapshotId`: Policy version used for the latest turn.

**Relationships**

- Has many `AgentMessage`.
- Has many `AgentContextAttachment`.
- Has many `AgentActionProposal`.
- Has many `AgentAuditEvent`.

**Validation**

- Must belong to the authenticated user and tenant.
- Active sessions cannot outlive configured retention/expiration policy.
- Session context must be recalculated or invalidated when permissions change.

## AgentMessage

Represents a user request, assistant response, system notice, tool/progress
event, or error.

**Fields**

- `id`: Stable message identifier.
- `sessionId`: Owning session.
- `role`: `user`, `assistant`, `system`, `tool`, `progress`, `error`.
- `content`: Text or structured message content.
- `status`: `pending`, `streaming`, `completed`, `failed`, `cancelled`.
- `contextAttachmentIds`: Attachments used by this turn.
- `proposalIds`: Action proposals produced by this turn.
- `createdAt`, `completedAt`: Timing metadata.
- `error`: Optional user-safe error summary.

**Validation**

- User messages require a non-empty prompt.
- Assistant/tool messages must reference the turn that produced them.
- Failed messages preserve user-safe diagnostics without exposing secrets.

## AgentContextAttachment

Represents bounded context made available to an agent turn.

**Fields**

- `id`: Stable context identifier.
- `sessionId`: Owning session.
- `source`: `studio-shell`, `workflow`, `diagnostics`, `module`, `feature`, `settings`, `documentation`, `server`.
- `sourceId`: Route, resource, workflow, module, or diagnostic identifier.
- `label`: User-visible context chip label.
- `contentType`: `summary`, `json`, `text`, `diagnostic`, `workflow-definition`, `selection`.
- `sensitivity`: `public`, `internal`, `sensitive`, `secret-redacted`.
- `scope`: `screen`, `selection`, `tenant`, `environment`, `module`.
- `content`: Context payload after policy filtering and redaction.
- `policyDecisionId`: Policy decision that allowed or denied inclusion.
- `createdAt`: Collection timestamp.

**Validation**

- Secret values are never included; only redacted markers or metadata are
  allowed.
- Context cannot cross tenant/environment boundaries.
- Attachments must identify their source and sensitivity.

## AgentCapability

Describes an assistant capability available to a session.

**Fields**

- `id`: Stable capability identifier.
- `moduleId`: Optional owning Studio module.
- `displayName`: User-visible name.
- `description`: Short capability summary.
- `kind`: `answer`, `context`, `prompt-starter`, `proposal`, `action`.
- `risk`: `read-only`, `review-required`, `destructive`, `admin`.
- `surfaces`: Routes/resource types where the capability applies.
- `requiredPermissions`: Permissions required to offer or execute it.
- `status`: `available`, `disabled`, `incompatible`, `policy-denied`.

**Validation**

- Module-owned capabilities are available only when the module is enabled and
  compatible.
- Mutating capabilities must require proposal review.
- Capabilities must declare permissions and risk.

## AgentActionProposal

Represents a reviewable assistant recommendation to change state.

**Fields**

- `id`: Stable proposal identifier.
- `sessionId`: Owning session.
- `messageId`: Assistant message that created it.
- `capabilityId`: Capability/action that would execute the proposal.
- `title`: User-visible proposal title.
- `summary`: Business impact summary.
- `risk`: `review-required`, `destructive`, `admin`.
- `parameters`: Structured proposal parameters.
- `diff`: Optional structured before/after or patch representation.
- `requiredPermissions`: Permissions required for approval/execution.
- `approvalStatus`: `draft`, `awaiting-approval`, `approved`, `denied`, `edited`, `expired`, `executed`, `failed`, `cancelled`.
- `approvedBy`, `approvedAt`: Approval metadata.
- `executionResultId`: Optional result reference.

**Validation**

- Cannot execute unless approved by an authorized user.
- Approval must be revalidated against current policy and permissions.
- Edits create a new proposal revision or preserve revision history.

## AgentAuditEvent

Records security, policy, and operational events.

**Fields**

- `id`: Stable event identifier.
- `sessionId`: Optional session.
- `proposalId`: Optional proposal.
- `userId`, `tenantId`: Actor boundary.
- `eventType`: `session-created`, `context-collected`, `policy-denied`, `message-sent`, `proposal-created`, `proposal-approved`, `proposal-denied`, `action-executed`, `action-failed`, `feedback-recorded`.
- `summary`: User-safe event summary.
- `metadata`: Structured metadata with sensitive values redacted.
- `createdAt`: Event timestamp.

**Validation**

- Audit events are append-only.
- Sensitive data and secrets must be redacted.
- Events must be queryable by administrators according to policy.

## ModuleAgentContribution

Describes module-contributed agent capabilities.

**Fields**

- `moduleId`: Owning module.
- `contextProviders`: Context sources exposed by the module.
- `promptStarters`: Suggested prompts for relevant surfaces.
- `capabilities`: Capability descriptors.
- `actions`: Reviewable action descriptors.
- `diagnostics`: Contribution diagnostics.

**Validation**

- Contributions must have stable IDs.
- Duplicate IDs are rejected or diagnosed.
- Contributions are filtered by module state, route relevance, permission, and
  policy.

## AgentPolicy

Administrative rules for agent availability, context, approvals, retention, and
provider usage.

**Fields**

- `id`: Policy identifier.
- `enabled`: Whether assistant features are available.
- `allowedCapabilities`: Capability allow/deny rules.
- `approvalRules`: Risk and permission requirements for proposals.
- `contextRules`: Allowed context sources, sensitivity limits, and redaction
  behavior.
- `retention`: Session/message/audit retention settings.
- `providerProfile`: Backend provider configuration reference.
- `updatedAt`, `updatedBy`: Change metadata.

**Validation**

- Disabling agent features must leave Studio usable.
- Policy changes affect new turns and invalidate stale proposals when required.
- Provider secrets are referenced indirectly and never exposed to Studio.

## State Transitions

### AgentSession

```text
active -> completed
active -> cancelled
active -> failed
active -> expired
```

### AgentMessage

```text
pending -> streaming -> completed
pending -> failed
streaming -> failed
streaming -> cancelled
```

### AgentActionProposal

```text
draft -> awaiting-approval -> approved -> executed
draft -> awaiting-approval -> denied
draft -> awaiting-approval -> edited -> awaiting-approval
awaiting-approval -> expired
approved -> failed
approved -> cancelled
```
