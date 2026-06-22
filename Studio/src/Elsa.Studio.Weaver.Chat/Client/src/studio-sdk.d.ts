declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export type StudioAiPromptMode = "enqueue" | "steer";

  export interface StudioHttpClient {
    getJson<T>(url: string, init?: RequestInit): Promise<T>;
    postJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
  }

  export interface StudioEndpointContext {
    baseUrl: string;
    headers?: HeadersInit;
    http: StudioHttpClient;
  }

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
    mode?: StudioAiPromptMode;
    attachments?: StudioAiContextAttachment[];
    source?: {
      moduleId?: string;
      actionId?: string;
      label?: string;
    };
  }

  export interface StudioContributionRegistry<T> {
    add(contribution: T): void;
    list(): T[];
  }

  export interface StudioNavigationContribution {
    id: string;
    label: string;
    path: string;
    activePathPrefix?: string;
    order?: number;
    iconColor?: string;
  }

  export interface StudioRouteContribution {
    id: string;
    path: string;
    label: string;
    component: ComponentType;
  }

  export interface StudioPanelContribution {
    id: string;
    title: string;
    order?: number;
    component: ComponentType;
  }

  export interface StudioAiSurfaceContribution {
    id: string;
    title: string;
    placement: "route" | "panel" | "drawer" | "inline";
    moduleId?: string;
    order?: number;
  }

  export interface StudioAiContributionApi {
    readonly surfaces: StudioContributionRegistry<StudioAiSurfaceContribution>;
    readonly contextProviders: StudioContributionRegistry<unknown>;
    readonly promptActions: StudioContributionRegistry<unknown>;
    readonly tools: StudioContributionRegistry<unknown>;
    readonly proposalRenderers: StudioContributionRegistry<unknown>;
    dispatchPrompt(request: StudioAiPromptRequest): void;
    onPrompt(listener: (request: StudioAiPromptRequest) => void): () => void;
  }

  export interface ElsaStudioModuleApi {
    readonly backend: StudioEndpointContext;
    readonly navigation: StudioContributionRegistry<StudioNavigationContribution>;
    readonly routes: StudioContributionRegistry<StudioRouteContribution>;
    readonly panels: StudioContributionRegistry<StudioPanelContribution>;
    readonly ai: StudioAiContributionApi;
  }

  export function withDefaultHeaders(defaultHeaders: HeadersInit | undefined, init?: RequestInit): RequestInit;
}
