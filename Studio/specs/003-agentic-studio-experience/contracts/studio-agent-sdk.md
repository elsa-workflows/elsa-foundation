# Contract: Studio Weaver/Agent SDK

This contract extends the module-facing Studio SDK with Weaver contribution
registries. The visible assistant is **Weaver**; the `agent` property name is an
internal compatibility term for provider-neutral profiles, capabilities, and
module contributions. The contract follows the existing registry style used by
navigation, routes, dashboard widgets, panels, setting editors, and diagnostics.

## Module API extension

```ts
interface ElsaStudioModuleApi {
  readonly agent: StudioAgentRegistry;
}

interface StudioAgentRegistry {
  readonly contextProviders: StudioContributionRegistry<StudioAgentContextProviderContribution>;
  readonly promptStarters: StudioContributionRegistry<StudioAgentPromptStarterContribution>;
  readonly capabilities: StudioContributionRegistry<StudioAgentCapabilityContribution>;
  readonly actions: StudioContributionRegistry<StudioAgentActionContribution>;
}
```

## Context provider contribution

```ts
interface StudioAgentContextProviderContribution {
  id: string;
  moduleId?: string;
  displayName: string;
  order?: number;
  surfaces: string[];
  sensitivity: "public" | "internal" | "sensitive" | "secret-redacted";
  collect(context: StudioAgentContextRequest): Promise<StudioAgentContextAttachment[]>;
}
```

**Rules**

- Providers return summarized, bounded context rather than unfiltered screen
  state.
- Providers must label source, sensitivity, and target surface.
- Providers may return no attachments when the current surface is irrelevant.
- Host/backend policy can discard or redact returned context.

## Prompt starter contribution

```ts
interface StudioAgentPromptStarterContribution {
  id: string;
  moduleId?: string;
  label: string;
  prompt: string;
  surfaces: string[];
  order?: number;
  requiredCapabilities?: string[];
}
```

**Rules**

- Prompt starters are suggestions only; they do not bypass permission checks.
- Prompt starters are shown only when their surfaces and capabilities match the
  active context.

## Capability contribution

```ts
interface StudioAgentCapabilityContribution {
  id: string;
  moduleId?: string;
  displayName: string;
  description: string;
  kind: "answer" | "context" | "prompt-starter" | "proposal" | "action";
  risk: "read-only" | "review-required" | "destructive" | "admin";
  surfaces: string[];
  requiredPermissions?: string[];
}
```

**Rules**

- `review-required`, `destructive`, and `admin` capabilities must create
  proposals before execution.
- Capabilities are filtered by module status, compatibility, permission, tenant,
  and policy.
- Capability IDs must be stable and globally unique.

## Action contribution

```ts
interface StudioAgentActionContribution {
  id: string;
  capabilityId: string;
  displayName: string;
  description: string;
  risk: "review-required" | "destructive" | "admin";
  surfaces: string[];
  proposalSchema: unknown;
}
```

**Rules**

- Studio modules describe actions; privileged execution happens through the
  backend action/proposal contract.
- Actions cannot directly mutate Studio state from an assistant response.
- The UI must render a proposal review before any action execution request.

## Assistant UI contract

The host owns shared Weaver UI primitives:

- Weaver launcher in shell/command center.
- Conversation panel or drawer with message list, streaming state, composer,
  context chips, prompt starters, disabled state, and error state.
- Proposal review surface with impact summary, diff/parameters, risk labels,
  approval controls, denial reason, and execution progress.
- Feedback control on assistant messages and executed proposals.

Modules may contribute context/actions but must not render their own competing
global Weaver assistant.

## Accessibility requirements

- Launcher is keyboard reachable and announces availability.
- Opening the assistant moves focus to the composer or first actionable item.
- Closing restores focus to the invoking control.
- Streaming updates use polite announcements and do not steal focus.
- Proposal approval buttons expose risk and disabled reasons.
- Disabled/unavailable agent states remain readable and navigable.

## Filtering behavior

The host builds an active contribution set from:

1. Enabled and compatible module manifests.
2. Active route/resource surface.
3. Authenticated user permissions and tenant.
4. Backend bootstrap policy.
5. Contribution-specific capability/risk metadata.

Filtered contributions produce diagnostics for administrators but are not shown
as available user actions.
