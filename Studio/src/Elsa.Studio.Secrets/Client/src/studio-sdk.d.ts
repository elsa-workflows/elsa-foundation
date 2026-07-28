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
    editingMode: "literal" | "text" | "structured" | "reference";
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

  export type StudioExpressionEditorSurface = "inline" | "expanded";

  export interface StudioExpressionDocument {
    id: string; uri: string; draftId: string; activityId: string; propertyKey: string;
    expressionType: string; source: string; sourceVersion: number; contextVersion?: string;
  }
  export type StudioExpressionToolingState = "ready" | "supported-empty" | "unavailable" | "unauthorized" | "incompatible" | "stale" | "canceled";
  export type StudioExpressionSymbolKind = "value" | "function" | "filter" | "tag" | "namespace" | "member" | "keyword";
  export type StudioExpressionValueShapeKind = "unknown" | "scalar" | "object" | "collection" | "callable";
  export interface StudioExpressionToolingCapabilities { highlighting: boolean; completion: boolean; hover: boolean; signatures: boolean; formatting: boolean; localDiagnostics: boolean; semanticValidation: boolean; }
  export interface StudioExpressionToolingDescriptor { expressionType: string; moduleId: string; moduleVersion: string; contractMinVersion: number; contractMaxVersion: number; capabilities: StudioExpressionToolingCapabilities; catalogVersion?: string; permissionRevision?: string; hostPolicyRevision?: string; }
  export interface StudioExpressionPosition { line: number; column: number; }
  export interface StudioExpressionRange { start: StudioExpressionPosition; end: StudioExpressionPosition; }
  export interface StudioExpressionSignatureParameter { name: string; documentation?: string; shapeId?: string; optional?: boolean; }
  export interface StudioExpressionSignature { label: string; documentation?: string; returnShapeId?: string; parameters: StudioExpressionSignatureParameter[]; }
  export interface StudioExpressionSymbol { id: string; name: string; kind: StudioExpressionSymbolKind; documentation?: string; shapeId?: string; parentId?: string; sortText?: string; signatures?: StudioExpressionSignature[]; }
  export interface StudioExpressionValueShapeMember { name: string; documentation?: string; shapeId: string; }
  export interface StudioExpressionValueShape { id: string; kind: StudioExpressionValueShapeKind; displayName?: string; nullable: boolean; scalarType?: string; elementShapeId?: string; additionalMembers?: boolean; members: StudioExpressionValueShapeMember[]; }
  export interface StudioExpressionAuthoringContext { version: string; catalogVersion?: string; permissionRevision?: string; hostPolicyRevision?: string; capabilities?: StudioExpressionToolingCapabilities; expectedResultType?: string; expectedResultShape?: StudioExpressionValueShape; target?: StudioExpressionSymbol; rootSymbols?: StudioExpressionSymbol[]; workflowInputs: StudioExpressionSymbol[]; visibleVariables: StudioExpressionSymbol[]; visibleActivityOutputs: StudioExpressionSymbol[]; shapeReferences?: string[]; }
  export interface StudioExpressionToolingResult<T> { state: StudioExpressionToolingState; contractVersion: number; expressionType: string; moduleVersion?: string; catalogVersion?: string; contextVersion?: string; data?: T; }
  export interface StudioExpressionSymbolCatalogPage { symbols: StudioExpressionSymbol[]; nextCursor?: string; }
  export interface StudioExpressionValidationDiagnostic { severity: "info" | "warning" | "error"; code?: string; message: string; range?: StudioExpressionRange; documentId: string; sourceVersion: number; contextVersion: string; catalogVersion?: string; }
  export interface StudioExpressionValidationResult { documentId: string; sourceVersion: number; contextVersion: string; diagnostics: StudioExpressionValidationDiagnostic[]; }
  export interface StudioExpressionCompletionItem { label: string; detail?: string; documentation?: string; insertText?: string; kind?: StudioExpressionSymbolKind; }
  export interface StudioExpressionCompletionResult { items: StudioExpressionCompletionItem[]; }
  export interface StudioExpressionHoverResult { contents: string; range?: StudioExpressionRange; }
  export interface StudioExpressionToolingClient {
    describe(signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionToolingDescriptor[]>>;
    getCatalog(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, query?: string, cursor?: string, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionSymbolCatalogPage>>;
    getValueShape(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, shapeId: string, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionValueShape>>;
    getAuthoringContext(document: StudioExpressionDocument, state: unknown, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionAuthoringContext>>;
    getCompletions(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, position: StudioExpressionPosition, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionCompletionResult>>;
    getHover(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, position: StudioExpressionPosition, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionHoverResult>>;
    validate(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionValidationResult>>;
    invalidateAuthorization(revisions?: { permissionRevision?: string; hostPolicyRevision?: string }): void;
    dispose(): void;
  }

  export interface StudioExpressionEditorContext {
    syntax: string;
    surface: StudioExpressionEditorSurface;
    descriptor: StudioActivityInputDescriptor;
    activity: unknown;
    expressionDescriptors: StudioExpressionDescriptor[];
    readOnly?: boolean;
    document?: StudioExpressionDocument;
    authoringContext?: StudioExpressionToolingResult<StudioExpressionAuthoringContext>;
    validation?: StudioExpressionToolingResult<StudioExpressionValidationResult>;
    tooling?: StudioExpressionToolingClient;
    onFocus?(): void;
  }

  export interface StudioExpressionEditorProps {
    descriptor: StudioActivityInputDescriptor;
    syntax: string;
    value: unknown;
    disabled?: boolean;
    initialFocus?: boolean;
    context: StudioExpressionEditorContext;
    onChange(value: unknown): void;
    onExpand?(): void;
  }

  export interface StudioExpressionEditorContribution {
    id: string;
    order?: number;
    supports(context: StudioExpressionEditorContext): boolean;
    surfaces: Partial<Record<StudioExpressionEditorSurface, ComponentType<StudioExpressionEditorProps>>>;
    createDefaultValue?(context: StudioExpressionEditorContext): unknown;
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
    readonly expressionEditors: StudioContributionRegistry<StudioExpressionEditorContribution>;
    readonly dialogs: StudioDialogApi;
  }
}
