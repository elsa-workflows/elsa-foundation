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
    requestId?: string;
  }

  export interface StudioAiPromptResult {
    requestId: string;
    status: "completed" | "cancelled" | "failed";
    text: string;
    autoApply?: boolean;
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
    inputSchema?: unknown;
    execute?(input: Record<string, unknown>): Promise<unknown>;
  }

  export interface StudioHttpClient {
    requestJson<T>(url: string, init?: RequestInit): Promise<T>;
    getJson<T>(url: string): Promise<T>;
    postJson<T>(url: string, body: unknown): Promise<T>;
    putJson<T>(url: string, body: unknown): Promise<T>;
    deleteJson<T>(url: string): Promise<T>;
  }

  export interface StudioEndpointContext {
    baseUrl: string;
    http: StudioHttpClient;
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
    publishPromptResult(result: StudioAiPromptResult): void;
    onPromptResult(listener: (result: StudioAiPromptResult) => void): () => void;
  }

  export interface ElsaStudioModuleApi {
    readonly backend: StudioEndpointContext;
    readonly ai: StudioAiContributionApi;
  }
}
