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

  export interface StudioActivityPropertyDescriptor {
    name: string;
    typeName: string;
    displayName?: string | null;
    description?: string | null;
    order?: number;
    category?: string | null;
    isBrowsable?: boolean | null;
    isSynthetic?: boolean;
  }

  export interface StudioActivityInputDescriptor extends StudioActivityPropertyDescriptor {
    isWrapped?: boolean;
    uiHint?: string | null;
    defaultValue?: unknown;
    defaultSyntax?: string | null;
    isReadOnly?: boolean | null;
    storageDriverType?: string | null;
    uiSpecifications?: Record<string, unknown> | null;
  }

  export interface StudioActivityOutputDescriptor extends StudioActivityPropertyDescriptor {
  }

  export interface StudioActivityPortDescriptor {
    name: string;
    displayName?: string | null;
    type: string;
    isBrowsable?: boolean | null;
  }

  export interface StudioActivityDescriptor {
    typeName: string;
    namespace?: string;
    name?: string;
    version?: number;
    category?: string;
    displayName?: string | null;
    description?: string | null;
    icon?: string | null;
    iconName?: string | null;
    iconColor?: string | null;
    kind?: string;
    inputs: StudioActivityInputDescriptor[];
    outputs: StudioActivityOutputDescriptor[];
    ports: StudioActivityPortDescriptor[];
    customProperties?: Record<string, unknown>;
    constructionProperties?: Record<string, unknown>;
    isContainer?: boolean;
    isBrowsable?: boolean;
    isStart?: boolean;
    isTerminal?: boolean;
  }

  export interface StudioExpressionDescriptor {
    type: string;
    displayName?: string | null;
    description?: string | null;
  }

  export type StudioActivityPropertyEditorScope = "element" | "collection";

  export interface StudioActivityPropertyEditorContext {
    activity: unknown;
    expressionDescriptors: StudioExpressionDescriptor[];
    readOnly?: boolean;
    scope?: StudioActivityPropertyEditorScope;
  }

  export interface StudioActivityPropertyEditorProps {
    descriptor: StudioActivityInputDescriptor;
    value: unknown;
    disabled?: boolean;
    context: StudioActivityPropertyEditorContext;
    onChange(value: unknown): void;
  }

  export interface StudioActivityPropertyEditorContribution {
    id: string;
    order?: number;
    supports(descriptor: StudioActivityInputDescriptor, context: StudioActivityPropertyEditorContext): boolean;
    component: ComponentType<StudioActivityPropertyEditorProps>;
  }

  export type StudioWorkflowDesignerPanelSide = "left" | "right";

  export interface StudioWorkflowDesignerPanelProps<TContext = unknown> {
    context: TContext;
  }

  export interface StudioWorkflowDesignerPanelContribution<TContext = unknown> {
    id: string;
    title: string;
    side: StudioWorkflowDesignerPanelSide;
    order?: number;
    component: ComponentType<StudioWorkflowDesignerPanelProps<TContext>>;
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
    requestId?: string;
  }

  export interface StudioAiPromptResult {
    requestId: string;
    status: "completed" | "cancelled" | "failed";
    text: string;
    autoApply?: boolean;
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
    publishPromptResult(result: StudioAiPromptResult): void;
    onPromptResult(listener: (result: StudioAiPromptResult) => void): () => void;
  }

  export interface StudioConfirmOptions {
    title?: string;
    message: string;
    confirmLabel?: string;
    cancelLabel?: string;
    tone?: "default" | "danger";
  }

  export interface StudioPromptOptions {
    title?: string;
    message: string;
    confirmLabel?: string;
    cancelLabel?: string;
    defaultValue?: string;
    placeholder?: string;
  }

  export interface StudioAlertOptions {
    title?: string;
    message: string;
    confirmLabel?: string;
  }

  export interface StudioDialogApi {
    confirm(options: StudioConfirmOptions): Promise<boolean>;
    prompt(options: StudioPromptOptions): Promise<string | null>;
    alert(options: StudioAlertOptions): Promise<void>;
  }

  export interface ElsaStudioModuleApi {
    readonly backend: StudioEndpointContext;
    readonly featureAreas: StudioContributionRegistry<StudioFeatureAreaContribution>;
    readonly navigation: StudioContributionRegistry<StudioNavigationContribution>;
    readonly routes: StudioContributionRegistry<StudioRouteContribution>;
    readonly ai: StudioAiContributionApi;
    readonly dialogs: StudioDialogApi;
    readonly propertyEditors: StudioContributionRegistry<StudioActivityPropertyEditorContribution>;
    readonly workflowDesigner: {
      readonly panels: StudioContributionRegistry<StudioWorkflowDesignerPanelContribution>;
    };
  }
}
