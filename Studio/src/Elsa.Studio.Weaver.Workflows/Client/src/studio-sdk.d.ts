declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export interface StudioAiContextAttachment {
    id?: string;
    kind: string;
    referenceId?: string | null;
    scope?: string | null;
    activityId?: string | null;
    metadata?: Record<string, unknown>;
  }

  export interface StudioAiPromptRequest {
    message: string;
    agent?: string | null;
    mode?: "enqueue" | "steer";
    attachments?: StudioAiContextAttachment[];
    source?: {
      moduleId?: string;
      actionId?: string;
      label?: string;
    };
  }

  export interface StudioAiContextProviderContribution {
    id: string;
    kind: string;
    label: string;
    description?: string;
    moduleId?: string;
    order?: number;
    createAttachment(reference: unknown): StudioAiContextAttachment | null;
  }

  export interface StudioAiPromptActionContribution<TContext = unknown> {
    id: string;
    label: string;
    description?: string;
    moduleId?: string;
    order?: number;
    placement: "shell" | "toolbar" | "inspector" | "empty-state" | "field-adornment" | "selection";
    contextKind: string;
    createPrompt(context: TContext): StudioAiPromptRequest | null;
  }

  export interface StudioAiToolContribution {
    name: string;
    displayName: string;
    description?: string;
    mutability: "read-only" | "proposal" | "administrative";
    dangerLevel: "low" | "medium" | "high" | "critical";
    tenantBehavior?: "tenant-scoped" | "host-scoped" | "cross-tenant-denied";
    permissions?: string[];
    agentScopes?: string[];
    moduleId?: string;
  }

  export interface StudioAiProposalRendererProps {
    proposal: {
      id: string;
      kind: string;
      status: string;
      rationale?: string | null;
      warnings?: string[];
      diagnostics?: Array<{ code?: string; message: string; severity?: string; path?: string | null }>;
      graphDiff?: {
        addedActivityIds?: string[];
        removedActivityIds?: string[];
        changedActivityIds?: string[];
      } | null;
    };
  }

  export interface StudioAiProposalRendererContribution {
    id: string;
    kind: string;
    moduleId?: string;
    component: ComponentType<StudioAiProposalRendererProps>;
  }

  export interface StudioContributionRegistry<T> {
    add(contribution: T): void;
    list(): T[];
  }

  export interface StudioAiContributionApi {
    readonly contextProviders: StudioContributionRegistry<StudioAiContextProviderContribution>;
    readonly promptActions: StudioContributionRegistry<StudioAiPromptActionContribution>;
    readonly tools: StudioContributionRegistry<StudioAiToolContribution>;
    readonly proposalRenderers: StudioContributionRegistry<StudioAiProposalRendererContribution>;
  }

  export interface ElsaStudioModuleApi {
    readonly ai: StudioAiContributionApi;
  }
}
