declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export interface StudioHttpClient {
    requestJson<T>(url: string, init?: RequestInit): Promise<T>;
    getJson<T>(url: string, init?: RequestInit): Promise<T>;
    postJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
    putJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
    deleteJson<T>(url: string, init?: RequestInit): Promise<T>;
    postForm<T>(url: string, body: FormData, init?: RequestInit): Promise<T>;
  }

  export interface StudioEndpointContext {
    baseUrl: string;
    http: StudioHttpClient;
  }

  export interface StudioNavigationContribution {
    id: string;
    label: string;
    path: string;
    activePathPrefix?: string;
    order?: number;
    iconColor?: string;
    parentId?: string;
  }

  export interface StudioRouteContribution {
    id: string;
    path: string;
    label: string;
    component: ComponentType;
  }

  export interface StudioContributionRegistry<T> {
    add(contribution: T): void;
    list(): T[];
  }

  export interface StudioFeatureAreaNavLeaf {
    id?: string;
    title: string;
    path: string;
    iconColor?: string;
    placeholder?: boolean;
  }

  export interface StudioFeatureAreaNavParent {
    id?: string;
    title: string;
    path: string;
    iconColor?: string;
    items: StudioFeatureAreaNavLeaf[];
  }

  export type StudioFeatureAreaNavContribution = StudioFeatureAreaNavLeaf | StudioFeatureAreaNavParent;

  export interface StudioFeatureAreaRouteContribution {
    id: string;
    path: string;
    label: string;
    component: ComponentType;
  }

  export interface StudioFeatureAreaContribution {
    id: string;
    title: string;
    description?: string;
    navGroup?: string;
    ownedPaths: string[];
    required?: boolean;
    defaultEnabled?: boolean;
    order?: number;
    nav: StudioFeatureAreaNavContribution;
    routes: StudioFeatureAreaRouteContribution[];
  }

  export interface StudioAiPromptRequest {
    message: string;
    agent?: string | null;
    mode?: "enqueue" | "steer";
    attachments?: Array<{
      id?: string;
      kind: string;
      referenceId?: string | null;
      scope?: string | null;
      activityId?: string | null;
      metadata?: Record<string, unknown>;
    }>;
    source?: {
      moduleId?: string;
      actionId?: string;
      label?: string;
    };
  }

  export interface StudioAiPromptActionContribution<TContext = unknown> {
    id: string;
    label: string;
    description?: string;
    placement: "shell" | "toolbar" | "inspector" | "empty-state" | "field-adornment" | "selection";
    contextKind: string;
    createPrompt(context: TContext): StudioAiPromptRequest | null;
  }

  export interface StudioAiContributionApi {
    readonly promptActions: StudioContributionRegistry<StudioAiPromptActionContribution>;
    dispatchPrompt(request: StudioAiPromptRequest): void;
    onPrompt?(listener: (request: StudioAiPromptRequest) => void): () => void;
  }

  export interface ElsaStudioModuleApi {
    readonly backend: StudioEndpointContext;
    readonly featureAreas: StudioContributionRegistry<StudioFeatureAreaContribution>;
    readonly navigation: StudioContributionRegistry<StudioNavigationContribution>;
    readonly routes: StudioContributionRegistry<StudioRouteContribution>;
    readonly ai: StudioAiContributionApi;
  }
}
