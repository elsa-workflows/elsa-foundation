declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export interface StudioHttpClient {
    requestJson<T>(url: string, init?: RequestInit): Promise<T>;
    getJson<T>(url: string, init?: RequestInit): Promise<T>;
    postJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
    putJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
    deleteJson<T>(url: string, init?: RequestInit): Promise<T>;
  }

  export interface StudioEndpointContext {
    baseUrl: string;
    http: StudioHttpClient;
  }

  export interface StudioContributionRegistry<T> {
    add(contribution: T): void;
    list(): T[];
  }

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
    nav: unknown;
    routes: StudioFeatureAreaRouteContribution[];
  }

  export interface StudioActivityInputDescriptor {
    name: string;
    displayName?: string | null;
    typeName: string;
    uiHint?: string | null;
    defaultSyntax?: string | null;
  }

  export interface StudioExpressionDescriptor {
    type: string;
    displayName?: string | null;
  }

  export interface StudioActivityPropertyEditorContext {
    activity: unknown;
    expressionDescriptors: StudioExpressionDescriptor[];
    readOnly?: boolean;
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
    readonly propertyEditors: StudioContributionRegistry<StudioActivityPropertyEditorContribution>;
    readonly dialogs: StudioDialogApi;
  }
}
